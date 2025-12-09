using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamKit2;

namespace DepotDownloader.Lib;

/// <summary>
///     Sections of Steam app info that can be queried.
/// </summary>
internal enum EAppInfoSection
{
    Common,
    Extended,
    Config,
    Depots
}

/// <summary>
///     Provides methods for querying Steam app information and account access.
/// </summary>
internal static class AppInfoService
{
    /// <summary>
    ///     Cache for password-protected branch depot sections.
    /// </summary>
    private static readonly Dictionary<(uint appId, string branch), KeyValue> PrivateBetaSectionCache = [];

    /// <summary>
    ///     Gets a specific section from Steam app info.
    /// </summary>
    internal static KeyValue GetAppSection(Steam3Session steam3, uint appId, EAppInfoSection section)
    {
        if (steam3?.AppInfo is null) return null;

        if (!steam3.AppInfo.TryGetValue(appId, out var app) || app is null) return null;

        var appinfo = app.KeyValues;
        var sectionKey = section switch
        {
            EAppInfoSection.Common => "common",
            EAppInfoSection.Extended => "extended",
            EAppInfoSection.Config => "config",
            EAppInfoSection.Depots => "depots",
            _ => throw new NotImplementedException()
        };
        return appinfo.Children.FirstOrDefault(c => c.Name == sectionKey);
    }

    /// <summary>
    ///     Gets the app name from Steam app info.
    /// </summary>
    internal static string GetAppName(Steam3Session steam3, uint appId)
    {
        var info = GetAppSection(steam3, appId, EAppInfoSection.Common);
        return info is null ? string.Empty : info["name"].AsString();
    }

    /// <summary>
    ///     Gets the build number for an app branch.
    /// </summary>
    internal static uint GetAppBuildNumber(Steam3Session steam3, uint appId, string branch)
    {
        if (appId == ContentDownloader.InvalidAppId)
            return 0;

        var depots = GetAppSection(steam3, appId, EAppInfoSection.Depots);
        if (depots is null)
            return 0;

        var branches = depots["branches"];
        if (branches == KeyValue.Invalid)
            return 0;

        var node = branches[branch];
        if (node == KeyValue.Invalid)
            return 0;

        var buildid = node["buildid"];
        if (buildid == KeyValue.Invalid || string.IsNullOrEmpty(buildid.Value))
            return 0;

        return uint.Parse(buildid.Value);
    }

    /// <summary>
    ///     Gets the proxy app ID for a depot if it references another app.
    /// </summary>
    internal static uint GetDepotProxyAppId(Steam3Session steam3, uint depotId, uint appId)
    {
        var depots = GetAppSection(steam3, appId, EAppInfoSection.Depots);
        var depotChild = depots?[depotId.ToString()];

        if (depotChild is null || depotChild == KeyValue.Invalid || depotChild["depotfromapp"] == KeyValue.Invalid)
            return ContentDownloader.InvalidAppId;

        return depotChild["depotfromapp"].AsUnsignedInteger();
    }

    /// <summary>
    ///     Gets the manifest ID for a depot on a specific branch.
    /// </summary>
    internal static async Task<ulong> GetDepotManifestAsync(
        Steam3Session steam3,
        uint depotId,
        uint appId,
        string branch,
        string betaPassword,
        IUserInterface userInterface)
    {
        var depots = GetAppSection(steam3, appId, EAppInfoSection.Depots);
        if (depots is null)
            return ContentDownloader.InvalidManifestId;

        var depotChild = depots[depotId.ToString()];

        if (depotChild == KeyValue.Invalid)
            return ContentDownloader.InvalidManifestId;

        // Shared depots can either provide manifests, or leave you relying on their parent app.
        if (depotChild["manifests"] == KeyValue.Invalid && depotChild["depotfromapp"] != KeyValue.Invalid)
        {
            var otherAppId = depotChild["depotfromapp"].AsUnsignedInteger();
            if (otherAppId == appId)
            {
                userInterface?.WriteLine("App {0}, Depot {1} has depotfromapp of {2}!",
                    appId, depotId, otherAppId);
                return ContentDownloader.InvalidManifestId;
            }

            await steam3.RequestAppInfo(otherAppId);

            return await GetDepotManifestAsync(steam3, depotId, otherAppId, branch, betaPassword, userInterface);
        }

        var manifests = depotChild["manifests"];

        if (manifests.Children.Count == 0)
            return ContentDownloader.InvalidManifestId;

        var node = manifests[branch]["gid"];

        // Non passworded branch, found the manifest
        if (node != KeyValue.Invalid && !string.IsNullOrEmpty(node.Value))
            return ulong.Parse(node.Value);

        // If we requested public branch, and it had no manifest, nothing to do
        if (string.Equals(branch, ContentDownloader.DefaultBranch, StringComparison.OrdinalIgnoreCase))
            return ContentDownloader.InvalidManifestId;

        // Either the branch just doesn't exist, or it has a password
        if (string.IsNullOrEmpty(betaPassword))
        {
            userInterface?.WriteLine(
                $"Branch {branch} for depot {depotId} was not found, either it does not exist or it has a password.");
            return ContentDownloader.InvalidManifestId;
        }

        if (!steam3.AppBetaPasswords.ContainsKey(branch))
        {
            await steam3.CheckAppBetaPassword(appId, betaPassword);

            if (!steam3.AppBetaPasswords.ContainsKey(branch))
            {
                userInterface?.WriteLine(
                    $"Error: Password was invalid for branch {branch} (or the branch does not exist)");
                return ContentDownloader.InvalidManifestId;
            }
        }

        // Got the password, request private depot section
        if (!PrivateBetaSectionCache.TryGetValue((appId, branch), out var privateDepotSection))
        {
            privateDepotSection = await steam3.GetPrivateBetaDepotSection(appId, branch);
            PrivateBetaSectionCache[(appId, branch)] = privateDepotSection;
        }

        depotChild = privateDepotSection[depotId.ToString()];

        if (depotChild == KeyValue.Invalid)
            return ContentDownloader.InvalidManifestId;

        manifests = depotChild["manifests"];

        if (manifests.Children.Count == 0)
            return ContentDownloader.InvalidManifestId;

        node = manifests[branch]["gid"];

        if (node == KeyValue.Invalid || string.IsNullOrEmpty(node.Value))
            return ContentDownloader.InvalidManifestId;

        return ulong.Parse(node.Value);
    }

    /// <summary>
    ///     Checks if the account has access to download a specific depot.
    /// </summary>
    internal static async Task<bool> AccountHasAccessAsync(Steam3Session steam3, uint appId, uint depotId)
    {
        if (steam3?.SteamUser?.SteamID is null)
            return false;

        List<uint> licenseQuery;
        if (steam3.SteamUser.SteamID.AccountType == EAccountType.AnonUser)
        {
            licenseQuery = [17906];
        }
        else
        {
            if (steam3.Licenses is null)
                return false;

            licenseQuery = [.. steam3.Licenses.Select(x => x.PackageID).Distinct()];
        }

        await steam3.RequestPackageInfo(licenseQuery);

        foreach (var license in licenseQuery)
            if (steam3.PackageInfo.TryGetValue(license, out var package) && package is not null)
            {
                if (package.KeyValues["appids"].Children.Any(child => child.AsUnsignedInteger() == depotId))
                    return true;

                if (package.KeyValues["depotids"].Children.Any(child => child.AsUnsignedInteger() == depotId))
                    return true;
            }

        var info = GetAppSection(steam3, appId, EAppInfoSection.Common);
        return info is not null && info["FreeToDownload"].AsBoolean();
    }

    /// <summary>
    ///     Clears the private beta section cache.
    /// </summary>
    internal static void ClearCache()
    {
        PrivateBetaSectionCache.Clear();
    }
}
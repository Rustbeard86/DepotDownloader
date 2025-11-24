using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.CDN;

namespace DepotDownloader;

internal class ContentDownloaderException(string value) : Exception(value);

internal static class ContentDownloader
{
    // Constants for validation sentinel values
    public const uint InvalidAppId = uint.MaxValue;
    public const uint InvalidDepotId = uint.MaxValue;
    public const ulong InvalidManifestId = ulong.MaxValue;
    public const string DefaultBranch = "public";

    // Directory structure constants
    private const string DefaultDownloadDir = "depots";
    private const string ConfigDir = ".DepotDownloader";
    private static readonly string StagingDir = Path.Combine(ConfigDir, "staging");

    // Configuration and session state
    public static readonly DownloadConfig Config = new();
    private static Steam3Session _steam3;
    private static CdnClientPool _cdnPool;

    // Performance optimization cache for password-protected branches
    // Avoids redundant API calls when downloading multiple depots from the same protected branch
    private static readonly Dictionary<(uint appId, string branch), KeyValue> PrivateBetaSectionCache = [];

    // Workshop content type filtering
    private static readonly FrozenSet<EWorkshopFileType> SupportedWorkshopFileTypes = new[]
    {
        EWorkshopFileType.Community,
        EWorkshopFileType.Art,
        EWorkshopFileType.Screenshot,
        EWorkshopFileType.Merch,
        EWorkshopFileType.IntegratedGuide,
        EWorkshopFileType.ControllerBinding
    }.ToFrozenSet();

    private static bool CreateDirectories(uint depotId, uint depotVersion, out string installDir)
    {
        installDir = null;
        try
        {
            if (string.IsNullOrWhiteSpace(Config.InstallDirectory))
            {
                Directory.CreateDirectory(DefaultDownloadDir);

                var depotPath = Path.Combine(DefaultDownloadDir, depotId.ToString());
                Directory.CreateDirectory(depotPath);

                installDir = Path.Combine(depotPath, depotVersion.ToString());
                Directory.CreateDirectory(installDir);

                Directory.CreateDirectory(Path.Combine(installDir, ConfigDir));
                Directory.CreateDirectory(Path.Combine(installDir, StagingDir));
            }
            else
            {
                Directory.CreateDirectory(Config.InstallDirectory);

                installDir = Config.InstallDirectory;

                Directory.CreateDirectory(Path.Combine(installDir, ConfigDir));
                Directory.CreateDirectory(Path.Combine(installDir, StagingDir));
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    private static bool TestIsFileIncluded(string filename)
    {
        if (!Config.UsingFileList)
            return true;

        filename = filename.Replace('\\', '/');

        if (Config.FilesToDownload.Contains(filename)) return true;

        foreach (var rgx in Config.FilesToDownloadRegex)
        {
            var m = rgx.Match(filename);

            if (m.Success)
                return true;
        }

        return false;
    }

    private static async Task<bool> AccountHasAccess(uint appId, uint depotId)
    {
        if (_steam3 == null || _steam3.SteamUser.SteamID == null)
            return false;

        List<uint> licenseQuery;
        if (_steam3.SteamUser.SteamID.AccountType == EAccountType.AnonUser)
        {
            licenseQuery = [17906];
        }
        else
        {
            if (_steam3.Licenses == null)
                return false;

            // Materialize to list to avoid multiple enumeration
            licenseQuery = [.. _steam3.Licenses.Select(x => x.PackageID).Distinct()];
        }

        await _steam3.RequestPackageInfo(licenseQuery);

        foreach (var license in licenseQuery)
            if (_steam3.PackageInfo.TryGetValue(license, out var package) && package != null)
            {
                if (package.KeyValues["appids"].Children.Any(child => child.AsUnsignedInteger() == depotId))
                    return true;

                if (package.KeyValues["depotids"].Children.Any(child => child.AsUnsignedInteger() == depotId))
                    return true;
            }

        // Check if this app is free to download without a license
        var info = GetSteam3AppSection(appId, EAppInfoSection.Common);
        return info != null && info["FreeToDownload"].AsBoolean();
    }

    private static KeyValue GetSteam3AppSection(uint appId, EAppInfoSection section)
    {
        if (_steam3?.AppInfo == null) return null;

        if (!_steam3.AppInfo.TryGetValue(appId, out var app) || app == null) return null;

        var appinfo = app.KeyValues;
        var sectionKey = section switch
        {
            EAppInfoSection.Common => "common",
            EAppInfoSection.Extended => "extended",
            EAppInfoSection.Config => "config",
            EAppInfoSection.Depots => "depots",
            _ => throw new NotImplementedException()
        };
        var sectionKv = appinfo.Children.FirstOrDefault(c => c.Name == sectionKey);
        return sectionKv;
    }

    private static uint GetSteam3AppBuildNumber(uint appId, string branch)
    {
        if (appId == InvalidAppId)
            return 0;

        var depots = GetSteam3AppSection(appId, EAppInfoSection.Depots);
        if (depots == null)
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

    private static uint GetSteam3DepotProxyAppId(uint depotId, uint appId)
    {
        var depots = GetSteam3AppSection(appId, EAppInfoSection.Depots);
        var depotChild = depots[depotId.ToString()];

        if (depotChild == KeyValue.Invalid || depotChild["depotfromapp"] == KeyValue.Invalid)
            return InvalidAppId;

        return depotChild["depotfromapp"].AsUnsignedInteger();
    }

    private static async Task<ulong> GetSteam3DepotManifest(uint depotId, uint appId, string branch)
    {
        var depots = GetSteam3AppSection(appId, EAppInfoSection.Depots);
        if (depots == null)
            return InvalidManifestId;

        var depotChild = depots[depotId.ToString()];

        if (depotChild == KeyValue.Invalid)
            return InvalidManifestId;

        // Shared depots can either provide manifests, or leave you relying on their parent app.
        // It seems that with the latter, "sharedinstall" will exist (and equals 2 in the one existance I know of).
        // Rather than relay on the unknown sharedinstall key, just look for manifests. Test cases: 111710, 346680.
        if (depotChild["manifests"] == KeyValue.Invalid && depotChild["depotfromapp"] != KeyValue.Invalid)
        {
            var otherAppId = depotChild["depotfromapp"].AsUnsignedInteger();
            if (otherAppId == appId)
            {
                // This shouldn't ever happen, but ya never know with Valve. Don't infinite loop.
                Console.WriteLine("App {0}, Depot {1} has depotfromapp of {2}!",
                    appId, depotId, otherAppId);
                return InvalidManifestId;
            }

            await _steam3.RequestAppInfo(otherAppId);

            return await GetSteam3DepotManifest(depotId, otherAppId, branch);
        }

        var manifests = depotChild["manifests"];

        if (manifests.Children.Count == 0)
            return InvalidManifestId;

        var node = manifests[branch]["gid"];

        // Non passworded branch, found the manifest
        if (node != KeyValue.Invalid && !string.IsNullOrEmpty(node.Value))
            return ulong.Parse(node.Value);

        // If we requested public branch, and it had no manifest, nothing to do
        if (string.Equals(branch, DefaultBranch, StringComparison.OrdinalIgnoreCase))
            return InvalidManifestId;

        // Either the branch just doesn't exist, or it has a password
        if (string.IsNullOrEmpty(Config.BetaPassword))
        {
            Console.WriteLine(
                $"Branch {branch} for depot {depotId} was not found, either it does not exist or it has a password.");
            return InvalidManifestId;
        }

        if (!_steam3.AppBetaPasswords.ContainsKey(branch))
        {
            // Submit the password to Steam now to get encryption keys
            await _steam3.CheckAppBetaPassword(appId, Config.BetaPassword);

            if (!_steam3.AppBetaPasswords.ContainsKey(branch))
            {
                Console.WriteLine($"Error: Password was invalid for branch {branch} (or the branch does not exist)");
                return InvalidManifestId;
            }
        }

        // Got the password, request private depot section
        if (!PrivateBetaSectionCache.TryGetValue((appId, branch), out var privateDepotSection))
        {
            privateDepotSection = await _steam3.GetPrivateBetaDepotSection(appId, branch);
            PrivateBetaSectionCache[(appId, branch)] = privateDepotSection;
        }

        // Now repeat the same code to get the manifest gid from depot section
        depotChild = privateDepotSection[depotId.ToString()];

        if (depotChild == KeyValue.Invalid)
            return InvalidManifestId;

        manifests = depotChild["manifests"];

        if (manifests.Children.Count == 0)
            return InvalidManifestId;

        node = manifests[branch]["gid"];

        if (node == KeyValue.Invalid || string.IsNullOrEmpty(node.Value))
            return InvalidManifestId;

        return ulong.Parse(node.Value);
    }

    private static string GetAppName(uint appId)
    {
        var info = GetSteam3AppSection(appId, EAppInfoSection.Common);
        return info == null ? string.Empty : info["name"].AsString();
    }

    public static bool InitializeSteam3(string username, string password)
    {
        string loginToken = null;

        if (username != null && Config.RememberPassword)
            _ = AccountSettingsStore.Instance.LoginTokens.TryGetValue(username, out loginToken);

        _steam3 = new Steam3Session(
            new SteamUser.LogOnDetails
            {
                Username = username,
                Password = loginToken == null ? password : null,
                ShouldRememberPassword = Config.RememberPassword,
                AccessToken = loginToken,
                LoginID = Config.LoginId ?? 0x534B32 // "SK2"
            }
        );

        if (!_steam3.WaitForCredentials())
        {
            Console.WriteLine("Unable to get steam3 credentials.");
            return false;
        }

        Task.Run(_steam3.TickCallbacks);

        return true;
    }

    public static void ShutdownSteam3()
    {
        _steam3?.Disconnect();
    }

    private static async Task ProcessPublishedFileAsync(uint appId, ulong publishedFileId,
        List<ValueTuple<string, string>> fileUrls, List<ulong> contentFileIds)
    {
        var details = await _steam3.GetPublishedFileDetails(appId, publishedFileId);
        var fileType = (EWorkshopFileType)details.file_type;

        if (fileType == EWorkshopFileType.Collection)
        {
            foreach (var child in details.children)
                await ProcessPublishedFileAsync(appId, child.publishedfileid, fileUrls, contentFileIds);
        }
        else if (SupportedWorkshopFileTypes.Contains(fileType))
        {
            if (!string.IsNullOrEmpty(details.file_url))
                fileUrls.Add((details.filename, details.file_url));
            else if (details.hcontent_file > 0)
                contentFileIds.Add(details.hcontent_file);
            else
                Console.WriteLine("Unable to locate manifest ID for published file {0}", publishedFileId);
        }
        else
        {
            Console.WriteLine("Published file {0} has unsupported file type {1}. Skipping file", publishedFileId,
                fileType);
        }
    }

    public static async Task DownloadPubfileAsync(uint appId, ulong publishedFileId)
    {
        List<ValueTuple<string, string>> fileUrls = [];
        List<ulong> contentFileIds = [];

        await ProcessPublishedFileAsync(appId, publishedFileId, fileUrls, contentFileIds);

        foreach (var item in fileUrls) await DownloadWebFile(appId, item.Item1, item.Item2);

        if (contentFileIds.Count > 0)
        {
            var depotManifestIds = contentFileIds.Select(id => (appId, id)).ToList();
            await DownloadAppAsync(appId, depotManifestIds, DefaultBranch, null, null, null, false, true);
        }
    }

    public static async Task DownloadUgcAsync(uint appId, ulong ugcId)
    {
        SteamCloud.UGCDetailsCallback details = null;

        if (_steam3?.SteamUser?.SteamID?.AccountType != EAccountType.AnonUser)
        {
            if (_steam3 != null) details = await _steam3.GetUgcDetails(ugcId);
        }
        else
        {
            Console.WriteLine($"Unable to query UGC details for {ugcId} from an anonymous account");
        }

        if (!string.IsNullOrEmpty(details?.URL))
            await DownloadWebFile(appId, details.FileName, details.URL);
        else
            await DownloadAppAsync(appId, [(appId, ugcId)], DefaultBranch, null, null, null, false, true);
    }

    private static async Task DownloadWebFile(uint appId, string fileName, string url)
    {
        if (!CreateDirectories(appId, 0, out var installDir))
        {
            Console.WriteLine("Error: Unable to create install directories!");
            return;
        }

        var stagingDir = Path.Combine(installDir, StagingDir);
        var fileStagingPath = Path.Combine(stagingDir, fileName);
        var fileFinalPath = Path.Combine(installDir, fileName);

        var finalPathDirectory = Path.GetDirectoryName(fileFinalPath);
        if (!string.IsNullOrEmpty(finalPathDirectory))
            Directory.CreateDirectory(finalPathDirectory);

        var stagingPathDirectory = Path.GetDirectoryName(fileStagingPath);
        if (!string.IsNullOrEmpty(stagingPathDirectory))
            Directory.CreateDirectory(stagingPathDirectory);

        await using (var file = File.OpenWrite(fileStagingPath))
        {
            using var client = HttpClientFactory.CreateHttpClient();
            Console.WriteLine("Downloading {0}", fileName);
            var responseStream = await client.GetStreamAsync(url);
            await responseStream.CopyToAsync(file);
        }

        if (File.Exists(fileFinalPath)) File.Delete(fileFinalPath);

        File.Move(fileStagingPath, fileFinalPath);
    }

    public static async Task DownloadAppAsync(uint appId, List<(uint depotId, ulong manifestId)> depotManifestIds,
        string branch, string os, string arch, string language, bool lv, bool isUgc)
    {
        _cdnPool = new CdnClientPool(_steam3, appId);

        // Load our configuration data containing the depots currently installed
        var configPath = Config.InstallDirectory;
        if (string.IsNullOrWhiteSpace(configPath)) configPath = DefaultDownloadDir;

        Directory.CreateDirectory(Path.Combine(configPath, ConfigDir));
        DepotConfigStore.LoadFromFile(Path.Combine(configPath, ConfigDir, "depot.config"));

        await _steam3?.RequestAppInfo(appId);

        if (!await AccountHasAccess(appId, appId))
        {
            if (_steam3.SteamUser.SteamID.AccountType != EAccountType.AnonUser &&
                await _steam3.RequestFreeAppLicense(appId))
            {
                Console.WriteLine("Obtained FreeOnDemand license for app {0}", appId);

                // Fetch app info again in case we didn't get it fully without a license.
                await _steam3.RequestAppInfo(appId, true);
            }
            else
            {
                var contentName = GetAppName(appId);
                throw new ContentDownloaderException(string.Format("App {0} ({1}) is not available from this account.",
                    appId, contentName));
            }
        }

        var hasSpecificDepots = depotManifestIds.Count > 0;
        var depotIdsFound = new List<uint>();
        var depotIdsExpected = depotManifestIds.Select(x => x.depotId).ToList();
        var depots = GetSteam3AppSection(appId, EAppInfoSection.Depots);

        if (isUgc)
        {
            var workshopDepot = depots["workshopdepot"].AsUnsignedInteger();
            if (workshopDepot != 0 && !depotIdsExpected.Contains(workshopDepot))
            {
                depotIdsExpected.Add(workshopDepot);
                depotManifestIds = depotManifestIds.Select(pair => (workshopDepot, pair.manifestId)).ToList();
            }

            depotIdsFound.AddRange(depotIdsExpected);
        }
        else
        {
            Console.WriteLine("Using app branch: '{0}'.", branch);

            if (depots != null)
                foreach (var depotSection in depots.Children)
                {
                    if (depotSection.Children.Count == 0)
                        continue;

                    if (!uint.TryParse(depotSection.Name, out var id))
                        continue;

                    switch (hasSpecificDepots)
                    {
                        case true when !depotIdsExpected.Contains(id):
                            continue;
                        case false:
                        {
                            var depotConfig = depotSection["config"];
                            if (depotConfig != KeyValue.Invalid)
                            {
                                if (!Config.DownloadAllPlatforms &&
                                    depotConfig["oslist"] != KeyValue.Invalid &&
                                    !string.IsNullOrWhiteSpace(depotConfig["oslist"].Value))
                                {
                                    var oslist = depotConfig["oslist"].Value.Split(',');
                                    if (Array.IndexOf(oslist, os ?? Util.GetSteamOs()) == -1)
                                        continue;
                                }

                                if (!Config.DownloadAllArchs &&
                                    depotConfig["osarch"] != KeyValue.Invalid &&
                                    !string.IsNullOrWhiteSpace(depotConfig["osarch"].Value))
                                {
                                    var depotArch = depotConfig["osarch"].Value;
                                    if (depotArch != (arch ?? Util.GetSteamArch()))
                                        continue;
                                }

                                if (!Config.DownloadAllLanguages &&
                                    depotConfig["language"] != KeyValue.Invalid &&
                                    !string.IsNullOrWhiteSpace(depotConfig["language"].Value))
                                {
                                    var depotLang = depotConfig["language"].Value;
                                    if (depotLang != (language ?? "english"))
                                        continue;
                                }

                                if (!lv &&
                                    depotConfig["lowviolence"] != KeyValue.Invalid &&
                                    depotConfig["lowviolence"].AsBoolean())
                                    continue;
                            }

                            break;
                        }
                    }

                    depotIdsFound.Add(id);

                    if (!hasSpecificDepots)
                        depotManifestIds.Add((id, InvalidManifestId));
                }

            if (depotManifestIds.Count == 0 && !hasSpecificDepots)
                throw new ContentDownloaderException(string.Format("Couldn't find any depots to download for app {0}",
                    appId));

            if (depotIdsFound.Count < depotIdsExpected.Count)
            {
                var remainingDepotIds = depotIdsExpected.Except(depotIdsFound);
                throw new ContentDownloaderException(string.Format("Depot {0} not listed for app {1}",
                    string.Join(", ", remainingDepotIds), appId));
            }
        }

        var infos = new List<DepotDownloadInfo>();

        foreach (var (depotId, manifestId) in depotManifestIds)
        {
            var info = await GetDepotInfo(depotId, appId, manifestId, branch);
            if (info != null) infos.Add(info);
        }

        Console.WriteLine();

        try
        {
            await DownloadSteam3Async(infos).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("App {0} was not completely downloaded.", appId);
            throw;
        }
    }

    private static async Task<DepotDownloadInfo> GetDepotInfo(uint depotId, uint appId, ulong manifestId, string branch)
    {
        if (_steam3 != null && appId != InvalidAppId) await _steam3.RequestAppInfo(appId);

        if (!await AccountHasAccess(appId, depotId))
        {
            Console.WriteLine("Depot {0} is not available from this account.", depotId);

            return null;
        }

        if (manifestId == InvalidManifestId)
        {
            manifestId = await GetSteam3DepotManifest(depotId, appId, branch);
            if (manifestId == InvalidManifestId &&
                !string.Equals(branch, DefaultBranch, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Warning: Depot {0} does not have branch named \"{1}\". Trying {2} branch.", depotId,
                    branch, DefaultBranch);
                branch = DefaultBranch;
                manifestId = await GetSteam3DepotManifest(depotId, appId, branch);
            }

            if (manifestId == InvalidManifestId)
            {
                Console.WriteLine("Depot {0} missing public subsection or manifest section.", depotId);
                return null;
            }
        }

        await _steam3.RequestDepotKey(depotId, appId);
        if (!_steam3.DepotKeys.TryGetValue(depotId, out var depotKey))
        {
            Console.WriteLine("No valid depot key for {0}, unable to download.", depotId);
            return null;
        }

        var uVersion = GetSteam3AppBuildNumber(appId, branch);

        if (!CreateDirectories(depotId, uVersion, out var installDir))
        {
            Console.WriteLine("Error: Unable to create install directories!");
            return null;
        }

        // For depots that are proxied through depotfromapp, we still need to resolve the proxy app id, unless the app is freetodownload
        var containingAppId = appId;
        var proxyAppId = GetSteam3DepotProxyAppId(depotId, appId);
        if (proxyAppId != InvalidAppId)
        {
            var common = GetSteam3AppSection(appId, EAppInfoSection.Common);
            if (common == null || !common["FreeToDownload"].AsBoolean()) containingAppId = proxyAppId;
        }

        return new DepotDownloadInfo(depotId, containingAppId, manifestId, branch, installDir, depotKey);
    }

    private static async Task DownloadSteam3Async(List<DepotDownloadInfo> depots)
    {
        Ansi.Progress(Ansi.ProgressState.Indeterminate);

        await _cdnPool.UpdateServerList();

        var cts = new CancellationTokenSource();
        var downloadCounter = new GlobalDownloadCounter();
        var depotsToDownload = new List<DepotFilesData>(depots.Count);
        var allFileNamesAllDepots = new HashSet<string>();

        // First, fetch all the manifests for each depot (including previous manifests) and perform the initial setup
        foreach (var depot in depots)
        {
            var depotFileData = await ProcessDepotManifestAndFiles(cts, depot, downloadCounter);

            if (depotFileData != null)
            {
                depotsToDownload.Add(depotFileData);
                allFileNamesAllDepots.UnionWith(depotFileData.AllFileNames);
            }

            cts.Token.ThrowIfCancellationRequested();
        }

        // If we're about to write all the files to the same directory, we will need to first de-duplicate any files by path
        // This is in last-depot-wins order, from Steam or the list of depots supplied by the user
        if (!string.IsNullOrWhiteSpace(Config.InstallDirectory) && depotsToDownload.Count > 0)
        {
            var claimedFileNames = new HashSet<string>();

            for (var i = depotsToDownload.Count - 1; i >= 0; i--)
            {
                // For each depot, remove all files from the list that have been claimed by a later depot
                depotsToDownload[i].FilteredFiles.RemoveAll(file => claimedFileNames.Contains(file.FileName));

                claimedFileNames.UnionWith(depotsToDownload[i].AllFileNames);
            }
        }

        foreach (var depotFileData in depotsToDownload)
            await DownloadSteam3AsyncDepotFiles(cts, downloadCounter, depotFileData, allFileNamesAllDepots);

        Ansi.Progress(Ansi.ProgressState.Hidden);

        Console.WriteLine("Total downloaded: {0} bytes ({1} bytes uncompressed) from {2} depots",
            downloadCounter.TotalBytesCompressed, downloadCounter.TotalBytesUncompressed, depots.Count);
    }

    private static async Task<DepotFilesData> ProcessDepotManifestAndFiles(CancellationTokenSource cts,
        DepotDownloadInfo depot, GlobalDownloadCounter downloadCounter)
    {
        var depotCounter = new DepotDownloadCounter();

        Console.WriteLine("Processing depot {0}", depot.DepotId);

        DepotManifest oldManifest = null;
        DepotManifest newManifest = null;
        var configDir = Path.Combine(depot.InstallDir, ConfigDir);

        var lastManifestId = InvalidManifestId;
        DepotConfigStore.Instance.InstalledManifestIDs.TryGetValue(depot.DepotId, out lastManifestId);

        // In case we have an early exit, this will force equiv of verifyall next run.
        DepotConfigStore.Instance.InstalledManifestIDs[depot.DepotId] = InvalidManifestId;
        DepotConfigStore.Save();

        if (lastManifestId != InvalidManifestId)
        {
            // We only have to show this warning if the old manifest ID was different
            var badHashWarning = lastManifestId != depot.ManifestId;
            oldManifest = Util.LoadManifestFromFile(configDir, depot.DepotId, lastManifestId, badHashWarning);
        }

        if (lastManifestId == depot.ManifestId && oldManifest != null)
        {
            newManifest = oldManifest;
            Console.WriteLine("Already have manifest {0} for depot {1}.", depot.ManifestId, depot.DepotId);
        }
        else
        {
            newManifest = Util.LoadManifestFromFile(configDir, depot.DepotId, depot.ManifestId, true);

            if (newManifest != null)
            {
                Console.WriteLine("Already have manifest {0} for depot {1}.", depot.ManifestId, depot.DepotId);
            }
            else
            {
                Console.WriteLine($"Downloading depot {depot.DepotId} manifest");

                ulong manifestRequestCode = 0;
                var manifestRequestCodeExpiration = DateTime.MinValue;

                do
                {
                    cts.Token.ThrowIfCancellationRequested();

                    Server connection = null;

                    try
                    {
                        connection = _cdnPool.GetConnection();

                        string cdnToken = null;
                        if (_steam3.CdnAuthTokens.TryGetValue((depot.DepotId, connection.Host),
                                out var authTokenCallbackPromise))
                        {
                            var result = await authTokenCallbackPromise.Task;
                            cdnToken = result.Token;
                        }

                        var now = DateTime.Now;

                        // In order to download this manifest, we need the current manifest request code
                        // The manifest request code is only valid for a specific period in time
                        if (manifestRequestCode == 0 || now >= manifestRequestCodeExpiration)
                        {
                            manifestRequestCode = await _steam3.GetDepotManifestRequestCodeAsync(
                                depot.DepotId,
                                depot.AppId,
                                depot.ManifestId,
                                depot.Branch);
                            // This code will hopefully be valid for one period following the issuing period
                            manifestRequestCodeExpiration = now.Add(TimeSpan.FromMinutes(5));

                            // If we could not get the manifest code, this is a fatal error
                            if (manifestRequestCode == 0) cts.Cancel();
                        }

                        DebugLog.WriteLine("ContentDownloader",
                            "Downloading manifest {0} from {1} with {2}",
                            depot.ManifestId,
                            connection,
                            _cdnPool.ProxyServer != null ? _cdnPool.ProxyServer : "no proxy");
                        newManifest = await _cdnPool.CdnClient.DownloadManifestAsync(
                            depot.DepotId,
                            depot.ManifestId,
                            manifestRequestCode,
                            connection,
                            depot.DepotKey,
                            _cdnPool.ProxyServer,
                            cdnToken).ConfigureAwait(false);

                        _cdnPool.ReturnConnection(connection);
                    }
                    catch (TaskCanceledException)
                    {
                        Console.WriteLine("Connection timeout downloading depot manifest {0} {1}. Retrying.",
                            depot.DepotId, depot.ManifestId);
                    }
                    catch (SteamKitWebRequestException e)
                    {
                        // If the CDN returned 403, attempt to get a cdn auth if we didn't yet
                        if (e.StatusCode == HttpStatusCode.Forbidden &&
                            !_steam3.CdnAuthTokens.ContainsKey((depot.DepotId, connection.Host)))
                        {
                            await _steam3.RequestCdnAuthToken(depot.AppId, depot.DepotId, connection);

                            _cdnPool.ReturnConnection(connection);

                            continue;
                        }

                        _cdnPool.ReturnBrokenConnection(connection);

                        if (e.StatusCode == HttpStatusCode.Unauthorized || e.StatusCode == HttpStatusCode.Forbidden)
                        {
                            Console.WriteLine("Encountered {2} for depot manifest {0} {1}. Aborting.", depot.DepotId,
                                depot.ManifestId, (int)e.StatusCode);
                            break;
                        }

                        if (e.StatusCode == HttpStatusCode.NotFound)
                        {
                            Console.WriteLine("Encountered 404 for depot manifest {0} {1}. Aborting.", depot.DepotId,
                                depot.ManifestId);
                            break;
                        }

                        Console.WriteLine("Encountered error downloading depot manifest {0} {1}: {2}", depot.DepotId,
                            depot.ManifestId, e.StatusCode);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        _cdnPool.ReturnBrokenConnection(connection);
                        Console.WriteLine("Encountered error downloading manifest for depot {0} {1}: {2}",
                            depot.DepotId, depot.ManifestId, e.Message);
                    }
                } while (newManifest == null);

                if (newManifest == null)
                {
                    Console.WriteLine("\nUnable to download manifest {0} for depot {1}", depot.ManifestId,
                        depot.DepotId);
                    cts.Cancel();
                }

                // Throw the cancellation exception if requested so that this task is marked failed
                cts.Token.ThrowIfCancellationRequested();

                Util.SaveManifestToFile(configDir, newManifest);
            }
        }

        Console.WriteLine("Manifest {0} ({1})", depot.ManifestId, newManifest.CreationTime);

        if (Config.DownloadManifestOnly)
        {
            DumpManifestToTextFile(depot, newManifest);
            return null;
        }

        var stagingDir = Path.Combine(depot.InstallDir, StagingDir);

        var filesAfterExclusions = newManifest.Files.AsParallel().Where(f => TestIsFileIncluded(f.FileName)).ToList();
        var allFileNames = new HashSet<string>(filesAfterExclusions.Count);

        // Pre-process
        filesAfterExclusions.ForEach(file =>
        {
            allFileNames.Add(file.FileName);

            var fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
            var fileStagingPath = Path.Combine(stagingDir, file.FileName);

            if (file.Flags.HasFlag(EDepotFileFlag.Directory))
            {
                Directory.CreateDirectory(fileFinalPath);
                Directory.CreateDirectory(fileStagingPath);
            }
            else
            {
                // Some manifests don't explicitly include all necessary directories
                var finalPathDirectory = Path.GetDirectoryName(fileFinalPath);
                if (!string.IsNullOrEmpty(finalPathDirectory))
                    Directory.CreateDirectory(finalPathDirectory);

                var stagingPathDirectory = Path.GetDirectoryName(fileStagingPath);
                if (!string.IsNullOrEmpty(stagingPathDirectory))
                    Directory.CreateDirectory(stagingPathDirectory);

                downloadCounter.CompleteDownloadSize += file.TotalSize;
                depotCounter.CompleteDownloadSize += file.TotalSize;
            }
        });

        return new DepotFilesData
        {
            DepotDownloadInfo = depot,
            DepotCounter = depotCounter,
            StagingDir = stagingDir,
            Manifest = newManifest,
            PreviousManifest = oldManifest,
            FilteredFiles = filesAfterExclusions,
            AllFileNames = allFileNames
        };
    }

    private static async Task DownloadSteam3AsyncDepotFiles(CancellationTokenSource cts,
        GlobalDownloadCounter downloadCounter, DepotFilesData depotFilesData, HashSet<string> allFileNamesAllDepots)
    {
        var depot = depotFilesData.DepotDownloadInfo;
        var depotCounter = depotFilesData.DepotCounter;

        Console.WriteLine("Downloading depot {0}", depot.DepotId);

        var files = depotFilesData.FilteredFiles.Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory)).ToArray();
        var networkChunkQueue =
            new ConcurrentQueue<(FileStreamData fileStreamData, DepotManifest.FileData fileData, DepotManifest.ChunkData
                chunk)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Config.MaxDownloads,
            CancellationToken = cts.Token
        };

        await Parallel.ForEachAsync(files, parallelOptions, async (file, cancellationToken) =>
        {
            await Task.Yield();
            DownloadSteam3AsyncDepotFile(cts, downloadCounter, depotFilesData, file, networkChunkQueue);
        });

        await Parallel.ForEachAsync(networkChunkQueue, parallelOptions, async (q, cancellationToken) =>
        {
            await DownloadSteam3AsyncDepotFileChunk(
                cts, downloadCounter, depotFilesData,
                q.fileData, q.fileStreamData, q.chunk
            );
        });

        // Check for deleted files if updating the depot.
        if (depotFilesData.PreviousManifest != null)
        {
            var previousFilteredFiles = depotFilesData.PreviousManifest.Files.AsParallel()
                .Where(f => TestIsFileIncluded(f.FileName)).Select(f => f.FileName).ToHashSet();

            // Check if we are writing to a single output directory. If not, each depot folder is managed independently
            if (string.IsNullOrWhiteSpace(Config.InstallDirectory))
                // Of the list of files in the previous manifest, remove any file names that exist in the current set of all file names
                previousFilteredFiles.ExceptWith(depotFilesData.AllFileNames);
            else
                // Of the list of files in the previous manifest, remove any file names that exist in the current set of all file names across all depots being downloaded
                previousFilteredFiles.ExceptWith(allFileNamesAllDepots);

            foreach (var existingFileName in previousFilteredFiles)
            {
                var fileFinalPath = Path.Combine(depot.InstallDir, existingFileName);

                if (!File.Exists(fileFinalPath))
                    continue;

                File.Delete(fileFinalPath);
                Console.WriteLine("Deleted {0}", fileFinalPath);
            }
        }

        DepotConfigStore.Instance.InstalledManifestIDs[depot.DepotId] = depot.ManifestId;
        DepotConfigStore.Save();

        Console.WriteLine("Depot {0} - Downloaded {1} bytes ({2} bytes uncompressed)", depot.DepotId,
            depotCounter.DepotBytesCompressed, depotCounter.DepotBytesUncompressed);
    }

    private static void DownloadSteam3AsyncDepotFile(
        CancellationTokenSource cts,
        GlobalDownloadCounter downloadCounter,
        DepotFilesData depotFilesData,
        DepotManifest.FileData file,
        ConcurrentQueue<(FileStreamData, DepotManifest.FileData, DepotManifest.ChunkData)> networkChunkQueue)
    {
        cts.Token.ThrowIfCancellationRequested();

        var depot = depotFilesData.DepotDownloadInfo;
        var stagingDir = depotFilesData.StagingDir;
        var depotDownloadCounter = depotFilesData.DepotCounter;
        var oldProtoManifest = depotFilesData.PreviousManifest;
        DepotManifest.FileData oldManifestFile = null;
        if (oldProtoManifest != null)
            oldManifestFile = oldProtoManifest.Files.SingleOrDefault(f => f.FileName == file.FileName);

        var fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
        var fileStagingPath = Path.Combine(stagingDir, file.FileName);

        // This may still exist if the previous run exited before cleanup
        if (File.Exists(fileStagingPath)) File.Delete(fileStagingPath);

        List<DepotManifest.ChunkData> neededChunks;
        var fi = new FileInfo(fileFinalPath);
        var fileDidExist = fi.Exists;
        if (!fileDidExist)
        {
            Console.WriteLine("Pre-allocating {0}", fileFinalPath);

            // create new file. need all chunks
            using var fs = File.Create(fileFinalPath);
            try
            {
                fs.SetLength((long)file.TotalSize);
            }
            catch (IOException ex)
            {
                throw new ContentDownloaderException(string.Format("Failed to allocate file {0}: {1}", fileFinalPath,
                    ex.Message));
            }

            neededChunks = new List<DepotManifest.ChunkData>(file.Chunks);
        }
        else
        {
            // open existing
            if (oldManifestFile != null)
            {
                neededChunks = [];

                var hashMatches = oldManifestFile.FileHash.SequenceEqual(file.FileHash);
                if (Config.VerifyAll || !hashMatches)
                {
                    // we have a version of this file, but it doesn't fully match what we want
                    if (Config.VerifyAll) Console.WriteLine("Validating {0}", fileFinalPath);

                    var matchingChunks = new List<ChunkMatch>();

                    foreach (var chunk in file.Chunks)
                    {
                        var oldChunk =
                            oldManifestFile.Chunks.FirstOrDefault(c => c.ChunkID.SequenceEqual(chunk.ChunkID));
                        if (oldChunk != null)
                            matchingChunks.Add(new ChunkMatch(oldChunk, chunk));
                        else
                            neededChunks.Add(chunk);
                    }

                    var orderedChunks = matchingChunks.OrderBy(x => x.OldChunk.Offset);

                    var copyChunks = new List<ChunkMatch>();

                    using (var fsOld = File.Open(fileFinalPath, FileMode.Open))
                    {
                        foreach (var match in orderedChunks)
                        {
                            fsOld.Seek((long)match.OldChunk.Offset, SeekOrigin.Begin);

                            var adler = Util.AdlerHash(fsOld, (int)match.OldChunk.UncompressedLength);
                            if (!adler.SequenceEqual(BitConverter.GetBytes(match.OldChunk.Checksum)))
                                neededChunks.Add(match.NewChunk);
                            else
                                copyChunks.Add(match);
                        }
                    }

                    if (!hashMatches || neededChunks.Count > 0)
                    {
                        File.Move(fileFinalPath, fileStagingPath);

                        using (var fsOld = File.Open(fileStagingPath, FileMode.Open))
                        {
                            using var fs = File.Open(fileFinalPath, FileMode.Create);
                            try
                            {
                                fs.SetLength((long)file.TotalSize);
                            }
                            catch (IOException ex)
                            {
                                throw new ContentDownloaderException(string.Format(
                                    "Failed to resize file to expected size {0}: {1}", fileFinalPath, ex.Message));
                            }

                            foreach (var match in copyChunks)
                            {
                                fsOld.Seek((long)match.OldChunk.Offset, SeekOrigin.Begin);

                                var tmp = new byte[match.OldChunk.UncompressedLength];
                                fsOld.ReadExactly(tmp);

                                fs.Seek((long)match.NewChunk.Offset, SeekOrigin.Begin);
                                fs.Write(tmp, 0, tmp.Length);
                            }
                        }

                        File.Delete(fileStagingPath);
                    }
                }
            }
            else
            {
                // No old manifest or file not in old manifest. We must validate.

                using var fs = File.Open(fileFinalPath, FileMode.Open);
                if ((ulong)fi.Length != file.TotalSize)
                    try
                    {
                        fs.SetLength((long)file.TotalSize);
                    }
                    catch (IOException ex)
                    {
                        throw new ContentDownloaderException(string.Format("Failed to allocate file {0}: {1}",
                            fileFinalPath, ex.Message));
                    }

                Console.WriteLine("Validating {0}", fileFinalPath);
                neededChunks = Util.ValidateSteam3FileChecksums(fs, [.. file.Chunks.OrderBy(x => x.Offset)]);
            }

            if (neededChunks.Count == 0)
            {
                lock (depotDownloadCounter)
                {
                    depotDownloadCounter.SizeDownloaded += file.TotalSize;
                    Console.WriteLine("{0,6:#00.00}% {1}",
                        depotDownloadCounter.SizeDownloaded / (float)depotDownloadCounter.CompleteDownloadSize * 100.0f,
                        fileFinalPath);
                }

                lock (downloadCounter)
                {
                    downloadCounter.CompleteDownloadSize -= file.TotalSize;
                }

                return;
            }

            var sizeOnDisk = file.TotalSize - (ulong)neededChunks.Select(x => (long)x.UncompressedLength).Sum();
            lock (depotDownloadCounter)
            {
                depotDownloadCounter.SizeDownloaded += sizeOnDisk;
            }

            lock (downloadCounter)
            {
                downloadCounter.CompleteDownloadSize -= sizeOnDisk;
            }
        }

        var fileIsExecutable = file.Flags.HasFlag(EDepotFileFlag.Executable);
        if (fileIsExecutable && (!fileDidExist || oldManifestFile == null ||
                                 !oldManifestFile.Flags.HasFlag(EDepotFileFlag.Executable)))
            PlatformUtilities.SetExecutable(fileFinalPath, true);
        else if (!fileIsExecutable && oldManifestFile != null &&
                 oldManifestFile.Flags.HasFlag(EDepotFileFlag.Executable))
            PlatformUtilities.SetExecutable(fileFinalPath, false);

        var fileStreamData = new FileStreamData
        {
            FileStream = null,
            FileLock = new SemaphoreSlim(1),
            ChunksToDownload = neededChunks.Count
        };

        foreach (var chunk in neededChunks) networkChunkQueue.Enqueue((fileStreamData, file, chunk));
    }

    private static async Task DownloadSteam3AsyncDepotFileChunk(
        CancellationTokenSource cts,
        GlobalDownloadCounter downloadCounter,
        DepotFilesData depotFilesData,
        DepotManifest.FileData file,
        FileStreamData fileStreamData,
        DepotManifest.ChunkData chunk)
    {
        cts.Token.ThrowIfCancellationRequested();

        var depot = depotFilesData.DepotDownloadInfo;
        var depotDownloadCounter = depotFilesData.DepotCounter;

        var chunkId = Convert.ToHexString(chunk.ChunkID).ToLowerInvariant();

        var written = 0;
        var chunkBuffer = ArrayPool<byte>.Shared.Rent((int)chunk.UncompressedLength);

        try
        {
            do
            {
                cts.Token.ThrowIfCancellationRequested();

                Server connection = null;

                try
                {
                    connection = _cdnPool.GetConnection();

                    string cdnToken = null;
                    if (_steam3.CdnAuthTokens.TryGetValue((depot.DepotId, connection.Host),
                            out var authTokenCallbackPromise))
                    {
                        var result = await authTokenCallbackPromise.Task;
                        cdnToken = result.Token;
                    }

                    DebugLog.WriteLine("ContentDownloader", "Downloading chunk {0} from {1} with {2}", chunkId,
                        connection, _cdnPool.ProxyServer != null ? _cdnPool.ProxyServer : "no proxy");
                    written = await _cdnPool.CdnClient.DownloadDepotChunkAsync(
                        depot.DepotId,
                        chunk,
                        connection,
                        chunkBuffer,
                        depot.DepotKey,
                        _cdnPool.ProxyServer,
                        cdnToken).ConfigureAwait(false);

                    _cdnPool.ReturnConnection(connection);

                    break;
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Connection timeout downloading chunk {0}", chunkId);
                    _cdnPool.ReturnBrokenConnection(connection);
                }
                catch (SteamKitWebRequestException e)
                {
                    // If the CDN returned 403, attempt to get a cdn auth if we didn't yet,
                    // if auth task already exists, make sure it didn't complete yet, so that it gets awaited above
                    if (e.StatusCode == HttpStatusCode.Forbidden &&
                        (!_steam3.CdnAuthTokens.TryGetValue((depot.DepotId, connection.Host),
                            out var authTokenCallbackPromise) || !authTokenCallbackPromise.Task.IsCompleted))
                    {
                        await _steam3.RequestCdnAuthToken(depot.AppId, depot.DepotId, connection);

                        _cdnPool.ReturnConnection(connection);

                        continue;
                    }

                    _cdnPool.ReturnBrokenConnection(connection);

                    if (e.StatusCode == HttpStatusCode.Unauthorized || e.StatusCode == HttpStatusCode.Forbidden)
                    {
                        Console.WriteLine("Encountered {1} for chunk {0}. Aborting.", chunkId, (int)e.StatusCode);
                        break;
                    }

                    Console.WriteLine("Encountered error downloading chunk {0}: {1}", chunkId, e.StatusCode);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _cdnPool.ReturnBrokenConnection(connection);
                    Console.WriteLine("Encountered unexpected error downloading chunk {0}: {1}", chunkId, e.Message);
                }
            } while (written == 0);

            if (written == 0)
            {
                Console.WriteLine("Failed to find any server with chunk {0} for depot {1}. Aborting.", chunkId,
                    depot.DepotId);
                cts.Cancel();
            }

            // Throw the cancellation exception if requested so that this task is marked failed
            cts.Token.ThrowIfCancellationRequested();

            try
            {
                await fileStreamData.FileLock.WaitAsync().ConfigureAwait(false);

                if (fileStreamData.FileStream == null)
                {
                    var fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
                    fileStreamData.FileStream = File.Open(fileFinalPath, FileMode.Open);
                }

                fileStreamData.FileStream.Seek((long)chunk.Offset, SeekOrigin.Begin);
                await fileStreamData.FileStream.WriteAsync(chunkBuffer.AsMemory(0, written), cts.Token);
            }
            finally
            {
                fileStreamData.FileLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkBuffer);
        }

        var remainingChunks = Interlocked.Decrement(ref fileStreamData.ChunksToDownload);
        if (remainingChunks == 0)
        {
            fileStreamData.FileStream?.Dispose();
            fileStreamData.FileLock.Dispose();
        }

        ulong sizeDownloaded = 0;
        lock (depotDownloadCounter)
        {
            sizeDownloaded = depotDownloadCounter.SizeDownloaded + (ulong)written;
            depotDownloadCounter.SizeDownloaded = sizeDownloaded;
            depotDownloadCounter.DepotBytesCompressed += chunk.CompressedLength;
            depotDownloadCounter.DepotBytesUncompressed += chunk.UncompressedLength;
        }

        lock (downloadCounter)
        {
            downloadCounter.TotalBytesCompressed += chunk.CompressedLength;
            downloadCounter.TotalBytesUncompressed += chunk.UncompressedLength;

            Ansi.Progress(downloadCounter.TotalBytesUncompressed, downloadCounter.CompleteDownloadSize);
        }

        if (remainingChunks == 0)
        {
            var fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
            Console.WriteLine("{0,6:#00.00}% {1}",
                sizeDownloaded / (float)depotDownloadCounter.CompleteDownloadSize * 100.0f, fileFinalPath);
        }
    }

    private static void DumpManifestToTextFile(DepotDownloadInfo depot, DepotManifest manifest)
    {
        var txtManifest = Path.Combine(depot.InstallDir, $"manifest_{depot.DepotId}_{depot.ManifestId}.txt");
        using var sw = new StreamWriter(txtManifest);

        sw.WriteLine($"Content Manifest for Depot {depot.DepotId} ");
        sw.WriteLine();
        sw.WriteLine($"Manifest ID / date     : {depot.ManifestId} / {manifest.CreationTime} ");

        var uniqueChunks = new HashSet<byte[]>(new ChunkIdComparer());

        foreach (var file in manifest.Files)
        foreach (var chunk in file.Chunks)
            uniqueChunks.Add(chunk.ChunkID);

        sw.WriteLine($"Total number of files  : {manifest.Files.Count} ");
        sw.WriteLine($"Total number of chunks : {uniqueChunks.Count} ");
        sw.WriteLine($"Total bytes on disk    : {manifest.TotalUncompressedSize} ");
        sw.WriteLine($"Total bytes compressed : {manifest.TotalCompressedSize} ");
        sw.WriteLine();
        sw.WriteLine();
        sw.WriteLine("          Size Chunks File SHA                                 Flags Name");

        foreach (var file in manifest.Files)
        {
            var sha1Hash = Convert.ToHexString(file.FileHash).ToLower();
            sw.WriteLine(
                $"{file.TotalSize,14:d} {file.Chunks.Count,6:d} {sha1Hash} {(int)file.Flags,5:x} {file.FileName}");
        }
    }

    private sealed class DepotDownloadInfo(
        uint depotid,
        uint appId,
        ulong manifestId,
        string branch,
        string installDir,
        byte[] depotKey)
    {
        public uint DepotId { get; } = depotid;
        public uint AppId { get; } = appId;
        public ulong ManifestId { get; } = manifestId;
        public string Branch { get; } = branch;
        public string InstallDir { get; } = installDir;
        public byte[] DepotKey { get; } = depotKey;
    }

    private class ChunkMatch(DepotManifest.ChunkData oldChunk, DepotManifest.ChunkData newChunk)
    {
        public DepotManifest.ChunkData OldChunk { get; } = oldChunk;
        public DepotManifest.ChunkData NewChunk { get; } = newChunk;
    }

    private class DepotFilesData
    {
        public HashSet<string> AllFileNames;
        public DepotDownloadCounter DepotCounter;
        public DepotDownloadInfo DepotDownloadInfo;
        public List<DepotManifest.FileData> FilteredFiles;
        public DepotManifest Manifest;
        public DepotManifest PreviousManifest;
        public string StagingDir;
    }

    private class FileStreamData
    {
        public int ChunksToDownload;
        public SemaphoreSlim FileLock;
        public FileStream FileStream;
    }

    private class GlobalDownloadCounter
    {
        public ulong CompleteDownloadSize;
        public ulong TotalBytesCompressed;
        public ulong TotalBytesUncompressed;
    }

    private class DepotDownloadCounter
    {
        public ulong CompleteDownloadSize;
        public ulong DepotBytesCompressed;
        public ulong DepotBytesUncompressed;
        public ulong SizeDownloaded;
    }

    private class ChunkIdComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] x, byte[] y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            return x.SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            // ChunkID is SHA-1, so we can just use the first 4 bytes
            return BitConverter.ToInt32(obj, 0);
        }
    }
}
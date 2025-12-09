using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.CDN;

namespace DepotDownloader.Lib;

/// <summary>
///     Exception thrown when content download operations fail.
/// </summary>
public class ContentDownloaderException(string value) : Exception(value);

public static class ContentDownloader
{
    // Constants for validation sentinel values
    public const uint InvalidAppId = uint.MaxValue;
    public const ulong InvalidManifestId = ulong.MaxValue;
    public const string DefaultBranch = "public";

    // Directory structure constants
    private const string DefaultDownloadDir = "depots";
    private const string ConfigDir = ".DepotDownloader";
    private static readonly string StagingDirectoryName = Path.Combine(ConfigDir, "staging");

    // Configuration and session state
    public static readonly DownloadConfig Config = new();
    private static CdnClientPool _cdnPool;
    private static IUserInterface _userInterface;

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

    /// <summary>
    ///     Gets the current Steam3 session, or null if not initialized.
    /// </summary>
    internal static Steam3Session Steam3 { get; private set; }

    public static void Initialize(IUserInterface userInterface)
    {
        _userInterface = userInterface ?? throw new ArgumentNullException(nameof(userInterface));
    }

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
                Directory.CreateDirectory(Path.Combine(installDir, StagingDirectoryName));
            }
            else
            {
                Directory.CreateDirectory(Config.InstallDirectory);

                installDir = Config.InstallDirectory;

                Directory.CreateDirectory(Path.Combine(installDir, ConfigDir));
                Directory.CreateDirectory(Path.Combine(installDir, StagingDirectoryName));
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static bool InitializeSteam3(string username, string password)
    {
        string loginToken = null;

        if (username is not null && Config.RememberPassword)
            _ = AccountSettingsStore.Instance.LoginTokens.TryGetValue(username, out loginToken);

        Steam3 = new Steam3Session(
            new SteamUser.LogOnDetails
            {
                Username = username,
                Password = loginToken is null ? password : null,
                ShouldRememberPassword = Config.RememberPassword,
                AccessToken = loginToken,
                LoginID = Config.LoginId ?? 0x534B32
            },
            _userInterface
        );

        if (!Steam3.WaitForCredentials())
        {
            _userInterface?.WriteLine("Unable to get steam3 credentials.");
            return false;
        }

        Task.Run(Steam3.TickCallbacks);

        return true;
    }

    public static void ShutdownSteam3()
    {
        Steam3?.Disconnect();
        AppInfoService.ClearCache();
    }

    /// <summary>
    ///     Gets a download plan without actually downloading.
    /// </summary>
    public static async Task<DownloadPlan> GetDownloadPlanAsync(
        uint appId,
        List<(uint depotId, ulong manifestId)> depotManifestIds,
        string branch,
        string os,
        string arch,
        string language,
        bool lv)
    {
        if (Steam3 is null)
            throw new InvalidOperationException("Steam3 must be initialized before getting download plan.");

        await Steam3.RequestAppInfo(appId);

        var appInfo = AppInfoService.GetAppInfo(Steam3, appId);

        // Get depot list using the same logic as DownloadAppAsync
        var hasSpecificDepots = depotManifestIds.Count > 0;
        var depots = AppInfoService.GetAppSection(Steam3, appId, EAppInfoSection.Depots);

        if (!hasSpecificDepots && depots is not null)
            foreach (var depotSection in depots.Children)
            {
                if (depotSection.Children.Count == 0)
                    continue;

                if (!uint.TryParse(depotSection.Name, out var id))
                    continue;

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

                depotManifestIds.Add((id, InvalidManifestId));
            }

        var depotPlans = new List<DepotDownloadPlan>();
        ulong totalSize = 0;
        var totalFiles = 0;

        foreach (var (depotId, manifestId) in depotManifestIds)
        {
            var depotPlan = await GetDepotDownloadPlanAsync(depotId, appId, manifestId, branch);
            if (depotPlan is not null)
            {
                depotPlans.Add(depotPlan);
                totalSize += depotPlan.TotalSize;
                totalFiles += depotPlan.Files.Count;
            }
        }

        return new DownloadPlan(appId, appInfo.Name, depotPlans, totalSize, totalFiles);
    }

    private static async Task<DepotDownloadPlan> GetDepotDownloadPlanAsync(
        uint depotId, uint appId, ulong manifestId, string branch)
    {
        if (!await AppInfoService.AccountHasAccessAsync(Steam3, appId, depotId))
            return null;

        if (manifestId == InvalidManifestId)
        {
            manifestId = await AppInfoService.GetDepotManifestAsync(Steam3, depotId, appId, branch,
                Config.BetaPassword, _userInterface);

            if (manifestId == InvalidManifestId &&
                !string.Equals(branch, DefaultBranch, StringComparison.OrdinalIgnoreCase))
            {
                branch = DefaultBranch;
                manifestId = await AppInfoService.GetDepotManifestAsync(Steam3, depotId, appId, branch,
                    Config.BetaPassword, _userInterface);
            }

            if (manifestId == InvalidManifestId)
                return null;
        }

        // We need the depot key to download the manifest
        await Steam3.RequestDepotKey(depotId, appId);
        if (!Steam3.DepotKeys.TryGetValue(depotId, out var depotKey))
            return null;

        // Try to get manifest info
        var configDir = Config.InstallDirectory ?? DefaultDownloadDir;
        Directory.CreateDirectory(Path.Combine(configDir, ConfigDir));

        // Download manifest to get file list
        var cdnPool = new CdnClientPool(Steam3, appId);
        await cdnPool.UpdateServerList();

        DepotManifest manifest;
        var connection = cdnPool.GetConnection();

        try
        {
            var manifestRequestCode = await Steam3.GetDepotManifestRequestCodeAsync(
                depotId, appId, manifestId, branch);

            if (manifestRequestCode == 0)
                return null;

            string cdnToken = null;
            if (Steam3.CdnAuthTokens.TryGetValue((depotId, connection.Host), out var authTokenPromise))
            {
                var result = await authTokenPromise.Task;
                cdnToken = result.Token;
            }

            manifest = await cdnPool.CdnClient.DownloadManifestAsync(
                depotId, manifestId, manifestRequestCode,
                connection, depotKey, cdnPool.ProxyServer, cdnToken);

            cdnPool.ReturnConnection(connection);
        }
        catch
        {
            cdnPool.ReturnBrokenConnection(connection);
            return null;
        }

        if (manifest.Files is null)
            return null;

        var files = new List<FileDownloadInfo>();
        ulong totalSize = 0;

        foreach (var file in manifest.Files)
        {
            if (file.Flags.HasFlag(EDepotFileFlag.Directory))
                continue;

            if (!FileFilter.TestIsFileIncluded(file.FileName, Config))
                continue;

            var hash = Convert.ToHexString(file.FileHash).ToLowerInvariant();
            files.Add(new FileDownloadInfo(file.FileName, file.TotalSize, hash));
            totalSize += file.TotalSize;
        }

        return new DepotDownloadPlan(depotId, manifestId, files, totalSize);
    }

    private static async Task ProcessPublishedFileAsync(uint appId, ulong publishedFileId,
        List<ValueTuple<string, string>> fileUrls, List<ulong> contentFileIds)
    {
        var details = await Steam3.GetPublishedFileDetails(appId, publishedFileId);
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
                _userInterface?.WriteLine("Unable to locate manifest ID for published file {0}", publishedFileId);
        }
        else
        {
            _userInterface?.WriteLine("Published file {0} has unsupported file type {1}. Skipping file",
                publishedFileId, fileType);
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

        if (Steam3?.SteamUser?.SteamID?.AccountType != EAccountType.AnonUser)
        {
            if (Steam3 is not null) details = await Steam3.GetUgcDetails(ugcId);
        }
        else
        {
            _userInterface?.WriteLine($"Unable to query UGC details for {ugcId} from an anonymous account");
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
            _userInterface?.WriteLine("Error: Unable to create install directories!");
            return;
        }

        var stagingDir = Path.Combine(installDir, StagingDirectoryName);
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
            _userInterface?.WriteLine("Downloading {0}", fileName);
            var responseStream = await client.GetStreamAsync(url);
            await responseStream.CopyToAsync(file);
        }

        if (File.Exists(fileFinalPath)) File.Delete(fileFinalPath);

        File.Move(fileStagingPath, fileFinalPath);
    }

    public static Task DownloadAppAsync(uint appId, List<(uint depotId, ulong manifestId)> depotManifestIds,
        string branch, string os, string arch, string language, bool lv, bool isUgc)
    {
        return DownloadAppAsync(appId, depotManifestIds, branch, os, arch, language, lv, isUgc, null,
            CancellationToken.None);
    }

    internal static async Task DownloadAppAsync(uint appId, List<(uint depotId, ulong manifestId)> depotManifestIds,
        string branch, string os, string arch, string language, bool lv, bool isUgc,
        DownloadProgressContext progressContext, CancellationToken cancellationToken)
    {
        if (Steam3 is null) throw new InvalidOperationException("Steam3 must be initialized before downloading.");

        _cdnPool = new CdnClientPool(Steam3, appId);

        var configPath = Config.InstallDirectory;
        if (string.IsNullOrWhiteSpace(configPath)) configPath = DefaultDownloadDir;

        Directory.CreateDirectory(Path.Combine(configPath, ConfigDir));
        DepotConfigStore.LoadFromFile(Path.Combine(configPath, ConfigDir, "depot.config"));

        await Steam3.RequestAppInfo(appId);

        if (!await AppInfoService.AccountHasAccessAsync(Steam3, appId, appId))
        {
            if (Steam3.SteamUser?.SteamID?.AccountType != EAccountType.AnonUser &&
                await Steam3.RequestFreeAppLicense(appId))
            {
                _userInterface?.WriteLine("Obtained FreeOnDemand license for app {0}", appId);
                await Steam3.RequestAppInfo(appId, true);
            }
            else
            {
                var contentName = AppInfoService.GetAppName(Steam3, appId);
                throw new ContentDownloaderException(
                    $"App {appId} ({contentName}) is not available from this account.");
            }
        }

        var hasSpecificDepots = depotManifestIds.Count > 0;
        var depotIdsFound = new List<uint>();
        var depotIdsExpected = depotManifestIds.Select(x => x.depotId).ToList();
        var depots = AppInfoService.GetAppSection(Steam3, appId, EAppInfoSection.Depots);

        if (isUgc)
        {
            if (depots is not null)
            {
                var workshopDepot = depots["workshopdepot"].AsUnsignedInteger();
                if (workshopDepot != 0 && !depotIdsExpected.Contains(workshopDepot))
                {
                    depotIdsExpected.Add(workshopDepot);
                    depotManifestIds = [.. depotManifestIds.Select(pair => (workshopDepot, pair.manifestId))];
                }
            }

            depotIdsFound.AddRange(depotIdsExpected);
        }
        else
        {
            _userInterface?.WriteLine("Using app branch: '{0}'.", branch);

            if (depots is not null)
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
                throw new ContentDownloaderException($"Couldn't find any depots to download for app {appId}");

            if (depotIdsFound.Count < depotIdsExpected.Count)
            {
                var remainingDepotIds = depotIdsExpected.Except(depotIdsFound);
                throw new ContentDownloaderException(
                    $"Depot {string.Join(", ", remainingDepotIds)} not listed for app {appId}");
            }
        }

        var infos = new List<DepotDownloadInfo>();

        foreach (var (depotId, manifestId) in depotManifestIds)
        {
            var info = await GetDepotInfo(depotId, appId, manifestId, branch);
            if (info is not null) infos.Add(info);
        }

        _userInterface?.WriteLine();

        try
        {
            await DownloadSteam3Async(infos, progressContext, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _userInterface?.WriteLine("App {0} was not completely downloaded.", appId);
            throw;
        }
    }

    private static async Task DownloadSteam3Async(
        List<DepotDownloadInfo> depots,
        DownloadProgressContext progressContext, CancellationToken cancellationToken)
    {
        _userInterface?.UpdateProgress(0, 1);

        await _cdnPool.UpdateServerList();

        // Create a linked token source if external token is provided
        using var linkedCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();

        var downloadCounter = new GlobalDownloadCounter();
        var depotsToDownload = new List<DepotFilesData>(depots.Count);
        var allFileNamesAllDepots = new HashSet<string>();

        foreach (var depot in depots)
        {
            var depotFileData = await ProcessDepotManifestAndFiles(linkedCts, depot, downloadCounter);

            if (depotFileData is not null)
            {
                depotsToDownload.Add(depotFileData);
                allFileNamesAllDepots.UnionWith(depotFileData.AllFileNames);
            }

            linkedCts.Token.ThrowIfCancellationRequested();
        }

        // Store the total size before it gets decremented during file processing
        downloadCounter.TotalDownloadSize = downloadCounter.CompleteDownloadSize;

        if (!string.IsNullOrWhiteSpace(Config.InstallDirectory) && depotsToDownload.Count > 0)
        {
            var claimedFileNames = new HashSet<string>();

            for (var i = depotsToDownload.Count - 1; i >= 0; i--)
            {
                depotsToDownload[i].FilteredFiles.RemoveAll(file => claimedFileNames.Contains(file.FileName));
                claimedFileNames.UnionWith(depotsToDownload[i].AllFileNames);
            }
        }

        foreach (var depotFileData in depotsToDownload)
            await DepotFileDownloader.DownloadDepotFilesAsync(linkedCts, downloadCounter, depotFileData,
                allFileNamesAllDepots, _cdnPool, Steam3, Config, _userInterface, progressContext);

        _userInterface?.UpdateProgress(downloadCounter.TotalDownloadSize, downloadCounter.TotalDownloadSize);

        _userInterface?.WriteLine("Total downloaded: {0} bytes ({1} bytes uncompressed) from {2} depots",
            downloadCounter.TotalBytesCompressed, downloadCounter.TotalBytesUncompressed, depots.Count);
    }

    private static async Task<DepotDownloadInfo> GetDepotInfo(uint depotId, uint appId, ulong manifestId, string branch)
    {
        if (Steam3 is null)
            throw new InvalidOperationException("Steam3 must be initialized before getting depot info.");

        if (appId != InvalidAppId) await Steam3.RequestAppInfo(appId);

        if (!await AppInfoService.AccountHasAccessAsync(Steam3, appId, depotId))
        {
            _userInterface?.WriteLine("Depot {0} is not available from this account.", depotId);
            return null;
        }

        if (manifestId == InvalidManifestId)
        {
            manifestId = await AppInfoService.GetDepotManifestAsync(Steam3, depotId, appId, branch,
                Config.BetaPassword, _userInterface);
            if (manifestId == InvalidManifestId &&
                !string.Equals(branch, DefaultBranch, StringComparison.OrdinalIgnoreCase))
            {
                _userInterface?.WriteLine("Warning: Depot {0} does not have branch named \"{1}\". Trying {2} branch.",
                    depotId, branch, DefaultBranch);
                branch = DefaultBranch;
                manifestId = await AppInfoService.GetDepotManifestAsync(Steam3, depotId, appId, branch,
                    Config.BetaPassword, _userInterface);
            }

            if (manifestId == InvalidManifestId)
            {
                _userInterface?.WriteLine("Depot {0} missing public subsection or manifest section.", depotId);
                return null;
            }
        }

        await Steam3.RequestDepotKey(depotId, appId);
        if (!Steam3.DepotKeys.TryGetValue(depotId, out var depotKey))
        {
            _userInterface?.WriteLine("No valid depot key for {0}, unable to download.", depotId);
            return null;
        }

        var uVersion = AppInfoService.GetAppBuildNumber(Steam3, appId, branch);

        if (!CreateDirectories(depotId, uVersion, out var installDir))
        {
            _userInterface?.WriteLine("Error: Unable to create install directories!");
            return null;
        }

        var containingAppId = appId;
        var proxyAppId = AppInfoService.GetDepotProxyAppId(Steam3, depotId, appId);
        if (proxyAppId != InvalidAppId)
        {
            var common = AppInfoService.GetAppSection(Steam3, appId, EAppInfoSection.Common);
            if (common is null || !common["FreeToDownload"].AsBoolean()) containingAppId = proxyAppId;
        }

        return new DepotDownloadInfo(depotId, containingAppId, manifestId, branch, installDir, depotKey);
    }

    private static async Task<DepotFilesData> ProcessDepotManifestAndFiles(CancellationTokenSource cts,
        DepotDownloadInfo depot, GlobalDownloadCounter downloadCounter)
    {
        var depotCounter = new DepotDownloadCounter();

        _userInterface?.WriteLine("Processing depot {0}", depot.DepotId);

        DepotManifest oldManifest = null;
        DepotManifest newManifest;
        var configDir = Path.Combine(depot.InstallDir, ConfigDir);

        DepotConfigStore.Instance.InstalledManifestIDs.TryGetValue(depot.DepotId, out var lastManifestId);

        DepotConfigStore.Instance.InstalledManifestIDs[depot.DepotId] = InvalidManifestId;
        DepotConfigStore.Save();

        if (lastManifestId != InvalidManifestId)
        {
            var badHashWarning = lastManifestId != depot.ManifestId;
            oldManifest = Util.LoadManifestFromFile(configDir, depot.DepotId, lastManifestId, badHashWarning);
        }

        if (lastManifestId == depot.ManifestId && oldManifest is not null)
        {
            newManifest = oldManifest;
            _userInterface?.WriteLine("Already have manifest {0} for depot {1}.", depot.ManifestId, depot.DepotId);
        }
        else
        {
            newManifest = Util.LoadManifestFromFile(configDir, depot.DepotId, depot.ManifestId, true);

            if (newManifest is not null)
            {
                _userInterface?.WriteLine("Already have manifest {0} for depot {1}.", depot.ManifestId, depot.DepotId);
            }
            else
            {
                _userInterface?.WriteLine($"Downloading depot {depot.DepotId} manifest");

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
                        if (Steam3.CdnAuthTokens.TryGetValue((depot.DepotId, connection.Host),
                                out var authTokenCallbackPromise))
                        {
                            var result = await authTokenCallbackPromise.Task;
                            cdnToken = result.Token;
                        }

                        var now = DateTime.Now;

                        if (manifestRequestCode == 0 || now >= manifestRequestCodeExpiration)
                        {
                            manifestRequestCode = await Steam3.GetDepotManifestRequestCodeAsync(
                                depot.DepotId, depot.AppId, depot.ManifestId, depot.Branch);
                            manifestRequestCodeExpiration = now.Add(TimeSpan.FromMinutes(5));

                            // ReSharper disable once MethodHasAsyncOverload
                            if (manifestRequestCode == 0) cts.Cancel();
                        }

                        DebugLog.WriteLine("ContentDownloader",
                            "Downloading manifest {0} from {1} with {2}",
                            depot.ManifestId, connection,
                            _cdnPool.ProxyServer is not null ? _cdnPool.ProxyServer : "no proxy");

                        newManifest = await _cdnPool.CdnClient.DownloadManifestAsync(
                            depot.DepotId, depot.ManifestId, manifestRequestCode,
                            connection, depot.DepotKey, _cdnPool.ProxyServer, cdnToken).ConfigureAwait(false);

                        _cdnPool.ReturnConnection(connection);
                    }
                    catch (TaskCanceledException)
                    {
                        _userInterface?.WriteLine("Connection timeout downloading depot manifest {0} {1}. Retrying.",
                            depot.DepotId, depot.ManifestId);
                    }
                    catch (SteamKitWebRequestException e)
                    {
                        if (e.StatusCode == HttpStatusCode.Forbidden &&
                            connection is not null &&
                            !Steam3.CdnAuthTokens.ContainsKey((depot.DepotId, connection.Host)))
                        {
                            await Steam3.RequestCdnAuthToken(depot.AppId, depot.DepotId, connection);
                            _cdnPool.ReturnConnection(connection);
                            continue;
                        }

                        _cdnPool.ReturnBrokenConnection(connection);

                        if (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        {
                            _userInterface?.WriteLine("Encountered {2} for depot manifest {0} {1}. Aborting.",
                                depot.DepotId, depot.ManifestId, (int)e.StatusCode);
                            break;
                        }

                        if (e.StatusCode == HttpStatusCode.NotFound)
                        {
                            _userInterface?.WriteLine("Encountered 404 for depot manifest {0} {1}. Aborting.",
                                depot.DepotId, depot.ManifestId);
                            break;
                        }

                        _userInterface?.WriteLine("Encountered error downloading depot manifest {0} {1}: {2}",
                            depot.DepotId, depot.ManifestId, e.StatusCode);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        _cdnPool.ReturnBrokenConnection(connection);
                        _userInterface?.WriteLine("Encountered error downloading manifest for depot {0} {1}: {2}",
                            depot.DepotId, depot.ManifestId, e.Message);
                    }
                } while (newManifest is null);

                if (newManifest is null)
                {
                    _userInterface?.WriteLine("\nUnable to download manifest {0} for depot {1}", depot.ManifestId,
                        depot.DepotId);
                    // ReSharper disable once MethodHasAsyncOverload
                    cts.Cancel();
                }

                cts.Token.ThrowIfCancellationRequested();

                if (!Util.SaveManifestToFile(configDir, newManifest))
                    _userInterface?.WriteLine("Warning: Failed to save manifest {0} for depot {1} to disk.",
                        depot.ManifestId, depot.DepotId);
            }
        }

        _userInterface?.WriteLine("Manifest {0} ({1})", depot.ManifestId, newManifest.CreationTime);

        if (Config.DownloadManifestOnly)
        {
            DumpManifestToTextFile(depot, newManifest);
            return null;
        }

        var stagingDirPath = Path.Combine(depot.InstallDir, StagingDirectoryName);

        var filesAfterExclusions = newManifest.Files.AsParallel()
            .Where(f => FileFilter.TestIsFileIncluded(f.FileName, Config)).ToList();
        var allFileNames = new HashSet<string>(filesAfterExclusions.Count);

        filesAfterExclusions.ForEach(file =>
        {
            allFileNames.Add(file.FileName);

            var fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
            var fileStagingPath = Path.Combine(stagingDirPath, file.FileName);

            if (file.Flags.HasFlag(EDepotFileFlag.Directory))
            {
                Directory.CreateDirectory(fileFinalPath);
                Directory.CreateDirectory(fileStagingPath);
            }
            else
            {
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
            StagingDirectoryPath = stagingDirPath,
            PreviousManifest = oldManifest,
            FilteredFiles = filesAfterExclusions,
            AllFileNames = allFileNames
        };
    }

    private static void DumpManifestToTextFile(DepotDownloadInfo depot, DepotManifest manifest)
    {
        var txtManifest = Path.Combine(depot.InstallDir, $"manifest_{depot.DepotId}_{depot.ManifestId}.txt");
        using var sw = new StreamWriter(txtManifest);

        sw.WriteLine($"Content Manifest for Depot {depot.DepotId} ");
        sw.WriteLine();
        sw.WriteLine($"Manifest ID / date     : {depot.ManifestId} / {manifest.CreationTime} ");

        var uniqueChunks = new HashSet<byte[]>(new ChunkIdComparer());

        if (manifest.Files is null) return;
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
}
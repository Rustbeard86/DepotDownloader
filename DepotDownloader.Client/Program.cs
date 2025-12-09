using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DepotDownloader.Lib;

namespace DepotDownloader.Client;

internal class Program
{
    private static bool[] _consumedArgs;
    private static ConfigFile _config;
    private static ConsoleUserInterface _userInterface;

    private static async Task<int> Main(string[] args)
    {
        // Initialize the user interface first
        _userInterface = new ConsoleUserInterface();

        switch (args.Length)
        {
            case 0:
                PrintVersion();
                PrintUsage();
                return 0;
            // Not using HasParameter because it is case-insensitive
            case 1 when args[0] == "-V" || args[0] == "--version":
                PrintVersion(true);
                return 0;
        }

        _consumedArgs = new bool[args.Length];

        // Load config file if specified (do this early so CLI args can override)
        var configPath = GetParameter<string>(args, "-config") ?? GetParameter<string>(args, "--config");
        if (configPath is not null)
            try
            {
                var configJson = await File.ReadAllTextAsync(configPath);
                _config = JsonSerializer.Deserialize(configJson, ConfigFileContext.Default.ConfigFile);
                _userInterface.WriteLine("Loaded config from: {0}", configPath);
            }
            catch (Exception ex)
            {
                _userInterface.WriteError("Error loading config file: {0}", ex.Message);
                return 1;
            }

        // Create the library client - this handles all initialization
        using var client = new DepotDownloaderClient(_userInterface);

        // Enable debug logging if requested (CLI takes precedence over config)
        if (HasParameter(args, "-debug") || (_config?.Debug ?? false))
        {
            PrintVersion(true);
            client.EnableDebugLogging();
        }

        // Parse authentication parameters (CLI takes precedence over config)
        var username = GetParameter<string>(args, "-username") ??
                       GetParameter<string>(args, "-user") ?? _config?.Username;
        var password = GetParameter<string>(args, "-password") ?? GetParameter<string>(args, "-pass");
        var rememberPassword = HasParameter(args, "-remember-password") || (_config?.RememberPassword ?? false);
        var useQrCode = HasParameter(args, "-qr") || (_config?.UseQrCode ?? false);
        var skipAppConfirmation = HasParameter(args, "-no-mobile") || (_config?.NoMobile ?? false);

        // Validate authentication parameter combinations
        if (username is null)
        {
            if (rememberPassword && !useQrCode)
            {
                _userInterface.WriteLine("Error: -remember-password can not be used without -username or -qr.");
                return 1;
            }
        }
        else if (useQrCode)
        {
            _userInterface.WriteLine("Error: -qr can not be used with -username.");
            return 1;
        }

        // Parse download options (CLI takes precedence over config)
        var appId = GetParameter(args, "-app", _config?.AppId ?? SteamConstants.InvalidAppId);
        if (appId == SteamConstants.InvalidAppId)
        {
            _userInterface.WriteLine("Error: -app not specified!");
            return 1;
        }

        // Check for query-only commands
        var listDepots = HasParameter(args, "-list-depots") || HasParameter(args, "--list-depots");
        var listBranches = HasParameter(args, "-list-branches") || HasParameter(args, "--list-branches");
        var dryRun = HasParameter(args, "-dry-run") || HasParameter(args, "--dry-run");
        var verbose = HasParameter(args, "-verbose") || HasParameter(args, "-v");
        var jsonOutput = HasParameter(args, "-json") || HasParameter(args, "--json");
        var noProgress = HasParameter(args, "-no-progress") || HasParameter(args, "--no-progress");
        var resume = HasParameter(args, "-resume") || HasParameter(args, "--resume");

        var pubFile = GetParameter(args, "-pubfile", SteamConstants.InvalidManifestId);
        var ugcId = GetParameter(args, "-ugc", SteamConstants.InvalidManifestId);

        // Build download options (unless this is a query-only command)
        DepotDownloadOptions options = null;
        if (!listDepots && !listBranches)
        {
            options = await BuildDownloadOptionsAsync(args, appId);
            options.Resume = resume;
        }

        PrintUnconsumedArgs(args);

        // Authenticate
        bool loginSuccess;
        if (useQrCode)
            loginSuccess = client.LoginWithQrCode(rememberPassword, skipAppConfirmation);
        else if (username is not null)
            loginSuccess = client.Login(username, password, rememberPassword, skipAppConfirmation);
        else
            loginSuccess = client.LoginAnonymous(skipAppConfirmation);

        if (!loginSuccess)
        {
            if (jsonOutput)
                WriteJsonError("Authentication failed");
            else
                _userInterface.WriteLine("Error: Authentication failed");
            return 1;
        }

        // Handle query commands
        if (listDepots)
            return await ListDepotsAsync(client, appId, jsonOutput);

        if (listBranches)
            return await ListBranchesAsync(client, appId, jsonOutput);

        if (dryRun)
            return await DryRunAsync(client, options, verbose, jsonOutput);

        // Perform download
        try
        {
            // Setup progress bar if not disabled and not in JSON mode
            ProgressBar progressBar = null;
            var downloadStartTime = DateTime.UtcNow;

            if (!jsonOutput && !noProgress && !Console.IsOutputRedirected)
            {
                progressBar = new ProgressBar();
                client.DownloadProgress += (_, e) =>
                {
                    progressBar.Update(
                        e.BytesDownloaded,
                        e.TotalBytes,
                        e.SpeedBytesPerSecond,
                        e.EstimatedTimeRemaining,
                        e.FilesCompleted,
                        e.TotalFiles);
                };
            }

            if (pubFile != SteamConstants.InvalidManifestId)
                await client.DownloadPublishedFileAsync(appId, pubFile);
            else if (ugcId != SteamConstants.InvalidManifestId)
                await client.DownloadUgcAsync(appId, ugcId);
            else
                await client.DownloadAppAsync(options);

            progressBar?.Complete();

            if (jsonOutput)
            {
                var duration = DateTime.UtcNow - downloadStartTime;
                WriteJsonSuccess(appId, duration);
            }

            return 0;
        }
        catch (InsufficientDiskSpaceException ex)
        {
            if (jsonOutput)
            {
                WriteJsonError(
                    $"Insufficient disk space on {ex.TargetDrive}. Required: {ex.RequiredBytes}, Available: {ex.AvailableBytes}");
            }
            else
            {
                _userInterface.WriteLine();
                _userInterface.WriteError("Error: Insufficient disk space!");
                _userInterface.WriteError("  Drive:      {0}", ex.TargetDrive);
                _userInterface.WriteError("  Required:   {0}", FormatSize(ex.RequiredBytes));
                _userInterface.WriteError("  Available:  {0}", FormatSize(ex.AvailableBytes));
                _userInterface.WriteError("  Shortfall:  {0}", FormatSize(ex.ShortfallBytes));
                _userInterface.WriteLine();
                _userInterface.WriteLine("Free up disk space or use -skip-disk-check to bypass this check.");
            }

            return 1;
        }
        catch (ContentDownloaderException ex)
        {
            if (jsonOutput)
                WriteJsonError(ex.Message);
            else
                _userInterface.WriteLine(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            if (jsonOutput)
                WriteJsonError("Download was cancelled");
            else
                _userInterface.WriteLine("Download was cancelled.");
            return 1;
        }
        catch (Exception e)
        {
            if (jsonOutput)
                WriteJsonError($"Download failed: {e.Message}");
            else
                _userInterface.WriteLine("Download failed due to an unhandled exception: {0}", e.Message);
            throw;
        }
    }

    private static async Task<int> ListDepotsAsync(DepotDownloaderClient client, uint appId, bool jsonOutput)
    {
        try
        {
            var appInfo = await client.GetAppInfoAsync(appId);
            var depots = await client.GetDepotsAsync(appId);

            if (jsonOutput)
            {
                JsonOutput.WriteDepots(new DepotsResultJson
                {
                    AppId = appId,
                    AppName = appInfo.Name,
                    AppType = appInfo.Type,
                    Depots =
                    [
                        .. depots.Select(d => new DepotJson
                        {
                            DepotId = d.DepotId,
                            Name = d.Name,
                            Os = d.Os,
                            Architecture = d.Architecture,
                            Language = d.Language,
                            MaxSize = d.MaxSize,
                            IsSharedInstall = d.IsSharedInstall
                        })
                    ]
                });
                return 0;
            }

            _userInterface.WriteLine();
            _userInterface.WriteLine("Depots for {0} ({1}) [Type: {2}]:", appInfo.Name, appInfo.AppId, appInfo.Type);
            _userInterface.WriteLine();

            if (depots.Count == 0)
            {
                _userInterface.WriteLine("  No depots found.");
                return 0;
            }

            // Column headers
            _userInterface.WriteLine("  {0,-10} {1,-40} {2,-15} {3,-6} {4,-10} {5}",
                "DepotID", "Name", "OS", "Arch", "Language", "Size");
            _userInterface.WriteLine("  {0}", new string('-', 100));

            foreach (var depot in depots.OrderBy(d => d.DepotId))
            {
                var os = depot.Os ?? "all";
                var arch = depot.Architecture ?? "-";
                var lang = depot.Language ?? "-";
                var size = depot.MaxSize.HasValue ? FormatSize(depot.MaxSize.Value) : "-";
                var name = depot.Name ?? "(unnamed)";
                var shared = depot.IsSharedInstall ? " [shared]" : "";

                if (name.Length > 38)
                    name = name[..35] + "...";

                _userInterface.WriteLine("  {0,-10} {1,-40} {2,-15} {3,-6} {4,-10} {5}{6}",
                    depot.DepotId, name, os, arch, lang, size, shared);
            }

            _userInterface.WriteLine();
            _userInterface.WriteLine("Total: {0} depot(s)", depots.Count);

            return 0;
        }
        catch (Exception ex)
        {
            if (jsonOutput)
                JsonOutput.WriteError($"Error listing depots: {ex.Message}");
            else
                _userInterface.WriteLine("Error listing depots: {0}", ex.Message);
            return 1;
        }
    }

    private static async Task<int> ListBranchesAsync(DepotDownloaderClient client, uint appId, bool jsonOutput)
    {
        try
        {
            var appInfo = await client.GetAppInfoAsync(appId);
            var branches = await client.GetBranchesAsync(appId);

            if (jsonOutput)
            {
                JsonOutput.WriteBranches(new BranchesResultJson
                {
                    AppId = appId,
                    AppName = appInfo.Name,
                    Branches =
                    [
                        .. branches.Select(b => new BranchJson
                        {
                            Name = b.Name,
                            BuildId = b.BuildId,
                            TimeUpdated = b.TimeUpdated,
                            IsPasswordProtected = b.IsPasswordProtected,
                            Description = b.Description
                        })
                    ]
                });
                return 0;
            }

            _userInterface.WriteLine();
            _userInterface.WriteLine("Branches for {0} ({1}):", appInfo.Name, appId);
            _userInterface.WriteLine();

            if (branches.Count == 0)
            {
                _userInterface.WriteLine("  No branches found.");
                return 0;
            }

            // Column headers
            _userInterface.WriteLine("  {0,-20} {1,-12} {2,-22} {3,-12} {4}",
                "Branch", "BuildID", "Updated", "Protected", "Description");
            _userInterface.WriteLine("  {0}", new string('-', 90));

            foreach (var branch in branches.OrderBy(b => b.Name == "public" ? 0 : 1).ThenBy(b => b.Name))
            {
                var updated = branch.TimeUpdated?.ToString("yyyy-MM-dd HH:mm") ?? "-";
                var protection = branch.IsPasswordProtected ? "Yes" : "No";
                var description = branch.Description ?? "";

                if (description.Length > 30)
                    description = description[..27] + "...";

                _userInterface.WriteLine("  {0,-20} {1,-12} {2,-22} {3,-12} {4}",
                    branch.Name, branch.BuildId, updated, protection, description);
            }

            _userInterface.WriteLine();
            _userInterface.WriteLine("Total: {0} branch(es)", branches.Count);

            return 0;
        }
        catch (Exception ex)
        {
            if (jsonOutput)
                JsonOutput.WriteError($"Error listing branches: {ex.Message}");
            else
                _userInterface.WriteLine("Error listing branches: {0}", ex.Message);
            return 1;
        }
    }

    private static async Task<int> DryRunAsync(DepotDownloaderClient client, DepotDownloadOptions options, bool verbose,
        bool jsonOutput)
    {
        try
        {
            if (!jsonOutput)
            {
                _userInterface.WriteLine();
                _userInterface.WriteLine("Analyzing download plan (dry-run mode)...");
                _userInterface.WriteLine();
            }

            var plan = await client.GetDownloadPlanAsync(options);

            if (jsonOutput)
            {
                JsonOutput.WriteDryRun(new DryRunResultJson
                {
                    AppId = plan.AppId,
                    AppName = plan.AppName,
                    TotalDepots = plan.Depots.Count,
                    TotalFiles = plan.TotalFileCount,
                    TotalBytes = plan.TotalDownloadSize,
                    TotalSize = FormatSize(plan.TotalDownloadSize),
                    Depots =
                    [
                        .. plan.Depots.Select(d => new DepotPlanJson
                        {
                            DepotId = d.DepotId,
                            ManifestId = d.ManifestId,
                            FileCount = d.Files.Count,
                            TotalBytes = d.TotalSize,
                            TotalSize = FormatSize(d.TotalSize),
                            Files = verbose
                                ?
                                [
                                    .. d.Files.Select(f => new FilePlanJson
                                    {
                                        FileName = f.FileName,
                                        Size = f.Size,
                                        Hash = f.Hash
                                    })
                                ]
                                : null
                        })
                    ]
                });
                return 0;
            }

            _userInterface.WriteLine("Download Plan for {0} ({1}):", plan.AppName, plan.AppId);
            _userInterface.WriteLine();

            if (plan.Depots.Count == 0)
            {
                _userInterface.WriteLine("  No depots would be downloaded.");
                return 0;
            }

            foreach (var depot in plan.Depots)
            {
                _userInterface.WriteLine("  Depot {0} (Manifest {1})", depot.DepotId, depot.ManifestId);
                _userInterface.WriteLine("    Files: {0}", depot.Files.Count);
                _userInterface.WriteLine("    Size:  {0}", FormatSize(depot.TotalSize));

                // Show file details in verbose mode
                if (verbose && depot.Files.Count > 0)
                {
                    _userInterface.WriteLine();
                    _userInterface.WriteLine("    Files:");
                    foreach (var file in depot.Files.OrderBy(f => f.FileName).Take(50))
                        _userInterface.WriteLine("      {0,-60} {1,12} {2}",
                            file.FileName.Length > 58 ? "..." + file.FileName[^55..] : file.FileName,
                            FormatSize(file.Size),
                            file.Hash[..8]);
                    if (depot.Files.Count > 50)
                        _userInterface.WriteLine("      ... and {0} more files", depot.Files.Count - 50);
                }

                _userInterface.WriteLine();
            }

            _userInterface.WriteLine(new string('-', 50));
            _userInterface.WriteLine();
            _userInterface.WriteLine("Summary:");
            _userInterface.WriteLine("  Total depots:     {0}", plan.Depots.Count);
            _userInterface.WriteLine("  Total files:      {0:N0}", plan.TotalFileCount);
            _userInterface.WriteLine("  Total size:       {0}", FormatSize(plan.TotalDownloadSize));

            // Estimate download time at various speeds
            if (plan.TotalDownloadSize > 0)
            {
                _userInterface.WriteLine();
                _userInterface.WriteLine("Estimated download time:");
                _userInterface.WriteLine("  At 10 MB/s:  {0}",
                    FormatDuration(plan.TotalDownloadSize / (10 * 1024 * 1024)));
                _userInterface.WriteLine("  At 50 MB/s:  {0}",
                    FormatDuration(plan.TotalDownloadSize / (50 * 1024 * 1024)));
                _userInterface.WriteLine("  At 100 MB/s: {0}",
                    FormatDuration(plan.TotalDownloadSize / (100 * 1024 * 1024)));
            }

            _userInterface.WriteLine();
            _userInterface.WriteLine("To download, run the same command without --dry-run");

            return 0;
        }
        catch (ContentDownloaderException ex)
        {
            if (jsonOutput)
                WriteJsonError(ex.Message);
            else
                _userInterface.WriteLine("Error: {0}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            if (jsonOutput)
                WriteJsonError($"Error getting download plan: {ex.Message}");
            else
                _userInterface.WriteLine("Error getting download plan: {0}", ex.Message);
            return 1;
        }
    }

    private static string FormatDuration(ulong seconds)
    {
        if (seconds < 60)
            return $"{seconds} seconds";
        if (seconds < 3600)
            return $"{seconds / 60} min {seconds % 60} sec";
        return $"{seconds / 3600}h {seconds % 3600 / 60}m";
    }

    private static string FormatSize(ulong bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    private static async Task<DepotDownloadOptions> BuildDownloadOptionsAsync(string[] args, uint appId)
    {
        // Helper to get CLI param or fall back to config value
        var useLancache = HasParameter(args, "-use-lancache") || (_config?.UseLancache ?? false);

        var options = new DepotDownloadOptions
        {
            AppId = appId,
            DownloadManifestOnly = HasParameter(args, "-manifest-only") || (_config?.ManifestOnly ?? false),
            InstallDirectory = GetParameter<string>(args, "-dir") ?? _config?.InstallDirectory,
            VerifyAll = HasParameter(args, "-verify-all") ||
                        HasParameter(args, "-verify_all") ||
                        HasParameter(args, "-validate") ||
                        (_config?.Validate ?? false),
            MaxDownloads = GetParameter(args, "-max-downloads", _config?.MaxDownloads ?? 8),
            LoginId = HasParameter(args, "-loginid")
                ? GetParameter<uint>(args, "-loginid")
                : _config?.LoginId,
            VerifyDiskSpace = !HasParameter(args, "-skip-disk-check") &&
                              !HasParameter(args, "--skip-disk-check") &&
                              !(_config?.SkipDiskCheck ?? false)
        };

        // Cell ID (CLI overrides config)
        var cellId = GetParameter(args, "-cellid", _config?.CellId ?? -1);
        options.CellId = cellId == -1 ? 0 : cellId;

        // Lancache detection
        if (useLancache)
        {
            await SteamKit2.CDN.Client.DetectLancacheServerAsync();
            if (SteamKit2.CDN.Client.UseLancacheServer)
            {
                _userInterface.WriteLine(
                    "Detected Lancache server! Downloads will be directed through the Lancache.");

                // Increase concurrent downloads for lancache if not explicitly set
                if (!HasParameter(args, "-max-downloads") && _config?.MaxDownloads is null)
                    options.MaxDownloads = 25;
            }
        }

        // File list (CLI overrides config)
        var fileList = GetParameter<string>(args, "-filelist") ?? _config?.FileList;
        if (fileList is not null)
        {
            const string regexPrefix = "regex:";

            try
            {
                options.FilesToDownload = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                options.FilesToDownloadRegex = [];

                var files = await File.ReadAllLinesAsync(fileList);

                foreach (var fileEntry in files)
                {
                    if (string.IsNullOrWhiteSpace(fileEntry)) continue;

                    if (fileEntry.StartsWith(regexPrefix))
                    {
                        var rgx = new Regex(fileEntry[regexPrefix.Length..],
                            RegexOptions.Compiled | RegexOptions.IgnoreCase);
                        options.FilesToDownloadRegex.Add(rgx);
                    }
                    else
                    {
                        options.FilesToDownload.Add(fileEntry.Replace('\\', '/'));
                    }
                }

                _userInterface.WriteLine("Using filelist: '{0}'.", fileList);
            }
            catch (Exception ex)
            {
                _userInterface.WriteLine("Warning: Unable to load filelist: {0}", ex);
            }
        }

        // Branch options (CLI overrides config)
        var branch = GetParameter<string>(args, "-branch") ??
                     GetParameter<string>(args, "-beta") ??
                     _config?.Branch ??
                     SteamConstants.DefaultBranch;
        options.Branch = branch;
        options.BranchPassword = GetParameter<string>(args, "-branchpassword") ??
                                 GetParameter<string>(args, "-betapassword") ??
                                 _config?.BranchPassword;

        if (!string.IsNullOrEmpty(options.BranchPassword) && string.IsNullOrEmpty(branch))
            throw new ArgumentException("Cannot specify -branchpassword when -branch is not specified.");

        // Platform options (CLI overrides config)
        options.DownloadAllPlatforms = HasParameter(args, "-all-platforms") || (_config?.AllPlatforms ?? false);
        options.Os = GetParameter<string>(args, "-os") ?? _config?.Os;

        if (options.DownloadAllPlatforms && !string.IsNullOrEmpty(options.Os))
            throw new ArgumentException("Cannot specify -os when -all-platforms is specified.");

        // Architecture options (CLI overrides config)
        options.DownloadAllArchs = HasParameter(args, "-all-archs") || (_config?.AllArchs ?? false);
        options.Architecture = GetParameter<string>(args, "-osarch") ?? _config?.OsArch;

        if (options.DownloadAllArchs && !string.IsNullOrEmpty(options.Architecture))
            throw new ArgumentException("Cannot specify -osarch when -all-archs is specified.");

        // Language options (CLI overrides config)
        options.DownloadAllLanguages = HasParameter(args, "-all-languages") || (_config?.AllLanguages ?? false);
        options.Language = GetParameter<string>(args, "-language") ?? _config?.Language;

        if (options.DownloadAllLanguages && !string.IsNullOrEmpty(options.Language))
            throw new ArgumentException("Cannot specify -language when -all-languages is specified.");

        // Low violence (CLI overrides config)
        options.LowViolence = HasParameter(args, "-lowviolence") || (_config?.LowViolence ?? false);

        // Depot and manifest IDs (CLI overrides config)
        var depotIdList = GetParameterList<uint>(args, "-depot");
        var manifestIdList = GetParameterList<ulong>(args, "-manifest");

        // If CLI didn't specify depots, use config
        if (depotIdList.Count == 0 && _config?.Depots is { Count: > 0 })
        {
            depotIdList = _config.Depots;
            manifestIdList = _config.Manifests ?? [];
        }

        if (manifestIdList.Count > 0)
        {
            if (depotIdList.Count != manifestIdList.Count)
                throw new ArgumentException("-manifest requires one id for every -depot specified");

            options.DepotManifestIds =
                [.. depotIdList.Zip(manifestIdList, (depotId, manifestId) => (depotId, manifestId))];
        }
        else
        {
            options.DepotManifestIds =
                [.. depotIdList.Select(depotId => (depotId, SteamConstants.InvalidManifestId))];
        }

        return options;
    }

    private static int IndexOfParam(string[] args, string param)
    {
        for (var x = 0; x < args.Length; ++x)
            if (args[x].Equals(param, StringComparison.OrdinalIgnoreCase))
            {
                _consumedArgs[x] = true;
                return x;
            }

        return -1;
    }

    private static bool HasParameter(string[] args, string param)
    {
        return IndexOfParam(args, param) > -1;
    }

    private static T GetParameter<T>(string[] args, string param, T defaultValue = default)
    {
        var index = IndexOfParam(args, param);

        if (index == -1 || index == args.Length - 1)
            return defaultValue;

        var strParam = args[index + 1];

        var converter = TypeDescriptor.GetConverter(typeof(T));
        _consumedArgs[index + 1] = true;
        return (T)converter.ConvertFromString(strParam);
    }

    private static List<T> GetParameterList<T>(string[] args, string param)
    {
        var list = new List<T>();
        var index = IndexOfParam(args, param);

        if (index == -1 || index == args.Length - 1)
            return list;

        index++;

        while (index < args.Length)
        {
            var strParam = args[index];

            if (strParam[0] == '-') break;

            var converter = TypeDescriptor.GetConverter(typeof(T));
            _consumedArgs[index] = true;
            list.Add((T)converter.ConvertFromString(strParam));

            index++;
        }

        return list;
    }

    private static void PrintUnconsumedArgs(string[] args)
    {
        var printError = false;

        for (var index = 0; index < _consumedArgs.Length; index++)
            if (!_consumedArgs[index])
            {
                printError = true;
                _userInterface.WriteError($"Argument #{index + 1} {args[index]} was not used.");
            }

        if (printError)
        {
            _userInterface.WriteError(
                "Make sure you specified the arguments correctly. Check --help for correct arguments.");
            _userInterface.WriteError("");
        }
    }

    private static void PrintUsage()
    {
        // Do not use tabs to align parameters here because tab size may differ
        _userInterface.WriteLine();
        _userInterface.WriteLine("Usage: downloading one or all depots for an app:");
        _userInterface.WriteLine("       depotdownloader -app <id> [-depot <id> [-manifest <id>]]");
        _userInterface.WriteLine(
            "                       [-username <username> [-password <password>]] [other options]");
        _userInterface.WriteLine();
        _userInterface.WriteLine("Usage: listing depots or branches for an app:");
        _userInterface.WriteLine("       depotdownloader -app <id> -list-depots [-username <username>]");
        _userInterface.WriteLine("       depotdownloader -app <id> -list-branches [-username <username>]");
        _userInterface.WriteLine();
        _userInterface.WriteLine("Usage: downloading a workshop item using pubfile id");
        _userInterface.WriteLine(
            "       depotdownloader -app <id> -pubfile <id> [-username <username> [-password <password>]]");
        _userInterface.WriteLine("Usage: downloading a workshop item using ugc id");
        _userInterface.WriteLine(
            "       depotdownloader -app <id> -ugc <id> [-username <username> [-password <password>]]");
        _userInterface.WriteLine();
        _userInterface.WriteLine("Usage: using a config file:");
        _userInterface.WriteLine("       depotdownloader -config <config.json> [overrides]");
        _userInterface.WriteLine();
        _userInterface.WriteLine("Parameters:");
        _userInterface.WriteLine("  -app <#>                 - the AppID to download.");
        _userInterface.WriteLine("  -depot <#>               - the DepotID to download.");
        _userInterface.WriteLine(
            "  -manifest <id>           - manifest id of content to download (requires -depot, default: current for branch).");
        _userInterface.WriteLine(
            $"  -branch <branchname>     - download from specified branch if available (default: {SteamConstants.DefaultBranch}).");
        _userInterface.WriteLine("  -branchpassword <pass>   - branch password if applicable.");
        _userInterface.WriteLine(
            "  -all-platforms           - downloads all platform-specific depots when -app is used.");
        _userInterface.WriteLine(
            "  -all-archs               - download all architecture-specific depots when -app is used.");
        _userInterface.WriteLine(
            "  -os <os>                 - the operating system for which to download the game (windows, macos or linux, default: OS the program is currently running on)");
        _userInterface.WriteLine(
            "  -osarch <arch>           - the architecture for which to download the game (32 or 64, default: the host's architecture)");
        _userInterface.WriteLine(
            "  -all-languages           - download all language-specific depots when -app is used.");
        _userInterface.WriteLine(
            "  -language <lang>         - the language for which to download the game (default: english)");
        _userInterface.WriteLine("  -lowviolence             - download low violence depots when -app is used.");
        _userInterface.WriteLine();
        _userInterface.WriteLine("  -ugc <#>                 - the UGC ID to download.");
        _userInterface.WriteLine(
            "  -pubfile <#>             - the PublishedFileId to download. (Will automatically resolve to UGC id)");
        _userInterface.WriteLine();
        _userInterface.WriteLine(
            "  -username <user>         - the username of the account to login to for restricted content.");
        _userInterface.WriteLine(
            "  -password <pass>         - the password of the account to login to for restricted content.");
        _userInterface.WriteLine(
            "  -remember-password       - if set, remember the password for subsequent logins of this user.");
        _userInterface.WriteLine(
            "                             use -username <username> -remember-password as login credentials.");
        _userInterface.WriteLine(
            "  -qr                      - display a login QR code to be scanned with the Steam mobile app");
        _userInterface.WriteLine(
            "  -no-mobile               - prefer entering a 2FA code instead of prompting to accept in the Steam mobile app");
        _userInterface.WriteLine();
        _userInterface.WriteLine("  -dir <installdir>        - the directory in which to place downloaded files.");
        _userInterface.WriteLine(
            "  -filelist <file.txt>     - the name of a local file that contains a list of files to download (from the manifest).");
        _userInterface.WriteLine(
            "                             prefix file path with `regex:` if you want to match with regex. each file path should be on their own line.");
        _userInterface.WriteLine();
        _userInterface.WriteLine(
            "  -validate                - include checksum verification of files already downloaded");
        _userInterface.WriteLine(
            "  -manifest-only           - downloads a human readable manifest for any depots that would be downloaded.");
        _userInterface.WriteLine(
            "  -cellid <#>              - the overridden CellID of the content server to download from.");
        _userInterface.WriteLine(
            "  -max-downloads <#>       - maximum number of chunks to download concurrently. (default: 8).");
        _userInterface.WriteLine(
            "  -loginid <#>             - a unique 32-bit integer Steam LogonID in decimal, required if running multiple instances of DepotDownloader concurrently.");
        _userInterface.WriteLine(
            "  -use-lancache            - forces downloads over the local network via a Lancache instance.");
        _userInterface.WriteLine(
            "  -skip-disk-check         - skip disk space verification before downloading.");
        _userInterface.WriteLine(
            "  -resume                  - resume a previously interrupted download.");
        _userInterface.WriteLine();
        _userInterface.WriteLine("  -config <file.json>      - load settings from a JSON configuration file.");
        _userInterface.WriteLine("                             CLI arguments override config file settings.");
        _userInterface.WriteLine();
        _userInterface.WriteLine("  -list-depots             - list all depots for the specified app and exit.");
        _userInterface.WriteLine("  -list-branches           - list all branches for the specified app and exit.");
        _userInterface.WriteLine("  -dry-run                 - show what would be downloaded without downloading.");
        _userInterface.WriteLine("  -verbose, -v             - show detailed output (e.g., file list in dry-run).");
        _userInterface.WriteLine();
        _userInterface.WriteLine(
            "  -json                    - output results in JSON format for scripting/automation.");
        _userInterface.WriteLine("  -no-progress             - disable the progress bar during downloads.");
        _userInterface.WriteLine();
        _userInterface.WriteLine("  -debug                   - enable verbose debug logging.");
        _userInterface.WriteLine("  -V or --version          - print version and runtime.");
    }

    private static void PrintVersion(bool printExtra = false)
    {
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";

        _userInterface.WriteLine($"DepotDownloader v{version}");

        if (!printExtra) return;

        _userInterface.WriteLine(
            $"Runtime: {RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription}");
    }

    private static void WriteJsonError(string message)
    {
        JsonOutput.WriteError(message);
    }

    private static void WriteJsonSuccess(uint appId, TimeSpan duration)
    {
        JsonOutput.WriteSuccess(new DownloadResultJson
        {
            AppId = appId,
            DurationSeconds = duration.TotalSeconds
        });
    }
}
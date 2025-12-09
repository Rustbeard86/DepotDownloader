using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DepotDownloader.Lib;

namespace DepotDownloader.Client;

internal class Program
{
    private static bool[] _consumedArgs;
    private static ConsoleUserInterface _userInterface;

    private static async Task<int> Main(string[] args)
    {
        // Initialize the user interface first
        _userInterface = new ConsoleUserInterface();

        if (args.Length == 0)
        {
            PrintVersion();
            PrintUsage();
            return 0;
        }

        // Not using HasParameter because it is case-insensitive
        if (args.Length == 1 && (args[0] == "-V" || args[0] == "--version"))
        {
            PrintVersion(true);
            return 0;
        }

        _consumedArgs = new bool[args.Length];

        // Create the library client - this handles all initialization
        using var client = new DepotDownloaderClient(_userInterface);

        // Enable debug logging if requested
        if (HasParameter(args, "-debug"))
        {
            PrintVersion(true);
            client.EnableDebugLogging();
        }

        // Parse authentication parameters
        var username = GetParameter<string>(args, "-username") ?? GetParameter<string>(args, "-user");
        var password = GetParameter<string>(args, "-password") ?? GetParameter<string>(args, "-pass");
        var rememberPassword = HasParameter(args, "-remember-password");
        var useQrCode = HasParameter(args, "-qr");
        var skipAppConfirmation = HasParameter(args, "-no-mobile");

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

        // Parse download options
        var appId = GetParameter(args, "-app", ContentDownloader.InvalidAppId);
        if (appId == ContentDownloader.InvalidAppId)
        {
            _userInterface.WriteLine("Error: -app not specified!");
            return 1;
        }

        var pubFile = GetParameter(args, "-pubfile", ContentDownloader.InvalidManifestId);
        var ugcId = GetParameter(args, "-ugc", ContentDownloader.InvalidManifestId);

        // Build download options
        var options = await BuildDownloadOptionsAsync(args, appId);

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
            _userInterface.WriteLine("Error: Authentication failed");
            return 1;
        }

        // Perform download
        try
        {
            if (pubFile != ContentDownloader.InvalidManifestId)
                await client.DownloadPublishedFileAsync(appId, pubFile);
            else if (ugcId != ContentDownloader.InvalidManifestId)
                await client.DownloadUgcAsync(appId, ugcId);
            else
                await client.DownloadAppAsync(options);

            return 0;
        }
        catch (ContentDownloaderException ex)
        {
            _userInterface.WriteLine(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            _userInterface.WriteLine("Download was cancelled.");
            return 1;
        }
        catch (Exception e)
        {
            _userInterface.WriteLine("Download failed due to an unhandled exception: {0}", e.Message);
            throw;
        }
    }

    private static async Task<DepotDownloadOptions> BuildDownloadOptionsAsync(string[] args, uint appId)
    {
        var options = new DepotDownloadOptions
        {
            AppId = appId,
            DownloadManifestOnly = HasParameter(args, "-manifest-only"),
            InstallDirectory = GetParameter<string>(args, "-dir"),
            VerifyAll = HasParameter(args, "-verify-all") ||
                        HasParameter(args, "-verify_all") ||
                        HasParameter(args, "-validate"),
            MaxDownloads = GetParameter(args, "-max-downloads", 8),
            LoginId = HasParameter(args, "-loginid") ? GetParameter<uint>(args, "-loginid") : null
        };

        // Cell ID
        var cellId = GetParameter(args, "-cellid", -1);
        options.CellId = cellId == -1 ? 0 : cellId;

        // Lancache detection
        if (HasParameter(args, "-use-lancache"))
        {
            await SteamKit2.CDN.Client.DetectLancacheServerAsync();
            if (SteamKit2.CDN.Client.UseLancacheServer)
            {
                _userInterface.WriteLine(
                    "Detected Lancache server! Downloads will be directed through the Lancache.");

                // Increase concurrent downloads for lancache
                if (!HasParameter(args, "-max-downloads")) options.MaxDownloads = 25;
            }
        }

        // File list
        var fileList = GetParameter<string>(args, "-filelist");
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

        // Branch options
        var branch = GetParameter<string>(args, "-branch") ??
                     GetParameter<string>(args, "-beta") ??
                     ContentDownloader.DefaultBranch;
        options.Branch = branch;
        options.BranchPassword = GetParameter<string>(args, "-branchpassword") ??
                                 GetParameter<string>(args, "-betapassword");

        if (!string.IsNullOrEmpty(options.BranchPassword) && string.IsNullOrEmpty(branch))
            throw new ArgumentException("Cannot specify -branchpassword when -branch is not specified.");

        // Platform options
        options.DownloadAllPlatforms = HasParameter(args, "-all-platforms");
        options.Os = GetParameter<string>(args, "-os");

        if (options.DownloadAllPlatforms && !string.IsNullOrEmpty(options.Os))
            throw new ArgumentException("Cannot specify -os when -all-platforms is specified.");

        // Architecture options
        options.DownloadAllArchs = HasParameter(args, "-all-archs");
        options.Architecture = GetParameter<string>(args, "-osarch");

        if (options.DownloadAllArchs && !string.IsNullOrEmpty(options.Architecture))
            throw new ArgumentException("Cannot specify -osarch when -all-archs is specified.");

        // Language options
        options.DownloadAllLanguages = HasParameter(args, "-all-languages");
        options.Language = GetParameter<string>(args, "-language");

        if (options.DownloadAllLanguages && !string.IsNullOrEmpty(options.Language))
            throw new ArgumentException("Cannot specify -language when -all-languages is specified.");

        // Low violence
        options.LowViolence = HasParameter(args, "-lowviolence");

        // Depot and manifest IDs
        var depotIdList = GetParameterList<uint>(args, "-depot");
        var manifestIdList = GetParameterList<ulong>(args, "-manifest");

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
                [.. depotIdList.Select(depotId => (depotId, ContentDownloader.InvalidManifestId))];
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
        _userInterface.WriteLine("Usage: downloading a workshop item using pubfile id");
        _userInterface.WriteLine(
            "       depotdownloader -app <id> -pubfile <id> [-username <username> [-password <password>]]");
        _userInterface.WriteLine("Usage: downloading a workshop item using ugc id");
        _userInterface.WriteLine(
            "       depotdownloader -app <id> -ugc <id> [-username <username> [-password <password>]]");
        _userInterface.WriteLine();
        _userInterface.WriteLine("Parameters:");
        _userInterface.WriteLine("  -app <#>                 - the AppID to download.");
        _userInterface.WriteLine("  -depot <#>               - the DepotID to download.");
        _userInterface.WriteLine(
            "  -manifest <id>           - manifest id of content to download (requires -depot, default: current for branch).");
        _userInterface.WriteLine(
            $"  -branch <branchname>    - download from specified branch if available (default: {ContentDownloader.DefaultBranch}).");
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
}
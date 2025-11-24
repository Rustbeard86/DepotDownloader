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
using SteamKit2;

namespace DepotDownloader.Client;

internal class Program
{
    private static bool[] _consumedArgs;
    private static ConsoleUserInterface _userInterface;
    private static HttpDiagnosticEventListener _httpEventListener;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            // Initialize the user interface first
            _userInterface = new ConsoleUserInterface();

            // Initialize all components that need IUserInterface
            Ansi.Initialize(_userInterface);
            AccountSettingsStore.Initialize(_userInterface);
            DepotConfigStore.Initialize(_userInterface);
            ContentDownloader.Initialize(_userInterface);
            Util.Initialize(_userInterface);

            if (args.Length == 0)
            {
                PrintVersion();
                PrintUsage();

                return 0;
            }

            Ansi.Init();

            DebugLog.Enabled = false;

            AccountSettingsStore.LoadFromFile("account.config");

            #region Common Options

            // Not using HasParameter because it is case-insensitive
            if (args.Length == 1 && (args[0] == "-V" || args[0] == "--version"))
            {
                PrintVersion(true);
                return 0;
            }

            _consumedArgs = new bool[args.Length];

            // Note: When -debug is enabled, you may see a TaskCanceledException from WebSocketContext
            // during shutdown. This is normal SteamKit2 behavior when cancelling the connection.
            if (HasParameter(args, "-debug"))
            {
                PrintVersion(true);

                DebugLog.Enabled = true;
                DebugLog.AddListener((category, message) => { _userInterface.WriteDebug(category, message); });

                // Enable HTTP diagnostics for network debugging - keep reference to prevent GC
                _httpEventListener = new HttpDiagnosticEventListener(_userInterface);
            }

            var username = GetParameter<string>(args, "-username") ?? GetParameter<string>(args, "-user");
            var password = GetParameter<string>(args, "-password") ?? GetParameter<string>(args, "-pass");
            ContentDownloader.Config.RememberPassword = HasParameter(args, "-remember-password");
            ContentDownloader.Config.UseQrCode = HasParameter(args, "-qr");
            ContentDownloader.Config.SkipAppConfirmation = HasParameter(args, "-no-mobile");

            if (username == null)
            {
                if (ContentDownloader.Config.RememberPassword && !ContentDownloader.Config.UseQrCode)
                {
                    _userInterface.WriteLine("Error: -remember-password can not be used without -username or -qr.");
                    return 1;
                }
            }
            else if (ContentDownloader.Config.UseQrCode)
            {
                _userInterface.WriteLine("Error: -qr can not be used with -username.");
                return 1;
            }

            ContentDownloader.Config.DownloadManifestOnly = HasParameter(args, "-manifest-only");

            var cellId = GetParameter(args, "-cellid", -1);
            if (cellId == -1) cellId = 0;

            ContentDownloader.Config.CellId = cellId;

            var fileList = GetParameter<string>(args, "-filelist");

            if (fileList != null)
            {
                const string regexPrefix = "regex:";

                try
                {
                    ContentDownloader.Config.UsingFileList = true;
                    ContentDownloader.Config.FilesToDownload = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    ContentDownloader.Config.FilesToDownloadRegex = [];

                    var files = await File.ReadAllLinesAsync(fileList);

                    foreach (var fileEntry in files)
                    {
                        if (string.IsNullOrWhiteSpace(fileEntry)) continue;

                        if (fileEntry.StartsWith(regexPrefix))
                        {
                            var rgx = new Regex(fileEntry[regexPrefix.Length..],
                                RegexOptions.Compiled | RegexOptions.IgnoreCase);
                            ContentDownloader.Config.FilesToDownloadRegex.Add(rgx);
                        }
                        else
                        {
                            ContentDownloader.Config.FilesToDownload.Add(fileEntry.Replace('\\', '/'));
                        }
                    }

                    _userInterface.WriteLine("Using filelist: '{0}'.", fileList);
                }
                catch (Exception ex)
                {
                    _userInterface.WriteLine("Warning: Unable to load filelist: {0}", ex);
                }
            }

            ContentDownloader.Config.InstallDirectory = GetParameter<string>(args, "-dir");

            ContentDownloader.Config.VerifyAll =
                HasParameter(args, "-verify-all") || HasParameter(args, "-verify_all") ||
                HasParameter(args, "-validate");

            if (HasParameter(args, "-use-lancache"))
            {
                await SteamKit2.CDN.Client.DetectLancacheServerAsync();
                if (SteamKit2.CDN.Client.UseLancacheServer)
                {
                    _userInterface.WriteLine(
                        "Detected Lancache server! Downloads will be directed through the Lancache.");

                    // Increasing the number of concurrent downloads when the cache is detected since the downloads will likely
                    // be served much faster than over the internet.  Steam internally has this behavior as well.
                    if (!HasParameter(args, "-max-downloads")) ContentDownloader.Config.MaxDownloads = 25;
                }
            }

            ContentDownloader.Config.MaxDownloads = GetParameter(args, "-max-downloads", 8);
            ContentDownloader.Config.LoginId =
                HasParameter(args, "-loginid") ? GetParameter<uint>(args, "-loginid") : null;

            #endregion

            var appId = GetParameter(args, "-app", ContentDownloader.InvalidAppId);
            if (appId == ContentDownloader.InvalidAppId)
            {
                _userInterface.WriteLine("Error: -app not specified!");
                return 1;
            }

            var pubFile = GetParameter(args, "-pubfile", ContentDownloader.InvalidManifestId);
            var ugcId = GetParameter(args, "-ugc", ContentDownloader.InvalidManifestId);
            if (pubFile != ContentDownloader.InvalidManifestId)
            {
                #region Pubfile Downloading

                PrintUnconsumedArgs(args);

                if (InitializeSteam(username, password))
                {
                    try
                    {
                        await ContentDownloader.DownloadPubfileAsync(appId, pubFile).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (
                        ex is ContentDownloaderException
                        || ex is OperationCanceledException)
                    {
                        _userInterface.WriteLine(ex.Message);
                        return 1;
                    }
                    catch (Exception e)
                    {
                        _userInterface.WriteLine("Download failed to due to an unhandled exception: {0}", e.Message);
                        throw;
                    }
                    finally
                    {
                        ContentDownloader.ShutdownSteam3();
                    }
                }
                else
                {
                    _userInterface.WriteLine("Error: InitializeSteam failed");
                    return 1;
                }

                #endregion
            }
            else if (ugcId != ContentDownloader.InvalidManifestId)
            {
                #region UGC Downloading

                PrintUnconsumedArgs(args);

                if (InitializeSteam(username, password))
                {
                    try
                    {
                        await ContentDownloader.DownloadUgcAsync(appId, ugcId).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (
                        ex is ContentDownloaderException
                        || ex is OperationCanceledException)
                    {
                        _userInterface.WriteLine(ex.Message);
                        return 1;
                    }
                    catch (Exception e)
                    {
                        _userInterface.WriteLine("Download failed to due to an unhandled exception: {0}", e.Message);
                        throw;
                    }
                    finally
                    {
                        ContentDownloader.ShutdownSteam3();
                    }
                }
                else
                {
                    _userInterface.WriteLine("Error: InitializeSteam failed");
                    return 1;
                }

                #endregion
            }
            else
            {
                #region App downloading

                var branch = GetParameter<string>(args, "-branch") ??
                             GetParameter<string>(args, "-beta") ?? ContentDownloader.DefaultBranch;
                ContentDownloader.Config.BetaPassword = GetParameter<string>(args, "-branchpassword") ??
                                                        GetParameter<string>(args, "-betapassword");

                if (!string.IsNullOrEmpty(ContentDownloader.Config.BetaPassword) && string.IsNullOrEmpty(branch))
                {
                    _userInterface.WriteLine("Error: Cannot specify -branchpassword when -branch is not specified.");
                    return 1;
                }

                ContentDownloader.Config.DownloadAllPlatforms = HasParameter(args, "-all-platforms");

                var os = GetParameter<string>(args, "-os");

                if (ContentDownloader.Config.DownloadAllPlatforms && !string.IsNullOrEmpty(os))
                {
                    _userInterface.WriteLine("Error: Cannot specify -os when -all-platforms is specified.");
                    return 1;
                }

                ContentDownloader.Config.DownloadAllArchs = HasParameter(args, "-all-archs");

                var arch = GetParameter<string>(args, "-osarch");

                if (ContentDownloader.Config.DownloadAllArchs && !string.IsNullOrEmpty(arch))
                {
                    _userInterface.WriteLine("Error: Cannot specify -osarch when -all-archs is specified.");
                    return 1;
                }

                ContentDownloader.Config.DownloadAllLanguages = HasParameter(args, "-all-languages");
                var language = GetParameter<string>(args, "-language");

                if (ContentDownloader.Config.DownloadAllLanguages && !string.IsNullOrEmpty(language))
                {
                    _userInterface.WriteLine("Error: Cannot specify -language when -all-languages is specified.");
                    return 1;
                }

                var lv = HasParameter(args, "-lowviolence");

                var depotManifestIds = new List<(uint, ulong)>();
                var isUgc = false;

                var depotIdList = GetParameterList<uint>(args, "-depot");
                var manifestIdList = GetParameterList<ulong>(args, "-manifest");
                if (manifestIdList.Count > 0)
                {
                    if (depotIdList.Count != manifestIdList.Count)
                    {
                        _userInterface.WriteLine("Error: -manifest requires one id for every -depot specified");
                        return 1;
                    }

                    var zippedDepotManifest =
                        depotIdList.Zip(manifestIdList, (depotId, manifestId) => (depotId, manifestId));
                    depotManifestIds.AddRange(zippedDepotManifest);
                }
                else
                {
                    depotManifestIds.AddRange(depotIdList.Select(depotId =>
                        (depotId, INVALID_MANIFEST_ID: ContentDownloader.InvalidManifestId)));
                }

                PrintUnconsumedArgs(args);

                if (InitializeSteam(username, password))
                {
                    try
                    {
                        await ContentDownloader
                            .DownloadAppAsync(appId, depotManifestIds, branch, os, arch, language, lv, isUgc)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (
                        ex is ContentDownloaderException
                        || ex is OperationCanceledException)
                    {
                        _userInterface.WriteLine(ex.Message);
                        return 1;
                    }
                    catch (Exception e)
                    {
                        _userInterface.WriteLine("Download failed to due to an unhandled exception: {0}", e.Message);
                        throw;
                    }
                    finally
                    {
                        ContentDownloader.ShutdownSteam3();
                    }
                }
                else
                {
                    _userInterface.WriteLine("Error: InitializeSteam failed");
                    return 1;
                }

                #endregion
            }

            return 0;
        }
        finally
        {
            // Dispose of the HTTP event listener to clean up event subscriptions
            _httpEventListener?.Dispose();
        }
    }

    private static bool InitializeSteam(string username, string password)
    {
        if (!ContentDownloader.Config.UseQrCode)
        {
            if (username != null && password == null && (!ContentDownloader.Config.RememberPassword ||
                                                         !AccountSettingsStore.Instance.LoginTokens.ContainsKey(
                                                             username)))
            {
                if (AccountSettingsStore.Instance.LoginTokens.ContainsKey(username))
                    _userInterface.WriteLine(
                        $"Account \"{username}\" has stored credentials. Did you forget to specify -remember-password?");

                do
                {
                    _userInterface.Write("Enter account password for \"{0}\": ", username);
                    password = _userInterface.IsInputRedirected
                        ? _userInterface.ReadLine()
                        :
                        // Avoid console echoing of password
                        _userInterface.ReadPassword();

                    _userInterface.WriteLine();
                } while (string.Empty == password);
            }
            else if (username == null)
            {
                _userInterface.WriteLine(
                    "No username given. Using anonymous account with dedicated server subscription.");
            }
        }

        if (!string.IsNullOrEmpty(password))
        {
            const int maxPasswordSize = 64;

            if (password.Length > maxPasswordSize)
                _userInterface.WriteError(
                    $"Warning: Password is longer than {maxPasswordSize} characters, which is not supported by Steam.");

            if (!password.All(char.IsAscii))
                _userInterface.WriteError(
                    "Warning: Password contains non-ASCII characters, which is not supported by Steam.");
        }

        return ContentDownloader.InitializeSteam3(username, password);
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
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using DepotDownloader.Client.Commands;
using DepotDownloader.Lib;

namespace DepotDownloader.Client;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var ui = new ConsoleUserInterface();

        if (args.Length == 0)
        {
            PrintVersion(ui);
            HelpGenerator.PrintFullHelp(ui);
            return 0;
        }

        // Handle help requests
        if (args.Length == 1 && (args[0] == "-h" || args[0] == "--help"))
        {
            HelpGenerator.PrintFullHelp(ui);
            return 0;
        }

        if (args.Length == 2 && (args[0] == "-h" || args[0] == "--help"))
        {
            HelpGenerator.PrintCommandHelp(ui, args[1]);
            return 0;
        }

        if (args.Length == 1 && (args[0] == "-V" || args[0] == "--version"))
        {
            PrintVersion(ui, true);
            return 0;
        }

        var parser = new ArgumentParser(args);

        // Load config file
        var configPath = parser.Get<string>(null, "-config", "--config");
        ConfigFile config = null;
        if (configPath is not null)
            try
            {
                var configJson = await File.ReadAllTextAsync(configPath);
                config = JsonSerializer.Deserialize(configJson, ConfigFileContext.Default.ConfigFile);
                ui.WriteLine("Loaded config from: {0}", configPath);
            }
            catch (Exception ex)
            {
                ui.WriteError("Error loading config file: {0}", ex.Message);
                return 1;
            }

        using var client = new DepotDownloaderClient(ui);

        // Enable debug logging
        if (parser.Has("-debug") || (config?.Debug ?? false))
        {
            PrintVersion(ui, true);
            client.EnableDebugLogging();
        }

        // Parse authentication
        var username = parser.Get<string>(null, "-username", "-user") ?? config?.Username;
        var password = parser.Get<string>(null, "-password", "-pass");
        var rememberPassword = parser.Has("-remember-password") || (config?.RememberPassword ?? false);
        var useQrCode = parser.Has("-qr") || (config?.UseQrCode ?? false);
        var skipAppConfirmation = parser.Has("-no-mobile") || (config?.NoMobile ?? false);

        // Validate authentication combinations
        if (username is null && rememberPassword && !useQrCode)
        {
            ui.WriteLine("Error: -remember-password can not be used without -username or -qr.");
            return 1;
        }

        if (username is not null && useQrCode)
        {
            ui.WriteLine("Error: -qr can not be used with -username.");
            return 1;
        }

        // Parse app ID
        var appId = parser.Get(config?.AppId ?? SteamConstants.InvalidAppId, "-app");
        if (appId == SteamConstants.InvalidAppId)
        {
            ui.WriteLine("Error: -app not specified!");
            return 1;
        }

        // Parse command flags
        var jsonOutput = parser.Has("-json", "--json");

        // Support both full names and aliases
        var listDepots = parser.Has("-list-depots", "--list-depots") ||
                         parser.Has("-depots", "-list-depot");
        var listBranches = parser.Has("-list-branches", "--list-branches") ||
                           parser.Has("-branches", "-list-branch");
        var getManifest = parser.Has("-get-manifest", "--get-manifest") ||
                          parser.Has("-manifest", "-show-manifest");
        var checkSpace = parser.Has("-check-space", "--check-space") ||
                         parser.Has("-space", "-disk-space");
        var dryRun = parser.Has("-dry-run", "--dry-run") ||
                     parser.Has("-plan", "-preview");

        var verbose = parser.Has("-verbose", "-v");
        var noProgress = parser.Has("-no-progress", "--no-progress");
        var noResume = parser.Has("-no-resume", "--no-resume");
        var failFast = parser.Has("-fail-fast", "--fail-fast");

        var pubFile = parser.Get(SteamConstants.InvalidManifestId, "-pubfile");
        var ugcId = parser.Get(SteamConstants.InvalidManifestId, "-ugc");

        // Build download options (unless query-only)
        DepotDownloadOptions options = null;
        if (!listDepots && !listBranches && !getManifest)
        {
            var optionsBuilder = new OptionsBuilder(parser, config, ui);
            options = await optionsBuilder.BuildAsync(appId);
            options.Resume = !noResume;
            options.FailFast = failFast;
        }

        // Check unconsumed arguments
        if (parser.HasUnconsumedArgs())
        {
            foreach (var arg in parser.GetUnconsumedArgs())
                ui.WriteError(arg + " was not used.");
            ui.WriteError("Make sure you specified the arguments correctly. Check --help for correct arguments.");
            ui.WriteError("");
        }

        // Authenticate
        var loginSuccess = useQrCode
            ? client.LoginWithQrCode(rememberPassword, skipAppConfirmation)
            : username is not null
                ? client.Login(username, password, rememberPassword, skipAppConfirmation)
                : client.LoginAnonymous(skipAppConfirmation);

        if (!loginSuccess)
        {
            if (jsonOutput)
                JsonOutput.WriteError("Authentication failed");
            else
                ui.WriteLine("Error: Authentication failed");
            return 1;
        }

        // Create command context
        var context = new CommandContext(client, ui, parser, config, jsonOutput);

        // Route to command handler
        ICommand command = (listDepots, listBranches, getManifest, checkSpace, dryRun) switch
        {
            (true, _, _, _, _) => new ListDepotsCommand(appId),
            (_, true, _, _, _) => new ListBranchesCommand(appId),
            (_, _, true, _, _) => new GetManifestCommand(
                appId,
                parser.Get<uint>(0, "-depot"),
                parser.Get<string>(null, "-branch") ?? SteamConstants.DefaultBranch,
                parser.Get<string>(null, "-branchpassword")),
            (_, _, _, true, _) => new CheckSpaceCommand(options),
            (_, _, _, _, true) => new DryRunCommand(options, verbose),
            _ => new DownloadCommand(options, pubFile, ugcId, noProgress)
        };

        return await command.ExecuteAsync(context);
    }

    private static void PrintVersion(ConsoleUserInterface ui, bool printExtra = false)
    {
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";

        ui.WriteLine($"DepotDownloader v{version}");

        if (printExtra)
            ui.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription}");
    }
}
using DepotDownloader.Lib;
using GitHubArchiver.Daemon;
using Microsoft.Extensions.Options;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // Check for special modes
    var isSetup = args.Contains("--setup");
    var isRebuildManifest = args.Contains("--rebuild-manifest");

    if (isSetup)
    {
        await RunSetupModeAsync(args);
        return 0;
    }

    if (isRebuildManifest)
    {
        await RunRebuildManifestAsync();
        return 0;
    }

    var builder = Host.CreateApplicationBuilder(args);

    // Add platform-specific configuration
    var environmentName = builder.Environment.EnvironmentName;
    builder.Configuration
        .AddJsonFile("appsettings.json", false, true)
        .AddJsonFile($"appsettings.{environmentName}.json", true, true);

    // Add Windows-specific config if on Windows
    if (OperatingSystem.IsWindows())
        builder.Configuration.AddJsonFile("appsettings.windows.json", true, true);

    builder.Services.AddSerilog((services, config) => config
        .ReadFrom.Services(services)
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(AppContext.BaseDirectory, "logs", "github-archiver-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

    // Add platform-specific service integration
    if (OperatingSystem.IsWindows())
        builder.Services.AddWindowsService(options => { options.ServiceName = "GitHubArchiver"; });
    else
        builder.Services.AddSystemd();

    builder.Services.Configure<WorkshopOptions>(builder.Configuration.GetSection(WorkshopOptions.SectionName));
    builder.Services.Configure<SteamOptions>(builder.Configuration.GetSection(SteamOptions.SectionName));
    builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection(GitHubOptions.SectionName));

    builder.Services.AddSingleton<IWorkshopTracker, SqliteWorkshopTracker>();
    builder.Services.AddSingleton<ISteamMetadataService, SteamMetadataService>();
    builder.Services.AddSingleton<IGitHubArchiveService, GitHubArchiveService>();
    builder.Services.AddSingleton<WorkshopDownloadService>();

    builder.Services.AddHostedService<GitHubArchiverWorker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;

static async Task RunSetupModeAsync(string[] args)
{
    Console.WriteLine("=== GitHub Archiver Setup ===");
    Console.WriteLine("This will perform an interactive Steam login to save credentials.");
    Console.WriteLine();

    // Load configuration
    var configBuilder = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", false);

    if (OperatingSystem.IsWindows())
        configBuilder.AddJsonFile("appsettings.windows.json", true);

    var config = configBuilder
        .AddEnvironmentVariables()
        .Build();

    var steamOptions = new SteamOptions();
    config.GetSection(SteamOptions.SectionName).Bind(steamOptions);

    if (string.IsNullOrEmpty(steamOptions.Username))
    {
        Console.Write("Steam Username: ");
        steamOptions.Username = Console.ReadLine();
    }
    else
    {
        Console.WriteLine($"Using configured username: {steamOptions.Username}");
    }

    Console.Write("Steam Password: ");
    steamOptions.Password = ReadPassword();
    Console.WriteLine();

    Console.WriteLine();
    Console.WriteLine("Connecting to Steam...");
    Console.WriteLine("You may be prompted for 2FA code.");
    Console.WriteLine();

    using var client = new DepotDownloaderClient(new SetupUserInterface());

    var result = client.Login(steamOptions.Username, steamOptions.Password, true);

    if (result)
    {
        Console.WriteLine();
        Console.WriteLine("Login successful! Credentials have been saved.");
        Console.WriteLine("You can now run the daemon without the --setup flag.");

        // Quick test - query workshop
        var workshopOptions = new WorkshopOptions();
        config.GetSection(WorkshopOptions.SectionName).Bind(workshopOptions);

        if (workshopOptions.AppId != 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Testing workshop query for AppId {workshopOptions.AppId}...");

            try
            {
                var queryResult = await client.QueryWorkshopItemsAsync(workshopOptions.AppId, 1, 5);
                Console.WriteLine($"Found {queryResult.TotalItems} total workshop items.");

                if (queryResult.Items.Count > 0)
                {
                    Console.WriteLine("Sample items:");
                    foreach (var item in queryResult.Items.Take(3))
                        Console.WriteLine($"  - {item.PublishedFileId}: {item.Title}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Workshop query failed: {ex.Message}");
            }
        }

        client.Logout();
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("Login failed. Please check your credentials and try again.");
    }
}

static async Task RunRebuildManifestAsync()
{
    Console.WriteLine("=== GitHub Archiver - Rebuild Manifest ===");
    Console.WriteLine("This will rebuild the manifest from GitHub releases and Steam metadata.");
    Console.WriteLine();

    // Load configuration
    var configBuilder = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", false);

    if (OperatingSystem.IsWindows())
        configBuilder.AddJsonFile("appsettings.windows.json", true);

    var config = configBuilder
        .AddEnvironmentVariables()
        .Build();

    var gitHubOptions = new GitHubOptions();
    config.GetSection(GitHubOptions.SectionName).Bind(gitHubOptions);

    using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    var logger = loggerFactory.CreateLogger<GitHubArchiveService>();
    var metaLogger = loggerFactory.CreateLogger<SteamMetadataService>();

    var steamMetadataService = new SteamMetadataService(metaLogger);
    var archiveService = new GitHubArchiveService(
        Options.Create(gitHubOptions),
        steamMetadataService,
        logger);

    Console.WriteLine("Starting manifest rebuild...");
    Console.WriteLine();

    try
    {
        var count = await archiveService.RebuildManifestAsync();
        Console.WriteLine();
        Console.WriteLine($"Manifest rebuilt successfully with {count} entries.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error rebuilding manifest: {ex.Message}");
    }
}

static string ReadPassword()
{
    var password = string.Empty;
    ConsoleKeyInfo key;

    do
    {
        key = Console.ReadKey(true);

        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password = password[..^1];
            Console.Write("\b \b");
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password += key.KeyChar;
            Console.Write("*");
        }
    } while (key.Key != ConsoleKey.Enter);

    return password;
}

internal sealed class SetupUserInterface : IUserInterface
{
    public bool IsInputRedirected => false;
    public bool IsOutputRedirected => false;

    public void Write(string message)
    {
        Console.Write(message);
    }

    public void Write(string format, params object[] args)
    {
        Console.Write(format, args);
    }

    public void WriteDebug(string category, string message)
    {
    }

    public void WriteLine()
    {
        Console.WriteLine();
    }

    public void WriteLine(string message)
    {
        Console.WriteLine(message);
    }

    public void WriteLine(string format, params object[] args)
    {
        Console.WriteLine(format, args);
    }

    public void WriteError(string message)
    {
        Console.Error.WriteLine(message);
    }

    public void WriteError(string format, params object[] args)
    {
        Console.Error.WriteLine(format, args);
    }

    public string ReadLine()
    {
        return Console.ReadLine() ?? string.Empty;
    }

    public string ReadPassword()
    {
        return GitHubArchiver.Daemon.Program.ReadPassword();
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        return Console.ReadKey(intercept);
    }

    public void UpdateProgress(ulong downloaded, ulong total)
    {
    }

    public void DisplayQrCode(string challengeUrl)
    {
        Console.WriteLine();
        Console.WriteLine("Scan this QR code with Steam mobile app:");
        Console.WriteLine(challengeUrl);
        Console.WriteLine();
    }
}

namespace GitHubArchiver.Daemon
{
    public static class Program
    {
        public static string ReadPassword()
        {
            var password = string.Empty;
            ConsoleKeyInfo key;

            do
            {
                key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password[..^1];
                    Console.Write("\b \b");
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    password += key.KeyChar;
                    Console.Write("*");
                }
            } while (key.Key != ConsoleKey.Enter);

            return password;
        }
    }
}
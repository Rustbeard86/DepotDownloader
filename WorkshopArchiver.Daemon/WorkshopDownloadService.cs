using DepotDownloader.Lib;
using Microsoft.Extensions.Options;

namespace WorkshopArchiver.Daemon;

public sealed class WorkshopDownloadService : IDisposable
{
    private readonly ICompressionService _compressionService;
    private readonly ILogger<WorkshopDownloadService> _logger;
    private readonly IOptionsMonitor<WorkshopOptions> _optionsMonitor;
    private readonly SteamOptions _steamOptions;
    private readonly IWorkshopTracker _tracker;
    private readonly DaemonUserInterface _userInterface;
    private DepotDownloaderClient? _client;
    private bool _disposed;

    public WorkshopDownloadService(
        IOptionsMonitor<WorkshopOptions> optionsMonitor,
        IOptions<SteamOptions> steamOptions,
        IWorkshopTracker tracker,
        ICompressionService compressionService,
        ILogger<WorkshopDownloadService> logger)
    {
        _optionsMonitor = optionsMonitor;
        _steamOptions = steamOptions.Value;
        _tracker = tracker;
        _compressionService = compressionService;
        _logger = logger;
        _userInterface = new DaemonUserInterface(logger);
    }

    public bool IsLoggedIn => _client is not null;

    public void Dispose()
    {
        if (_disposed)
            return;

        _client?.Dispose();
        _disposed = true;
    }

    public bool Login()
    {
        if (_client is not null)
            return true;

        _client = new DepotDownloaderClient(_userInterface);

        var username = _steamOptions.Username;
        var password = _steamOptions.Password;

        if (string.IsNullOrEmpty(username))
        {
            _logger.LogError("Steam username not configured");
            return false;
        }

        _logger.LogInformation("Logging in to Steam as {Username}", username);
        var result = _client.Login(username, password, true);

        if (!result)
        {
            _logger.LogError("Failed to login to Steam");
            _client.Dispose();
            _client = null;
            return false;
        }

        _logger.LogInformation("Successfully logged in to Steam");
        return true;
    }

    public void Logout()
    {
        if (_client is null)
            return;

        _client.Logout();
        _client.Dispose();
        _client = null;
    }

    /// <summary>
    ///     Query workshop items for the specified AppId.
    /// </summary>
    public async Task<IReadOnlyList<WorkshopItemInfo>> QueryWorkshopItemsAsync(uint appId,
        CancellationToken ct = default)
    {
        if (_client is null)
            throw new InvalidOperationException("Not logged in");

        var items = new List<WorkshopItemInfo>();
        var delay = TimeSpan.FromMilliseconds(500); // Small delay between pages

        await foreach (var item in _client.QueryAllWorkshopItemsAsync(appId, delay, ct)) items.Add(item);

        _logger.LogInformation("Found {Count} workshop items for app {AppId}", items.Count, appId);
        return items;
    }

    public async Task<bool> DownloadAndArchiveItemAsync(WorkshopItemInfo item, CancellationToken ct = default)
    {
        if (_client is null)
            throw new InvalidOperationException("Not logged in");

        var options = _optionsMonitor.CurrentValue;
        var downloadDir = Path.Combine(options.DownloadPath, item.PublishedFileId.ToString());
        var archivePath = Path.Combine(options.OutputPath, $"{item.PublishedFileId}.7z");

        try
        {
            // Clean up any existing download directory
            if (Directory.Exists(downloadDir))
                Directory.Delete(downloadDir, true);

            Directory.CreateDirectory(downloadDir);

            _logger.LogInformation("Downloading workshop item {Id} ({Title})", item.PublishedFileId, item.Title);

            // Set download directory via config before download
            // The library downloads to depots/{depotId}/{version} by default
            // For workshop items, it goes to the workshop depot path
            await _client.DownloadPublishedFileAsync(item.AppId, item.PublishedFileId);

            // Find the downloaded content - it's in depots/{workshopDepotId}/{manifestId}
            var depotsDir = Path.Combine(Directory.GetCurrentDirectory(), "depots");
            if (!Directory.Exists(depotsDir))
            {
                _logger.LogError("Download directory not found after download");
                return false;
            }

            // Find the most recently modified depot directory (our download)
            var depotDirs = Directory.GetDirectories(depotsDir)
                .SelectMany(d => Directory.GetDirectories(d))
                .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
                .ToList();

            if (depotDirs.Count == 0)
            {
                _logger.LogError("No depot content found after download");
                return false;
            }

            var contentDir = depotDirs[0];
            _logger.LogDebug("Found content at {ContentDir}", contentDir);

            // Compress to output path
            await _compressionService.CompressDirectoryAsync(contentDir, archivePath, ct);

            // Mark as archived in database
            await _tracker.MarkArchivedAsync(item.PublishedFileId, item.TimeUpdated, ct);

            // Clean up the downloaded content
            try
            {
                Directory.Delete(contentDir, true);
                // Clean up empty parent depot directory if possible
                var parentDir = Path.GetDirectoryName(contentDir);
                if (parentDir != null && Directory.Exists(parentDir) &&
                    !Directory.EnumerateFileSystemEntries(parentDir).Any())
                    Directory.Delete(parentDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up download directory");
            }

            _logger.LogInformation("Successfully archived {Id} to {Path}", item.PublishedFileId, archivePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download/archive workshop item {Id}", item.PublishedFileId);
            await _tracker.RecordFailureAsync(item.PublishedFileId, item.AppId, ex.Message, ct);

            // Clean up on failure
            try
            {
                if (Directory.Exists(downloadDir))
                    Directory.Delete(downloadDir, true);
            }
            catch
            {
                /* ignore cleanup errors */
            }

            return false;
        }
    }
}

internal sealed class DaemonUserInterface : IUserInterface
{
    private readonly ILogger _logger;

    public DaemonUserInterface(ILogger logger)
    {
        _logger = logger;
    }

    public bool IsInputRedirected => true;
    public bool IsOutputRedirected => true;

    public void Write(string message)
    {
        _logger.LogDebug("{Message}", message);
    }

    public void Write(string format, params object[] args)
    {
        _logger.LogDebug(format, args);
    }

    public void WriteDebug(string category, string message)
    {
        _logger.LogTrace("[{Category}] {Message}", category, message);
    }

    public void WriteLine()
    {
    }

    public void WriteLine(string message)
    {
        _logger.LogDebug("{Message}", message);
    }

    public void WriteLine(string format, params object[] args)
    {
        _logger.LogDebug(format, args);
    }

    public void WriteError(string message)
    {
        _logger.LogWarning("{Message}", message);
    }

    public void WriteError(string format, params object[] args)
    {
        _logger.LogWarning(format, args);
    }

    public string ReadLine()
    {
        return string.Empty;
    }

    public string ReadPassword()
    {
        return string.Empty;
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        return default;
    }

    public void UpdateProgress(ulong downloaded, ulong total)
    {
    }

    public void DisplayQrCode(string challengeUrl)
    {
        _logger.LogInformation("QR Code URL: {Url}", challengeUrl);
    }
}
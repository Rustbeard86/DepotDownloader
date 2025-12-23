using DepotDownloader.Lib;
using Microsoft.Extensions.Options;

namespace GitHubArchiver.Daemon;

/// <summary>
///     Background service that archives Steam Workshop items to GitHub.
///     Supports runtime configuration changes via appsettings.json reload.
/// </summary>
public sealed class GitHubArchiverWorker : BackgroundService
{
    private readonly WorkshopDownloadService _downloadService;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<GitHubArchiverWorker> _logger;
    private readonly IOptionsMonitor<WorkshopOptions> _optionsMonitor;
    private readonly IWorkshopTracker _tracker;

    private uint _currentAppId;

    public GitHubArchiverWorker(
        IOptionsMonitor<WorkshopOptions> optionsMonitor,
        IWorkshopTracker tracker,
        WorkshopDownloadService downloadService,
        IHostApplicationLifetime lifetime,
        ILogger<GitHubArchiverWorker> logger)
    {
        _optionsMonitor = optionsMonitor;
        _tracker = tracker;
        _downloadService = downloadService;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _optionsMonitor.CurrentValue;
        _currentAppId = options.AppId;

        _logger.LogInformation("GitHub Archiver starting for AppId {AppId}", _currentAppId);
        _logger.LogInformation("Download path: {DownloadPath}", options.DownloadPath);
        _logger.LogInformation("Database path: {DatabasePath}", options.DatabasePath);

        // Initialize database
        await _tracker.InitializeAsync(stoppingToken);

        // Login to Steam
        if (!_downloadService.Login())
        {
            _logger.LogCritical("Failed to login to Steam. Shutting down.");
            _lifetime.StopApplication();
            return;
        }

        try
        {
            // Initial archive run
            await RunArchivePassAsync(true, stoppingToken);

            // Polling loop
            var pollInterval = TimeSpan.FromMinutes(options.PollIntervalMinutes);
            _logger.LogInformation("Initial archive complete. Polling every {Minutes} minutes",
                options.PollIntervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(pollInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                // Check for AppId changes
                var currentOptions = _optionsMonitor.CurrentValue;
                if (currentOptions.AppId != _currentAppId && currentOptions.AppId != 0)
                {
                    _logger.LogInformation("AppId changed from {OldAppId} to {NewAppId}", _currentAppId,
                        currentOptions.AppId);
                    _currentAppId = currentOptions.AppId;
                }

                // Update poll interval if changed
                pollInterval = TimeSpan.FromMinutes(currentOptions.PollIntervalMinutes);

                await RunArchivePassAsync(false, stoppingToken);
                await RetryFailedDownloadsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Shutdown requested");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in archive worker");
            throw;
        }
        finally
        {
            _downloadService.Logout();
        }
    }

    private async Task RunArchivePassAsync(bool isInitial, CancellationToken ct)
    {
        var options = _optionsMonitor.CurrentValue;

        if (_currentAppId == 0)
        {
            _logger.LogWarning("No AppId configured. Skipping archive pass.");
            return;
        }

        _logger.LogInformation("Starting {Type} archive pass for AppId {AppId}", isInitial ? "initial" : "update",
            _currentAppId);

        try
        {
            // Query all workshop items
            var workshopItems = await _downloadService.QueryWorkshopItemsAsync(_currentAppId, ct);

            // Get known items from database
            var knownItems = (await _tracker.GetAllItemsAsync(ct)).ToDictionary(i => i.PublishedFileId);

            var itemsToArchive = new List<WorkshopItemInfo>();

            foreach (var item in workshopItems)
            {
                // Update/insert item metadata in database
                var dbItem = new WorkshopItem(
                    item.PublishedFileId,
                    item.AppId,
                    item.Title,
                    item.TimeCreated,
                    item.TimeUpdated,
                    item.FileSize,
                    null,
                    null
                );

                if (knownItems.TryGetValue(item.PublishedFileId, out var existing))
                    // Preserve archived status
                    dbItem = dbItem with
                    {
                        ArchivedAt = existing.ArchivedAt,
                        ArchivedTimeUpdated = existing.ArchivedTimeUpdated
                    };

                await _tracker.UpsertItemAsync(dbItem, ct);

                // Determine if we need to archive this item
                var needsArchive = false;

                if (!knownItems.TryGetValue(item.PublishedFileId, out var known))
                {
                    // New item
                    needsArchive = true;
                    _logger.LogDebug("New item: {Id} ({Title})", item.PublishedFileId, item.Title);
                }
                else if (known.ArchivedTimeUpdated is null)
                {
                    // Never archived
                    needsArchive = true;
                    _logger.LogDebug("Never archived: {Id} ({Title})", item.PublishedFileId, item.Title);
                }
                else if (item.TimeUpdated > known.ArchivedTimeUpdated)
                {
                    // Updated since last archive
                    needsArchive = true;
                    _logger.LogDebug("Updated: {Id} ({Title}) - workshop: {New}, archived: {Old}",
                        item.PublishedFileId, item.Title, item.TimeUpdated, known.ArchivedTimeUpdated);
                }

                if (needsArchive)
                    itemsToArchive.Add(item);
            }

            _logger.LogInformation("Found {Total} items, {ToArchive} need archiving",
                workshopItems.Count, itemsToArchive.Count);

            // Download and archive items with rate limiting
            var successCount = 0;
            var failCount = 0;
            var delay = TimeSpan.FromSeconds(options.DelayBetweenDownloadsSeconds);

            foreach (var item in itemsToArchive)
            {
                if (ct.IsCancellationRequested)
                    break;

                var success = await _downloadService.DownloadAndArchiveItemAsync(item, ct);

                if (success)
                    successCount++;
                else
                    failCount++;

                // Rate limit
                if (!ct.IsCancellationRequested)
                    await Task.Delay(delay, ct);
            }

            _logger.LogInformation("Archive pass complete: {Success} succeeded, {Failed} failed",
                successCount, failCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during archive pass");
        }
    }

    private async Task RetryFailedDownloadsAsync(CancellationToken ct)
    {
        var options = _optionsMonitor.CurrentValue;
        var minAge = TimeSpan.FromMinutes(options.RetryBackoffMinutes);
        var failures = await _tracker.GetRetryableFailuresAsync(options.MaxRetryAttempts, minAge, ct);

        if (failures.Count == 0)
            return;

        _logger.LogInformation("Retrying {Count} failed downloads", failures.Count);

        // Get current workshop items to have the metadata
        var workshopItems = await _downloadService.QueryWorkshopItemsAsync(_currentAppId, ct);
        var itemLookup = workshopItems.ToDictionary(i => i.PublishedFileId);

        var delay = TimeSpan.FromSeconds(options.DelayBetweenDownloadsSeconds);

        foreach (var failure in failures)
        {
            if (ct.IsCancellationRequested)
                break;

            if (!itemLookup.TryGetValue(failure.PublishedFileId, out var item))
            {
                _logger.LogWarning("Failed item {Id} no longer exists in workshop, clearing failure",
                    failure.PublishedFileId);
                await _tracker.ClearFailureAsync(failure.PublishedFileId, ct);
                continue;
            }

            _logger.LogInformation("Retry attempt {Attempt} for {Id} ({Title})",
                failure.Attempts + 1, item.PublishedFileId, item.Title);

            await _downloadService.DownloadAndArchiveItemAsync(item, ct);

            if (!ct.IsCancellationRequested)
                await Task.Delay(delay, ct);
        }
    }
}
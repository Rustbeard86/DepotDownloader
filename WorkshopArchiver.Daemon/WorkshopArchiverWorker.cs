using DepotDownloader.Lib;
using Microsoft.Extensions.Options;

namespace WorkshopArchiver.Daemon;

public sealed class WorkshopArchiverWorker : BackgroundService
{
    private readonly ILogger<WorkshopArchiverWorker> _logger;
    private readonly WorkshopOptions _options;
    private readonly IWorkshopTracker _tracker;
    private readonly WorkshopDownloadService _downloadService;
    private readonly IHostApplicationLifetime _lifetime;

    public WorkshopArchiverWorker(
        IOptions<WorkshopOptions> options,
        IWorkshopTracker tracker,
        WorkshopDownloadService downloadService,
        IHostApplicationLifetime lifetime,
        ILogger<WorkshopArchiverWorker> logger)
    {
        _options = options.Value;
        _tracker = tracker;
        _downloadService = downloadService;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workshop Archiver starting for AppId {AppId}", _options.AppId);

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
            await RunArchivePassAsync(isInitial: true, stoppingToken);

            // Polling loop
            var pollInterval = TimeSpan.FromMinutes(_options.PollIntervalMinutes);
            _logger.LogInformation("Initial archive complete. Polling every {Minutes} minutes", _options.PollIntervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(pollInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                await RunArchivePassAsync(isInitial: false, stoppingToken);
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
        _logger.LogInformation("Starting {Type} archive pass", isInitial ? "initial" : "update");

        try
        {
            // Query all workshop items
            var workshopItems = await _downloadService.QueryWorkshopItemsAsync(ct);

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
                {
                    // Preserve archived status
                    dbItem = dbItem with
                    {
                        ArchivedAt = existing.ArchivedAt,
                        ArchivedTimeUpdated = existing.ArchivedTimeUpdated
                    };
                }

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
            var delay = TimeSpan.FromSeconds(_options.DelayBetweenDownloadsSeconds);

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
        var minAge = TimeSpan.FromMinutes(_options.RetryBackoffMinutes);
        var failures = await _tracker.GetRetryableFailuresAsync(_options.MaxRetryAttempts, minAge, ct);

        if (failures.Count == 0)
            return;

        _logger.LogInformation("Retrying {Count} failed downloads", failures.Count);

        // Get current workshop items to have the metadata
        var workshopItems = await _downloadService.QueryWorkshopItemsAsync(ct);
        var itemLookup = workshopItems.ToDictionary(i => i.PublishedFileId);

        var delay = TimeSpan.FromSeconds(_options.DelayBetweenDownloadsSeconds);

        foreach (var failure in failures)
        {
            if (ct.IsCancellationRequested)
                break;

            if (!itemLookup.TryGetValue(failure.PublishedFileId, out var item))
            {
                _logger.LogWarning("Failed item {Id} no longer exists in workshop, clearing failure", failure.PublishedFileId);
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

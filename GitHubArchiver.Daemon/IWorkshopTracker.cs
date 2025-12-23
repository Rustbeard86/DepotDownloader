namespace GitHubArchiver.Daemon;

/// <summary>
///     Service for tracking workshop items and download failures in persistent storage.
/// </summary>
public interface IWorkshopTracker : IAsyncDisposable
{
    /// <summary>
    ///     Initializes the database schema.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    ///     Gets a specific workshop item by ID.
    /// </summary>
    Task<WorkshopItem?> GetItemAsync(ulong publishedFileId, CancellationToken ct = default);

    /// <summary>
    ///     Gets all tracked workshop items.
    /// </summary>
    Task<IReadOnlyList<WorkshopItem>> GetAllItemsAsync(CancellationToken ct = default);

    /// <summary>
    ///     Inserts or updates a workshop item.
    /// </summary>
    Task UpsertItemAsync(WorkshopItem item, CancellationToken ct = default);

    /// <summary>
    ///     Marks a workshop item as successfully archived.
    /// </summary>
    Task MarkArchivedAsync(ulong publishedFileId, uint timeUpdated, CancellationToken ct = default);

    /// <summary>
    ///     Records a failed download attempt.
    /// </summary>
    Task RecordFailureAsync(ulong publishedFileId, uint appId, string error, CancellationToken ct = default);

    /// <summary>
    ///     Gets failed downloads eligible for retry.
    /// </summary>
    Task<IReadOnlyList<FailedDownload>> GetRetryableFailuresAsync(int maxAttempts, TimeSpan minAge,
        CancellationToken ct = default);

    /// <summary>
    ///     Clears failure record for a workshop item.
    /// </summary>
    Task ClearFailureAsync(ulong publishedFileId, CancellationToken ct = default);
}
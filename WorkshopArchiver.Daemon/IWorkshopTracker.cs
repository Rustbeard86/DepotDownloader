namespace WorkshopArchiver.Daemon;

public interface IWorkshopTracker : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<WorkshopItem?> GetItemAsync(ulong publishedFileId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkshopItem>> GetAllItemsAsync(CancellationToken ct = default);
    Task UpsertItemAsync(WorkshopItem item, CancellationToken ct = default);
    Task MarkArchivedAsync(ulong publishedFileId, uint timeUpdated, CancellationToken ct = default);
    Task RecordFailureAsync(ulong publishedFileId, uint appId, string error, CancellationToken ct = default);
    Task<IReadOnlyList<FailedDownload>> GetRetryableFailuresAsync(int maxAttempts, TimeSpan minAge, CancellationToken ct = default);
    Task ClearFailureAsync(ulong publishedFileId, CancellationToken ct = default);
}

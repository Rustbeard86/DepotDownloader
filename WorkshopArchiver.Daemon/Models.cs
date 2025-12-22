namespace WorkshopArchiver.Daemon;

public sealed record WorkshopItem(
    ulong PublishedFileId,
    uint AppId,
    string? Title,
    uint TimeCreated,
    uint TimeUpdated,
    ulong FileSize,
    DateTimeOffset? ArchivedAt,
    uint? ArchivedTimeUpdated
);

public sealed record FailedDownload(
    ulong PublishedFileId,
    uint AppId,
    int Attempts,
    string LastError,
    DateTimeOffset LastAttempt
);
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace WorkshopArchiver.Daemon;

public sealed class SqliteWorkshopTracker : IWorkshopTracker
{
    private readonly ILogger<SqliteWorkshopTracker> _logger;
    private readonly SqliteConnection _connection;

    public SqliteWorkshopTracker(IOptions<WorkshopOptions> options, ILogger<SqliteWorkshopTracker> logger)
    {
        _logger = logger;
        var dbPath = options.Value.DatabasePath;
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connection = new SqliteConnection($"Data Source={dbPath}");
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _connection.OpenAsync(ct);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS WorkshopItems (
                PublishedFileId INTEGER PRIMARY KEY,
                AppId INTEGER NOT NULL,
                Title TEXT,
                TimeCreated INTEGER NOT NULL,
                TimeUpdated INTEGER NOT NULL,
                FileSize INTEGER NOT NULL,
                ArchivedAt TEXT,
                ArchivedTimeUpdated INTEGER
            );

            CREATE TABLE IF NOT EXISTS FailedDownloads (
                PublishedFileId INTEGER PRIMARY KEY,
                AppId INTEGER NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 1,
                LastError TEXT NOT NULL,
                LastAttempt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_workshop_appid ON WorkshopItems(AppId);
            CREATE INDEX IF NOT EXISTS idx_failed_attempt ON FailedDownloads(LastAttempt);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Database initialized");
    }

    public async Task<WorkshopItem?> GetItemAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT PublishedFileId, AppId, Title, TimeCreated, TimeUpdated, FileSize, ArchivedAt, ArchivedTimeUpdated
            FROM WorkshopItems WHERE PublishedFileId = $id
            """;
        cmd.Parameters.AddWithValue("$id", (long)publishedFileId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadWorkshopItem(reader);
    }

    public async Task<IReadOnlyList<WorkshopItem>> GetAllItemsAsync(CancellationToken ct = default)
    {
        var results = new List<WorkshopItem>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT PublishedFileId, AppId, Title, TimeCreated, TimeUpdated, FileSize, ArchivedAt, ArchivedTimeUpdated
            FROM WorkshopItems ORDER BY PublishedFileId
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadWorkshopItem(reader));

        return results;
    }

    public async Task UpsertItemAsync(WorkshopItem item, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO WorkshopItems (PublishedFileId, AppId, Title, TimeCreated, TimeUpdated, FileSize, ArchivedAt, ArchivedTimeUpdated)
            VALUES ($id, $appId, $title, $created, $updated, $size, $archivedAt, $archivedUpdated)
            ON CONFLICT(PublishedFileId) DO UPDATE SET
                Title = excluded.Title,
                TimeCreated = excluded.TimeCreated,
                TimeUpdated = excluded.TimeUpdated,
                FileSize = excluded.FileSize
            """;
        cmd.Parameters.AddWithValue("$id", (long)item.PublishedFileId);
        cmd.Parameters.AddWithValue("$appId", (int)item.AppId);
        cmd.Parameters.AddWithValue("$title", item.Title ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$created", (int)item.TimeCreated);
        cmd.Parameters.AddWithValue("$updated", (int)item.TimeUpdated);
        cmd.Parameters.AddWithValue("$size", (long)item.FileSize);
        cmd.Parameters.AddWithValue("$archivedAt", item.ArchivedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$archivedUpdated", item.ArchivedTimeUpdated.HasValue ? (int)item.ArchivedTimeUpdated.Value : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkArchivedAsync(ulong publishedFileId, uint timeUpdated, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE WorkshopItems 
            SET ArchivedAt = $archivedAt, ArchivedTimeUpdated = $archivedUpdated
            WHERE PublishedFileId = $id
            """;
        cmd.Parameters.AddWithValue("$id", (long)publishedFileId);
        cmd.Parameters.AddWithValue("$archivedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$archivedUpdated", (int)timeUpdated);

        await cmd.ExecuteNonQueryAsync(ct);
        await ClearFailureAsync(publishedFileId, ct);

        _logger.LogDebug("Marked {PublishedFileId} as archived", publishedFileId);
    }

    public async Task RecordFailureAsync(ulong publishedFileId, uint appId, string error, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FailedDownloads (PublishedFileId, AppId, Attempts, LastError, LastAttempt)
            VALUES ($id, $appId, 1, $error, $lastAttempt)
            ON CONFLICT(PublishedFileId) DO UPDATE SET
                Attempts = Attempts + 1,
                LastError = excluded.LastError,
                LastAttempt = excluded.LastAttempt
            """;
        cmd.Parameters.AddWithValue("$id", (long)publishedFileId);
        cmd.Parameters.AddWithValue("$appId", (int)appId);
        cmd.Parameters.AddWithValue("$error", error);
        cmd.Parameters.AddWithValue("$lastAttempt", DateTimeOffset.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogWarning("Recorded failure for {PublishedFileId}: {Error}", publishedFileId, error);
    }

    public async Task<IReadOnlyList<FailedDownload>> GetRetryableFailuresAsync(int maxAttempts, TimeSpan minAge, CancellationToken ct = default)
    {
        var results = new List<FailedDownload>();
        var cutoff = DateTimeOffset.UtcNow.Subtract(minAge);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT PublishedFileId, AppId, Attempts, LastError, LastAttempt 
            FROM FailedDownloads 
            WHERE Attempts < $maxAttempts AND LastAttempt < $cutoff
            ORDER BY LastAttempt ASC
            """;
        cmd.Parameters.AddWithValue("$maxAttempts", maxAttempts);
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new FailedDownload(
                (ulong)reader.GetInt64(0),
                (uint)reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4))
            ));
        }

        return results;
    }

    public async Task ClearFailureAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM FailedDownloads WHERE PublishedFileId = $id";
        cmd.Parameters.AddWithValue("$id", (long)publishedFileId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private static WorkshopItem ReadWorkshopItem(SqliteDataReader reader)
    {
        return new WorkshopItem(
            (ulong)reader.GetInt64(0),
            (uint)reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            (uint)reader.GetInt32(3),
            (uint)reader.GetInt32(4),
            (ulong)reader.GetInt64(5),
            reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            reader.IsDBNull(7) ? null : (uint)reader.GetInt32(7)
        );
    }
}

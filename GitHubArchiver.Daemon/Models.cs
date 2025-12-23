using Newtonsoft.Json;

namespace GitHubArchiver.Daemon;

/// <summary>
///     Metadata for a workshop item stored in the rooms.json manifest.
/// </summary>
public sealed class RemoteRoomMeta
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;

    [JsonProperty("name")] public string Name { get; set; } = string.Empty;

    [JsonProperty("imageUrl")] public string ImageUrl { get; set; } = string.Empty;

    [JsonProperty("downloadUrl")] public string DownloadUrl { get; set; } = string.Empty;

    [JsonProperty("timestamp")] public long Timestamp { get; set; }
}

/// <summary>
///     Steam API response wrapper.
/// </summary>
public sealed class SteamApiResponse
{
    [JsonProperty("response")] public SteamResponse? Response { get; set; }
}

/// <summary>
///     Steam API response containing published file details.
/// </summary>
public sealed class SteamResponse
{
    [JsonProperty("publishedfiledetails")] public List<SteamDetails>? Publishedfiledetails { get; set; }
}

/// <summary>
///     Details of a Steam Workshop item from the Steam API.
/// </summary>
public sealed class SteamDetails
{
    [JsonProperty("publishedfileid")] public string Publishedfileid { get; set; } = string.Empty;

    [JsonProperty("title")] public string Title { get; set; } = string.Empty;

    [JsonProperty("preview_url")] public string Preview_url { get; set; } = string.Empty;

    [JsonProperty("time_updated")] public long Time_updated { get; set; }
}

/// <summary>
///     Internal tracking record for a workshop item in the database.
/// </summary>
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

/// <summary>
///     Record of a failed download attempt for retry tracking.
/// </summary>
public sealed record FailedDownload(
    ulong PublishedFileId,
    uint AppId,
    int Attempts,
    string LastError,
    DateTimeOffset LastAttempt
);
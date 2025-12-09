using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DepotDownloader.Lib;

/// <summary>
///     Tracks the state of a download for resume capability.
/// </summary>
public sealed class DownloadState
{
    /// <summary>
    ///     The Steam AppID being downloaded.
    /// </summary>
    [JsonPropertyName("appId")]
    public uint AppId { get; set; }

    /// <summary>
    ///     The branch being downloaded from.
    /// </summary>
    [JsonPropertyName("branch")]
    public string Branch { get; set; }

    /// <summary>
    ///     When the download was started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    ///     When the state was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>
    ///     State for each depot being downloaded.
    /// </summary>
    [JsonPropertyName("depots")]
    public Dictionary<uint, DepotDownloadState> Depots { get; set; } = [];

    /// <summary>
    ///     Total bytes downloaded across all depots.
    /// </summary>
    [JsonPropertyName("totalBytesDownloaded")]
    public ulong TotalBytesDownloaded { get; set; }

    /// <summary>
    ///     Total bytes to download across all depots.
    /// </summary>
    [JsonPropertyName("totalBytes")]
    public ulong TotalBytes { get; set; }
}

/// <summary>
///     Tracks the state of a single depot download.
/// </summary>
public sealed class DepotDownloadState
{
    /// <summary>
    ///     The depot ID.
    /// </summary>
    [JsonPropertyName("depotId")]
    public uint DepotId { get; set; }

    /// <summary>
    ///     The manifest ID being downloaded.
    /// </summary>
    [JsonPropertyName("manifestId")]
    public ulong ManifestId { get; set; }

    /// <summary>
    ///     Set of completed chunk IDs (hex strings).
    /// </summary>
    [JsonPropertyName("completedChunks")]
    public HashSet<string> CompletedChunks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Set of completed file names.
    /// </summary>
    [JsonPropertyName("completedFiles")]
    public HashSet<string> CompletedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Bytes downloaded for this depot.
    /// </summary>
    [JsonPropertyName("bytesDownloaded")]
    public ulong BytesDownloaded { get; set; }

    /// <summary>
    ///     Total bytes for this depot.
    /// </summary>
    [JsonPropertyName("totalBytes")]
    public ulong TotalBytes { get; set; }

    /// <summary>
    ///     Whether this depot download is complete.
    /// </summary>
    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }
}

/// <summary>
///     JSON serialization context for download state.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DownloadState))]
internal sealed partial class DownloadStateContext : JsonSerializerContext;
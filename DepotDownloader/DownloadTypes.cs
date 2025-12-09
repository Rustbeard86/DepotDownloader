using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SteamKit2;

namespace DepotDownloader.Lib;

/// <summary>
///     Information about a depot to be downloaded.
/// </summary>
internal sealed record DepotDownloadInfo(
    uint DepotId,
    uint AppId,
    ulong ManifestId,
    string Branch,
    string InstallDir,
    byte[] DepotKey);

/// <summary>
///     Tracks matching chunks between old and new manifests for delta updates.
/// </summary>
internal sealed record ChunkMatch(
    DepotManifest.ChunkData OldChunk,
    DepotManifest.ChunkData NewChunk);

/// <summary>
///     Contains all data needed to download files for a single depot.
/// </summary>
internal sealed class DepotFilesData
{
    public required HashSet<string> AllFileNames { get; init; }
    public required DepotDownloadCounter DepotCounter { get; init; }
    public required DepotDownloadInfo DepotDownloadInfo { get; init; }
    public required List<DepotManifest.FileData> FilteredFiles { get; init; }
    public required DepotManifest Manifest { get; init; }
    public DepotManifest PreviousManifest { get; init; }
    public required string StagingDirectoryPath { get; init; }
}

/// <summary>
///     Manages file stream state for concurrent chunk downloads.
/// </summary>
internal sealed class FileStreamData : IDisposable
{
    public readonly SemaphoreSlim FileLock = new(1);
    public int ChunksToDownload;
    public FileStream FileStream;

    public void Dispose()
    {
        FileStream?.Dispose();
        FileLock?.Dispose();
    }
}

/// <summary>
///     Tracks global download progress across all depots.
/// </summary>
internal sealed class GlobalDownloadCounter
{
    /// <summary>
    ///     Remaining bytes to download (decremented as files are validated/skipped).
    /// </summary>
    public ulong CompleteDownloadSize;

    public ulong TotalBytesCompressed;

    /// <summary>
    ///     Total bytes downloaded so far across all depots.
    /// </summary>
    public ulong TotalBytesDownloaded;

    public ulong TotalBytesUncompressed;

    /// <summary>
    ///     Total size of all files to download (set once before downloading begins).
    /// </summary>
    public ulong TotalDownloadSize;
}

/// <summary>
///     Tracks download progress for a single depot.
/// </summary>
internal sealed class DepotDownloadCounter
{
    public ulong CompleteDownloadSize;
    public ulong DepotBytesCompressed;
    public ulong DepotBytesUncompressed;
    public ulong SizeDownloaded;
}

/// <summary>
///     Comparer for byte arrays (used for chunk IDs which are SHA-1 hashes).
/// </summary>
internal sealed class ChunkIdComparer : IEqualityComparer<byte[]>
{
    public bool Equals(byte[] x, byte[] y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.SequenceEqual(y);
    }

    public int GetHashCode(byte[] obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        // ChunkID is SHA-1, so we can just use the first 4 bytes
        return BitConverter.ToInt32(obj, 0);
    }
}
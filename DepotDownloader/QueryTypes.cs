using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace DepotDownloader.Lib;

/// <summary>
///     Information about a Steam application.
/// </summary>
/// <param name="AppId">The Steam application ID.</param>
/// <param name="Name">The application name.</param>
/// <param name="Type">The application type (e.g., "game", "dlc", "tool").</param>
public record AppInfo(uint AppId, string Name, string Type);

/// <summary>
///     Information about a Steam depot.
/// </summary>
/// <param name="DepotId">The depot ID.</param>
/// <param name="Name">The depot name, if available.</param>
/// <param name="Os">Target operating system (windows, linux, macos), or null if platform-independent.</param>
/// <param name="Architecture">Target architecture (32, 64), or null if architecture-independent.</param>
/// <param name="Language">Target language, or null if language-independent.</param>
/// <param name="MaxSize">Maximum size in bytes, if known.</param>
/// <param name="IsSharedInstall">Whether this depot uses shared install.</param>
public record DepotInfo(
    uint DepotId,
    string Name,
    string Os,
    string Architecture,
    string Language,
    ulong? MaxSize,
    bool IsSharedInstall);

/// <summary>
///     Information about a Steam app branch.
/// </summary>
/// <param name="Name">The branch name (e.g., "public", "beta").</param>
/// <param name="BuildId">The current build ID for this branch.</param>
/// <param name="TimeUpdated">When the branch was last updated, if known.</param>
/// <param name="IsPasswordProtected">Whether a password is required to access this branch.</param>
/// <param name="Description">Optional branch description.</param>
public record BranchInfo(
    string Name,
    uint BuildId,
    DateTime? TimeUpdated,
    bool IsPasswordProtected,
    string Description);

/// <summary>
///     Information about a file that would be downloaded.
/// </summary>
/// <param name="FileName">The file path relative to the install directory.</param>
/// <param name="Size">The file size in bytes.</param>
/// <param name="Hash">The SHA-1 hash of the file.</param>
public record FileDownloadInfo(string FileName, ulong Size, string Hash);

/// <summary>
///     A plan of what would be downloaded without actually downloading.
/// </summary>
/// <param name="AppId">The Steam application ID.</param>
/// <param name="AppName">The application name.</param>
/// <param name="Depots">Information about each depot that would be downloaded.</param>
/// <param name="TotalDownloadSize">Total size to download in bytes.</param>
/// <param name="TotalFileCount">Total number of files to download.</param>
public record DownloadPlan(
    uint AppId,
    string AppName,
    IReadOnlyList<DepotDownloadPlan> Depots,
    ulong TotalDownloadSize,
    int TotalFileCount);

/// <summary>
///     Download plan for a single depot.
/// </summary>
/// <param name="DepotId">The depot ID.</param>
/// <param name="ManifestId">The manifest ID to download.</param>
/// <param name="Files">List of files in this depot.</param>
/// <param name="TotalSize">Total size of files in this depot.</param>
public record DepotDownloadPlan(
    uint DepotId,
    ulong ManifestId,
    IReadOnlyList<FileDownloadInfo> Files,
    ulong TotalSize);

/// <summary>
///     Result of a disk space check operation.
/// </summary>
/// <param name="HasSufficientSpace">Whether there is enough space for the download.</param>
/// <param name="RequiredBytes">The number of bytes required for the download.</param>
/// <param name="AvailableBytes">The number of bytes available on the target drive.</param>
/// <param name="TargetDrive">The drive letter or mount point being checked.</param>
public record DiskSpaceCheckResult(
    bool HasSufficientSpace,
    ulong RequiredBytes,
    ulong AvailableBytes,
    string TargetDrive);

/// <summary>
///     Event arguments for download progress updates.
/// </summary>
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members",
    Justification = "Public API for library consumers")]
public class DownloadProgressEventArgs : EventArgs
{
    /// <summary>
    ///     Total bytes downloaded so far.
    /// </summary>
    public ulong BytesDownloaded { get; init; }

    /// <summary>
    ///     Total bytes to download.
    /// </summary>
    public ulong TotalBytes { get; init; }

    /// <summary>
    ///     Current file being downloaded, if known.
    /// </summary>
    public string CurrentFile { get; init; }

    /// <summary>
    ///     Number of files completed.
    /// </summary>
    public int FilesCompleted { get; init; }

    /// <summary>
    ///     Total number of files to download.
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    ///     Current download speed in bytes per second.
    /// </summary>
    public double SpeedBytesPerSecond { get; init; }

    /// <summary>
    ///     Estimated time remaining for the download.
    /// </summary>
    public TimeSpan EstimatedTimeRemaining { get; init; }

    /// <summary>
    ///     Progress percentage (0-100).
    /// </summary>
    public double ProgressPercent => TotalBytes > 0 ? BytesDownloaded / (double)TotalBytes * 100.0 : 0;
}

/// <summary>
///     Exception thrown when there is insufficient disk space for a download.
/// </summary>
public class InsufficientDiskSpaceException(ulong requiredBytes, ulong availableBytes, string targetDrive)
    : Exception(FormatMessage(requiredBytes, availableBytes, targetDrive))
{
    /// <summary>
    ///     The number of bytes required for the download.
    /// </summary>
    public ulong RequiredBytes { get; } = requiredBytes;

    /// <summary>
    ///     The number of bytes available on the target drive.
    /// </summary>
    public ulong AvailableBytes { get; } = availableBytes;

    /// <summary>
    ///     The drive or mount point that lacks space.
    /// </summary>
    public string TargetDrive { get; } = targetDrive;

    /// <summary>
    ///     The number of additional bytes needed.
    /// </summary>
    public ulong ShortfallBytes => RequiredBytes - AvailableBytes;

    private static string FormatMessage(ulong requiredBytes, ulong availableBytes, string targetDrive)
    {
        var required = FormatSize(requiredBytes);
        var available = FormatSize(availableBytes);
        var shortfall = FormatSize(requiredBytes - availableBytes);

        return
            $"Insufficient disk space on {targetDrive}. Required: {required}, Available: {available}, Need additional: {shortfall}";
    }

    private static string FormatSize(ulong bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

/// <summary>
///     Result of downloading a single depot.
/// </summary>
/// <param name="DepotId">The depot ID.</param>
/// <param name="ManifestId">The manifest ID that was downloaded.</param>
/// <param name="Success">Whether the depot download succeeded.</param>
/// <param name="ErrorMessage">Error message if the download failed, null if successful.</param>
/// <param name="BytesDownloaded">Number of bytes downloaded from this depot.</param>
/// <param name="FilesDownloaded">Number of files downloaded from this depot.</param>
public record DepotDownloadResult(
    uint DepotId,
    ulong ManifestId,
    bool Success,
    string ErrorMessage,
    ulong BytesDownloaded,
    int FilesDownloaded)
{
    /// <summary>
    ///     Creates a successful depot download result.
    /// </summary>
    public static DepotDownloadResult Succeeded(uint depotId, ulong manifestId, ulong bytesDownloaded,
        int filesDownloaded)
    {
        return new DepotDownloadResult(depotId, manifestId, true, null, bytesDownloaded, filesDownloaded);
    }

    /// <summary>
    ///     Creates a failed depot download result.
    /// </summary>
    public static DepotDownloadResult Failed(uint depotId, ulong manifestId, string errorMessage)
    {
        return new DepotDownloadResult(depotId, manifestId, false, errorMessage, 0, 0);
    }

    /// <summary>
    ///     Creates a skipped depot download result (e.g., no access, no manifest).
    /// </summary>
    public static DepotDownloadResult Skipped(uint depotId, string reason)
    {
        return new DepotDownloadResult(depotId, 0, false, reason, 0, 0);
    }
}

/// <summary>
///     Result of a complete download operation, including all depot results.
/// </summary>
public sealed class DownloadResult
{
    /// <summary>
    ///     The Steam application ID.
    /// </summary>
    public uint AppId { get; init; }

    /// <summary>
    ///     Results for each depot that was processed.
    /// </summary>
    public IReadOnlyList<DepotDownloadResult> DepotResults { get; init; } = [];

    /// <summary>
    ///     Total bytes downloaded across all depots.
    /// </summary>
    public ulong TotalBytesDownloaded { get; init; }

    /// <summary>
    ///     Total bytes in compressed form (network transfer).
    /// </summary>
    public ulong TotalBytesCompressed { get; init; }

    /// <summary>
    ///     Total files downloaded across all depots.
    /// </summary>
    public int TotalFilesDownloaded { get; init; }

    /// <summary>
    ///     Number of depots that downloaded successfully.
    /// </summary>
    public int SuccessfulDepots => DepotResults.Count(r => r.Success);

    /// <summary>
    ///     Number of depots that failed to download.
    /// </summary>
    public int FailedDepots => DepotResults.Count(r => !r.Success);

    /// <summary>
    ///     Whether all depots downloaded successfully.
    /// </summary>
    public bool AllSucceeded => DepotResults.All(r => r.Success);

    /// <summary>
    ///     Whether the download partially succeeded (some depots succeeded, some failed).
    /// </summary>
    public bool PartialSuccess => SuccessfulDepots > 0 && FailedDepots > 0;

    /// <summary>
    ///     Whether the download completely failed (no depots succeeded).
    /// </summary>
    public bool AllFailed => DepotResults.Count > 0 && SuccessfulDepots == 0;

    /// <summary>
    ///     Gets the failed depot results.
    /// </summary>
    public IEnumerable<DepotDownloadResult> Failures => DepotResults.Where(r => !r.Success);

    /// <summary>
    ///     Gets the successful depot results.
    /// </summary>
    public IEnumerable<DepotDownloadResult> Successes => DepotResults.Where(r => r.Success);
}
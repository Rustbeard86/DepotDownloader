using System;
using System.Collections.Generic;

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

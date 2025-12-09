using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace DepotDownloader.Lib;

/// <summary>
///     Configuration options for downloading depot content.
/// </summary>
public sealed class DepotDownloadOptions
{
    /// <summary>
    ///     The Steam AppID to download.
    /// </summary>
    public uint AppId { get; set; } = ContentDownloader.InvalidAppId;

    /// <summary>
    ///     List of depot IDs and optional manifest IDs to download.
    ///     If manifestId is InvalidManifestId, the latest manifest for the branch will be used.
    /// </summary>
    public List<(uint depotId, ulong manifestId)> DepotManifestIds { get; set; } = [];

    /// <summary>
    ///     Branch name to download from (e.g., "public", "beta").
    /// </summary>
    public string Branch { get; set; } = ContentDownloader.DefaultBranch;

    /// <summary>
    ///     Password for password-protected branches.
    /// </summary>
    public string BranchPassword { get; set; }

    /// <summary>
    ///     Operating system filter (windows, macos, linux). Null uses current OS.
    /// </summary>
    public string Os { get; set; }

    /// <summary>
    ///     Architecture filter (32 or 64). Null uses current architecture.
    /// </summary>
    public string Architecture { get; set; }

    /// <summary>
    ///     Language filter. Null defaults to "english".
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    ///     Include low violence depots.
    /// </summary>
    public bool LowViolence { get; set; }

    /// <summary>
    ///     Directory where files will be downloaded. Null uses default depot structure.
    /// </summary>
    public string InstallDirectory { get; set; }

    /// <summary>
    ///     Specific files to download (by exact path match).
    /// </summary>
    public HashSet<string> FilesToDownload { get; set; }

    /// <summary>
    ///     Regular expressions for files to download.
    /// </summary>
    public List<Regex> FilesToDownloadRegex { get; set; }

    /// <summary>
    ///     Verify all existing files even if they match the manifest.
    /// </summary>
    public bool VerifyAll { get; set; }

    /// <summary>
    ///     Download manifest metadata only, don't download actual files.
    /// </summary>
    public bool DownloadManifestOnly { get; set; }

    /// <summary>
    ///     Maximum number of concurrent chunk downloads.
    /// </summary>
    public int MaxDownloads { get; set; } = 8;

    /// <summary>
    ///     Steam Cell ID override.
    /// </summary>
    public int CellId { get; set; }

    /// <summary>
    ///     Steam Login ID for running multiple instances.
    /// </summary>
    public uint? LoginId { get; set; }

    /// <summary>
    ///     Download depots for all platforms.
    /// </summary>
    public bool DownloadAllPlatforms { get; set; }

    /// <summary>
    ///     Download depots for all architectures.
    /// </summary>
    public bool DownloadAllArchs { get; set; }

    /// <summary>
    ///     Download depots for all languages.
    /// </summary>
    public bool DownloadAllLanguages { get; set; }

    /// <summary>
    ///     Verify disk space before downloading. Default is true.
    /// </summary>
    public bool VerifyDiskSpace { get; set; } = true;

    /// <summary>
    ///     Cancellation token for graceful cancellation from GUI apps or services.
    ///     If not provided, an internal token will be created.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}
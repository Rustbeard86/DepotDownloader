using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DepotDownloader.Lib;

public class DownloadConfig
{
    // Authentication settings
    public bool RememberPassword { get; set; }
    public uint? LoginId { get; set; }
    public bool UseQrCode { get; set; }

    // App/Depot selection
    public bool SkipAppConfirmation { get; set; }
    public string BetaPassword { get; set; }

    // Platform and architecture filtering
    public int CellId { get; set; }
    public bool DownloadAllPlatforms { get; set; }
    public bool DownloadAllArchs { get; set; }
    public bool DownloadAllLanguages { get; set; }

    // File filtering
    public bool UsingFileList { get; set; }
    public HashSet<string> FilesToDownload { get; set; }
    public List<Regex> FilesToDownloadRegex { get; set; }

    // Download behavior
    public string InstallDirectory { get; set; }
    public bool DownloadManifestOnly { get; set; }
    public bool VerifyAll { get; set; }
    public int MaxDownloads { get; set; }

    // Retry and throttling
    public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;
    public long? MaxBytesPerSecond { get; set; }

    // Resume support
    public bool Resume { get; set; }

    // Error handling
    /// <summary>
    ///     When true, stops downloading immediately on first depot failure.
    ///     When false (default), continues downloading remaining depots and reports failures at the end.
    /// </summary>
    public bool FailFast { get; set; }
}
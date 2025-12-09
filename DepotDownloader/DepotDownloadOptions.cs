using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Threading;

namespace DepotDownloader.Lib;

/// <summary>
///     Configuration options for downloading depot content.
/// </summary>
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global",
    Justification = "Properties are set incrementally by CLI and other consumers")]
public sealed class DepotDownloadOptions
{
    /// <summary>
    ///     The Steam AppID to download.
    /// </summary>
    public uint AppId { get; set; } = SteamConstants.InvalidAppId;

    /// <summary>
    ///     List of depot IDs and optional manifest IDs to download.
    ///     If manifestId is InvalidManifestId, the latest manifest for the branch will be used.
    /// </summary>
    public List<(uint depotId, ulong manifestId)> DepotManifestIds { get; set; } = [];

    /// <summary>
    ///     Branch name to download from (e.g., "public", "beta").
    /// </summary>
    public string Branch { get; set; } = SteamConstants.DefaultBranch;

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
    ///     Retry policy for failed chunk downloads.
    ///     Default uses exponential backoff with 5 retries.
    /// </summary>
    public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;

    /// <summary>
    ///     Maximum download speed in bytes per second.
    ///     Null or 0 means unlimited. Default is unlimited.
    /// </summary>
    public long? MaxBytesPerSecond { get; set; }

    /// <summary>
    ///     Enable resume support to continue interrupted downloads.
    ///     When enabled, download state is tracked and can be resumed.
    ///     Default is false.
    /// </summary>
    public bool Resume { get; set; }

    /// <summary>
    ///     Stop downloading immediately when any depot fails.
    ///     When false (default), continues downloading remaining depots and reports failures at the end.
    ///     When true, throws an exception on the first depot failure.
    /// </summary>
    public bool FailFast { get; set; }

    /// <summary>
    ///     Cancellation token for graceful cancellation of download operations.
    ///     Allows GUI applications and services to cancel downloads.
    /// </summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>
    ///     Validates the options and throws if invalid.
    /// </summary>
    /// <exception cref="ArgumentException">When validation fails.</exception>
    public void Validate()
    {
        // Required field validation
        if (AppId == SteamConstants.InvalidAppId)
            throw new ArgumentException("AppId must be specified.");

        // Mutually exclusive options validation
        if (DownloadAllPlatforms && !string.IsNullOrEmpty(Os))
            throw new ArgumentException("Cannot specify both DownloadAllPlatforms and Os. Choose one.");

        if (DownloadAllArchs && !string.IsNullOrEmpty(Architecture))
            throw new ArgumentException("Cannot specify both DownloadAllArchs and Architecture. Choose one.");

        if (DownloadAllLanguages && !string.IsNullOrEmpty(Language))
            throw new ArgumentException("Cannot specify both DownloadAllLanguages and Language. Choose one.");

        // Branch password requires branch
        if (!string.IsNullOrEmpty(BranchPassword) && string.IsNullOrEmpty(Branch))
            throw new ArgumentException("BranchPassword requires a Branch to be specified.");

        // Resume requires install directory
        if (Resume && string.IsNullOrWhiteSpace(InstallDirectory))
            throw new ArgumentException("Resume requires an InstallDirectory.");

        // Range validation
        if (MaxDownloads < 1 || MaxDownloads > 64)
            throw new ArgumentOutOfRangeException(nameof(MaxDownloads), "MaxDownloads must be between 1 and 64.");

        if (MaxBytesPerSecond is < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxBytesPerSecond), "MaxBytesPerSecond cannot be negative.");

        // OS validation
        if (!string.IsNullOrEmpty(Os) && Os is not ("windows" or "linux" or "macos"))
            throw new ArgumentException("Os must be 'windows', 'linux', or 'macos'.", nameof(Os));

        // Architecture validation
        if (!string.IsNullOrEmpty(Architecture) && Architecture is not ("32" or "64"))
            throw new ArgumentException("Architecture must be '32' or '64'.", nameof(Architecture));
    }

    /// <summary>
    ///     Validates the options and returns whether they are valid.
    /// </summary>
    /// <param name="errorMessage">The validation error message, if any.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public bool TryValidate(out string errorMessage)
    {
        try
        {
            Validate();
            errorMessage = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
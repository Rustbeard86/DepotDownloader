using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Threading;

namespace DepotDownloader.Lib;

/// <summary>
///     Fluent builder for creating <see cref="DepotDownloadOptions" /> instances.
/// </summary>
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members",
    Justification = "Public API for library consumers - methods are used externally")]
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Builder pattern requires instance methods for fluent chaining")]
public sealed class DepotDownloadOptionsBuilder
{
    private readonly List<(uint depotId, ulong manifestId)> _depotManifestIds = [];
    private readonly List<Regex> _fileRegexPatterns = [];
    private readonly HashSet<string> _filesToDownload = new(StringComparer.OrdinalIgnoreCase);

    private uint _appId = SteamConstants.InvalidAppId;
    private string _architecture;
    private string _branch = SteamConstants.DefaultBranch;
    private string _branchPassword;
    private CancellationToken _cancellationToken = CancellationToken.None;
    private int _cellId;
    private bool _downloadAllArchs;
    private bool _downloadAllLanguages;
    private bool _downloadAllPlatforms;
    private bool _downloadManifestOnly;
    private string _installDirectory;
    private string _language;
    private uint? _loginId;
    private bool _lowViolence;
    private int _maxDownloads = 8;
    private string _os;
    private bool _verifyAll;
    private bool _verifyDiskSpace = true;
    private RetryPolicy _retryPolicy = RetryPolicy.Default;
    private long? _maxBytesPerSecond;
    private bool _resume;

    /// <summary>
    ///     Sets the Steam AppID to download.
    /// </summary>
    /// <param name="appId">The Steam application ID.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder ForApp(uint appId)
    {
        _appId = appId;
        return this;
    }

    /// <summary>
    ///     Adds a depot to download with an optional specific manifest.
    /// </summary>
    /// <param name="depotId">The depot ID.</param>
    /// <param name="manifestId">Optional manifest ID. Use 0 or <see cref="SteamConstants.InvalidManifestId" /> for latest.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithDepot(uint depotId, ulong manifestId = SteamConstants.InvalidManifestId)
    {
        _depotManifestIds.Add((depotId, manifestId));
        return this;
    }

    /// <summary>
    ///     Sets the branch to download from.
    /// </summary>
    /// <param name="branch">Branch name (e.g., "public", "beta").</param>
    /// <param name="password">Optional password for protected branches.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder FromBranch(string branch, string password = null)
    {
        _branch = branch ?? SteamConstants.DefaultBranch;
        _branchPassword = password;
        return this;
    }

    /// <summary>
    ///     Sets the installation directory.
    /// </summary>
    /// <param name="directory">Path to install files.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder ToDirectory(string directory)
    {
        _installDirectory = directory;
        return this;
    }

    /// <summary>
    ///     Sets the target operating system.
    /// </summary>
    /// <param name="os">Operating system: "windows", "linux", or "macos".</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder ForOs(string os)
    {
        _os = os;
        _downloadAllPlatforms = false;
        return this;
    }

    /// <summary>
    ///     Sets the target architecture.
    /// </summary>
    /// <param name="architecture">Architecture: "32" or "64".</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder ForArchitecture(string architecture)
    {
        _architecture = architecture;
        _downloadAllArchs = false;
        return this;
    }

    /// <summary>
    ///     Sets the target language.
    /// </summary>
    /// <param name="language">Language code (e.g., "english", "german").</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder ForLanguage(string language)
    {
        _language = language;
        _downloadAllLanguages = false;
        return this;
    }

    /// <summary>
    ///     Configures to download all platform-specific depots.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder ForAllPlatforms()
    {
        _downloadAllPlatforms = true;
        _os = null;
        return this;
    }

    /// <summary>
    ///     Configures to download all architecture-specific depots.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder ForAllArchitectures()
    {
        _downloadAllArchs = true;
        _architecture = null;
        return this;
    }

    /// <summary>
    ///     Configures to download all language-specific depots.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder ForAllLanguages()
    {
        _downloadAllLanguages = true;
        _language = null;
        return this;
    }

    /// <summary>
    ///     Includes low-violence depots.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder IncludeLowViolence()
    {
        _lowViolence = true;
        return this;
    }

    /// <summary>
    ///     Adds a specific file path to download.
    /// </summary>
    /// <param name="filePath">Relative file path within the depot.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder IncludeFile(string filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
            _filesToDownload.Add(filePath.Replace('\\', '/'));
        return this;
    }

    /// <summary>
    ///     Adds multiple file paths to download.
    /// </summary>
    /// <param name="filePaths">Relative file paths within the depot.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder IncludeFiles(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
            IncludeFile(path);
        return this;
    }

    /// <summary>
    ///     Adds a regex pattern for matching files to download.
    /// </summary>
    /// <param name="pattern">Regular expression pattern.</param>
    /// <param name="options">Regex options (default: Compiled | IgnoreCase).</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder IncludeFilesMatching(string pattern,
        RegexOptions options = RegexOptions.Compiled | RegexOptions.IgnoreCase)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
            _fileRegexPatterns.Add(new Regex(pattern, options));
        return this;
    }

    /// <summary>
    ///     Adds a pre-compiled regex for matching files to download.
    /// </summary>
    /// <param name="regex">Compiled regular expression.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder IncludeFilesMatching(Regex regex)
    {
        if (regex is not null)
            _fileRegexPatterns.Add(regex);
        return this;
    }

    /// <summary>
    ///     Sets the maximum number of concurrent chunk downloads.
    /// </summary>
    /// <param name="maxDownloads">Maximum concurrent downloads (1-64, default: 8).</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithMaxConcurrency(int maxDownloads)
    {
        _maxDownloads = maxDownloads switch
        {
            < 1 => 1,
            > 64 => 64,
            _ => maxDownloads
        };
        return this;
    }

    /// <summary>
    ///     Enables verification of all existing files.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithVerification()
    {
        _verifyAll = true;
        return this;
    }

    /// <summary>
    ///     Configures to download manifest metadata only.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder ManifestOnly()
    {
        _downloadManifestOnly = true;
        return this;
    }

    /// <summary>
    ///     Sets the Steam Cell ID override.
    /// </summary>
    /// <param name="cellId">Cell ID for content server selection.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithCellId(int cellId)
    {
        _cellId = cellId;
        return this;
    }

    /// <summary>
    ///     Sets the Login ID for running multiple instances.
    /// </summary>
    /// <param name="loginId">Unique login identifier.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithLoginId(uint loginId)
    {
        _loginId = loginId;
        return this;
    }

    /// <summary>
    ///     Configures whether to verify disk space before downloading.
    /// </summary>
    /// <param name="verify">True to verify disk space (default), false to skip.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder VerifyDiskSpace(bool verify = true)
    {
        _verifyDiskSpace = verify;
        return this;
    }

    /// <summary>
    ///     Sets the retry policy for failed chunk downloads.
    /// </summary>
    /// <param name="policy">The retry policy to use.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithRetryPolicy(RetryPolicy policy)
    {
        _retryPolicy = policy ?? RetryPolicy.Default;
        return this;
    }

    /// <summary>
    ///     Configures retry behavior with custom settings.
    /// </summary>
    /// <param name="maxRetries">Maximum retry attempts per chunk.</param>
    /// <param name="initialDelay">Initial delay before first retry.</param>
    /// <param name="maxDelay">Maximum delay between retries.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithRetry(int maxRetries, TimeSpan? initialDelay = null, TimeSpan? maxDelay = null)
    {
        _retryPolicy = RetryPolicy.Create(maxRetries, initialDelay, maxDelay);
        return this;
    }

    /// <summary>
    ///     Disables retries - fail immediately on first error.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithNoRetry()
    {
        _retryPolicy = RetryPolicy.None;
        return this;
    }

    /// <summary>
    ///     Sets the maximum download speed.
    /// </summary>
    /// <param name="bytesPerSecond">Maximum bytes per second, or null for unlimited.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithMaxSpeed(long? bytesPerSecond)
    {
        _maxBytesPerSecond = bytesPerSecond;
        return this;
    }

    /// <summary>
    ///     Sets the maximum download speed in megabytes per second.
    /// </summary>
    /// <param name="mbPerSecond">Maximum MB/s, or null for unlimited.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithMaxSpeedMbps(double? mbPerSecond)
    {
        _maxBytesPerSecond = mbPerSecond.HasValue ? (long)(mbPerSecond.Value * 1024 * 1024) : null;
        return this;
    }

    /// <summary>
    ///     Enables resume support to continue interrupted downloads.
    /// </summary>
    /// <param name="enable">True to enable resume support.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithResume(bool enable = true)
    {
        _resume = enable;
        return this;
    }

    /// <summary>
    ///     Sets the cancellation token for the download operation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DepotDownloadOptionsBuilder WithCancellation(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        return this;
    }

    /// <summary>
    ///     Builds the <see cref="DepotDownloadOptions" /> instance with full validation.
    /// </summary>
    /// <returns>A configured <see cref="DepotDownloadOptions" /> instance.</returns>
    /// <exception cref="ArgumentException">When validation fails.</exception>
    public DepotDownloadOptions Build()
    {
        // Required field validation
        if (_appId == SteamConstants.InvalidAppId)
            throw new ArgumentException("AppId must be specified. Use ForApp() to set it.");

        // Mutually exclusive options validation
        if (_downloadAllPlatforms && !string.IsNullOrEmpty(_os))
            throw new ArgumentException("Cannot specify both ForAllPlatforms() and ForOs(). Choose one.");

        if (_downloadAllArchs && !string.IsNullOrEmpty(_architecture))
            throw new ArgumentException("Cannot specify both ForAllArchitectures() and ForArchitecture(). Choose one.");

        if (_downloadAllLanguages && !string.IsNullOrEmpty(_language))
            throw new ArgumentException("Cannot specify both ForAllLanguages() and ForLanguage(). Choose one.");

        // Branch password requires branch
        if (!string.IsNullOrEmpty(_branchPassword) && string.IsNullOrEmpty(_branch))
            throw new ArgumentException("BranchPassword requires a branch to be specified. Use FromBranch().");

        // Resume requires install directory
        if (_resume && string.IsNullOrWhiteSpace(_installDirectory))
            throw new ArgumentException("Resume requires an install directory. Use ToDirectory().");

        // Range validation
        if (_maxDownloads < 1 || _maxDownloads > 64)
            throw new ArgumentOutOfRangeException(nameof(_maxDownloads), "MaxDownloads must be between 1 and 64.");

        if (_maxBytesPerSecond is < 0)
            throw new ArgumentOutOfRangeException(nameof(_maxBytesPerSecond), "MaxBytesPerSecond cannot be negative.");

        // OS validation
        if (!string.IsNullOrEmpty(_os) && _os is not ("windows" or "linux" or "macos"))
            throw new ArgumentException("Os must be 'windows', 'linux', or 'macos'.", nameof(_os));

        // Architecture validation
        if (!string.IsNullOrEmpty(_architecture) && _architecture is not ("32" or "64"))
            throw new ArgumentException("Architecture must be '32' or '64'.", nameof(_architecture));

        return new DepotDownloadOptions
        {
            AppId = _appId,
            DepotManifestIds = [.. _depotManifestIds],
            Branch = _branch,
            BranchPassword = _branchPassword,
            Os = _os,
            Architecture = _architecture,
            Language = _language,
            LowViolence = _lowViolence,
            InstallDirectory = _installDirectory,
            FilesToDownload = _filesToDownload.Count > 0 ? _filesToDownload : null,
            FilesToDownloadRegex = _fileRegexPatterns.Count > 0 ? _fileRegexPatterns : null,
            VerifyAll = _verifyAll,
            DownloadManifestOnly = _downloadManifestOnly,
            MaxDownloads = _maxDownloads,
            CellId = _cellId,
            LoginId = _loginId,
            DownloadAllPlatforms = _downloadAllPlatforms,
            DownloadAllArchs = _downloadAllArchs,
            DownloadAllLanguages = _downloadAllLanguages,
            VerifyDiskSpace = _verifyDiskSpace,
            RetryPolicy = _retryPolicy,
            MaxBytesPerSecond = _maxBytesPerSecond,
            Resume = _resume,
            CancellationToken = _cancellationToken
        };
    }

    /// <summary>
    ///     Creates a new builder instance.
    /// </summary>
    /// <returns>A new <see cref="DepotDownloadOptionsBuilder" /> instance.</returns>
    public static DepotDownloadOptionsBuilder Create()
    {
        return new DepotDownloadOptionsBuilder();
    }
}
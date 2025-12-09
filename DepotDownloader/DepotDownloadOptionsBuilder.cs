using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace DepotDownloader.Lib;

/// <summary>
///     Fluent builder for creating <see cref="DepotDownloadOptions" /> instances.
/// </summary>
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
    ///     Builds the <see cref="DepotDownloadOptions" /> instance.
    /// </summary>
    /// <returns>A configured <see cref="DepotDownloadOptions" /> instance.</returns>
    /// <exception cref="System.ArgumentException">When AppId is not specified.</exception>
    public DepotDownloadOptions Build()
    {
        if (_appId == SteamConstants.InvalidAppId)
            throw new ArgumentException("AppId must be specified. Use ForApp() to set it.");

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
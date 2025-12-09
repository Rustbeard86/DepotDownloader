using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SteamKit2;

namespace DepotDownloader.Lib;

/// <summary>
///     Main client for downloading Steam depot content programmatically.
/// </summary>
public sealed class DepotDownloaderClient : IDisposable
{
    private readonly ContentDownloader _downloader;
    private readonly IUserInterface _userInterface;
    private bool _disposed;
    private HttpDiagnosticEventListener _httpEventListener;

    /// <summary>
    ///     Creates a new DepotDownloader client.
    /// </summary>
    /// <param name="userInterface">Interface for user interaction. Uses NullUserInterface if not provided.</param>
    public DepotDownloaderClient(IUserInterface userInterface = null)
    {
        _userInterface = userInterface ?? new NullUserInterface();

        // Initialize stores
        AccountSettingsStore.Initialize(_userInterface);
        DepotConfigStore.Initialize(_userInterface);
        Util.Initialize(_userInterface);

        // Create the instance-based downloader
        _downloader = new ContentDownloader(_userInterface);

        // Load account settings
        AccountSettingsStore.LoadFromFile("account.config");
    }

    /// <summary>
    ///     Releases resources and disconnects from Steam.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _httpEventListener?.Dispose();
        _downloader.Dispose();

        _disposed = true;
    }

    /// <summary>
    ///     Event raised when download progress changes.
    ///     Includes bytes downloaded, speed, ETA, and file information.
    /// </summary>
    public event EventHandler<DownloadProgressEventArgs> DownloadProgress;

    /// <summary>
    ///     Enables verbose debug logging including HTTP diagnostics.
    /// </summary>
    public void EnableDebugLogging()
    {
        DebugLog.Enabled = true;
        DebugLog.AddListener((category, message) => { _userInterface.WriteDebug(category, message); });

        _httpEventListener = new HttpDiagnosticEventListener(_userInterface);
    }

    /// <summary>
    ///     Authenticates with Steam using username and password.
    /// </summary>
    /// <param name="username">Steam username.</param>
    /// <param name="password">Steam password. If null and no saved token exists, will prompt via IUserInterface.</param>
    /// <param name="rememberPassword">Save credentials for future use.</param>
    /// <param name="skipAppConfirmation">Prefer entering a 2FA code instead of prompting to accept in the Steam mobile app.</param>
    /// <returns>True if authentication succeeded.</returns>
    public bool Login(string username, string password = null, bool rememberPassword = false,
        bool skipAppConfirmation = false)
    {
        ThrowIfDisposed();

        _downloader.Config.RememberPassword = rememberPassword;
        _downloader.Config.SkipAppConfirmation = skipAppConfirmation;

        // Check for saved login token
        string loginToken = null;
        if (username is not null && rememberPassword)
            AccountSettingsStore.Instance.LoginTokens.TryGetValue(username, out loginToken);

        // Prompt for password if needed
        if (username is not null && password is null && loginToken is null &&
            !AccountSettingsStore.Instance.LoginTokens.ContainsKey(username))
        {
            _userInterface.Write("Enter account password for \"{0}\": ", username);
            password = _userInterface.IsInputRedirected
                ? _userInterface.ReadLine()
                : _userInterface.ReadPassword();
            _userInterface.WriteLine();
        }

        // Validate password constraints
        if (!string.IsNullOrEmpty(password))
        {
            const int maxPasswordSize = 64;

            if (password.Length > maxPasswordSize)
                _userInterface.WriteError(
                    $"Warning: Password is longer than {maxPasswordSize} characters, which is not supported by Steam.");

            if (password.Any(c => c < ' ' || c > '~'))
                _userInterface.WriteError(
                    "Warning: Password contains non-ASCII characters, which is not supported by Steam.");
        }

        return _downloader.InitializeSteam3(username, password);
    }

    /// <summary>
    ///     Authenticates with Steam anonymously (limited access).
    /// </summary>
    /// <param name="skipAppConfirmation">Prefer entering a 2FA code instead of prompting to accept in the Steam mobile app.</param>
    /// <returns>True if authentication succeeded.</returns>
    public bool LoginAnonymous(bool skipAppConfirmation = false)
    {
        ThrowIfDisposed();
        _downloader.Config.SkipAppConfirmation = skipAppConfirmation;
        return _downloader.InitializeSteam3(null, null);
    }

    /// <summary>
    ///     Authenticates with Steam using QR code displayed via IUserInterface.
    /// </summary>
    /// <param name="rememberPassword">Save credentials for future use.</param>
    /// <param name="skipAppConfirmation">Prefer entering a 2FA code instead of prompting to accept in the Steam mobile app.</param>
    /// <returns>True if authentication succeeded.</returns>
    public bool LoginWithQrCode(bool rememberPassword = false, bool skipAppConfirmation = false)
    {
        ThrowIfDisposed();

        _downloader.Config.UseQrCode = true;
        _downloader.Config.RememberPassword = rememberPassword;
        _downloader.Config.SkipAppConfirmation = skipAppConfirmation;

        return _downloader.InitializeSteam3(null, null);
    }

    /// <summary>
    ///     Gets information about a Steam application.
    /// </summary>
    /// <param name="appId">The Steam application ID.</param>
    /// <returns>Application information including name and type.</returns>
    /// <exception cref="InvalidOperationException">When not logged in.</exception>
    public async Task<AppInfo> GetAppInfoAsync(uint appId)
    {
        ThrowIfDisposed();
        ThrowIfNotLoggedIn();

        await _downloader.Steam3.RequestAppInfo(appId);
        return AppInfoService.GetAppInfo(_downloader.Steam3, appId);
    }

    /// <summary>
    ///     Gets all depots for a Steam application.
    /// </summary>
    /// <param name="appId">The Steam application ID.</param>
    /// <returns>List of depot information.</returns>
    /// <exception cref="InvalidOperationException">When not logged in.</exception>
    public async Task<IReadOnlyList<DepotInfo>> GetDepotsAsync(uint appId)
    {
        ThrowIfDisposed();
        ThrowIfNotLoggedIn();

        await _downloader.Steam3.RequestAppInfo(appId);
        return AppInfoService.GetDepots(_downloader.Steam3, appId);
    }

    /// <summary>
    ///     Gets all branches for a Steam application.
    /// </summary>
    /// <param name="appId">The Steam application ID.</param>
    /// <returns>List of branch information.</returns>
    /// <exception cref="InvalidOperationException">When not logged in.</exception>
    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(uint appId)
    {
        ThrowIfDisposed();
        ThrowIfNotLoggedIn();

        await _downloader.Steam3.RequestAppInfo(appId);
        return AppInfoService.GetBranches(_downloader.Steam3, appId);
    }

    /// <summary>
    ///     Gets a download plan showing what would be downloaded without actually downloading.
    /// </summary>
    /// <param name="options">Download configuration options.</param>
    /// <returns>A plan showing files, sizes, and depot information.</returns>
    /// <exception cref="InvalidOperationException">When not logged in.</exception>
    public async Task<DownloadPlan> GetDownloadPlanAsync(DepotDownloadOptions options)
    {
        ThrowIfDisposed();
        ThrowIfNotLoggedIn();
        ArgumentNullException.ThrowIfNull(options);

        if (options.AppId == ContentDownloader.InvalidAppId)
            throw new ArgumentException("AppId must be specified", nameof(options));

        // Apply configuration from options
        ApplyConfiguration(options);

        return await _downloader.GetDownloadPlanAsync(
            options.AppId,
            options.DepotManifestIds,
            options.Branch,
            options.Os,
            options.Architecture,
            options.Language,
            options.LowViolence
        );
    }

    /// <summary>
    ///     Checks if there is sufficient disk space for a download.
    /// </summary>
    /// <param name="options">Download configuration options.</param>
    /// <returns>A result indicating available space, required space, and whether there's enough.</returns>
    /// <exception cref="InvalidOperationException">When not logged in.</exception>
    public async Task<DiskSpaceCheckResult> CheckDiskSpaceAsync(DepotDownloadOptions options)
    {
        ThrowIfDisposed();
        ThrowIfNotLoggedIn();

        var plan = await GetDownloadPlanAsync(options);
        var requiredBytes = plan.TotalDownloadSize;

        var targetPath = options.InstallDirectory ?? Environment.CurrentDirectory;
        var fullPath = Path.GetFullPath(targetPath);
        var root = Path.GetPathRoot(fullPath) ?? fullPath;
        var driveInfo = new DriveInfo(root);

        var availableBytes = (ulong)driveInfo.AvailableFreeSpace;

        return new DiskSpaceCheckResult(
            availableBytes >= requiredBytes,
            requiredBytes,
            availableBytes,
            root);
    }

    /// <summary>
    ///     Gets the required disk space for a download without checking availability.
    /// </summary>
    /// <param name="options">Download configuration options.</param>
    /// <returns>The total download size in bytes.</returns>
    /// <exception cref="InvalidOperationException">When not logged in.</exception>
    public async Task<ulong> GetRequiredDiskSpaceAsync(DepotDownloadOptions options)
    {
        var plan = await GetDownloadPlanAsync(options);
        return plan.TotalDownloadSize;
    }

    /// <summary>
    ///     Downloads Steam app content based on the provided options.
    /// </summary>
    /// <param name="options">Download configuration options.</param>
    /// <exception cref="ArgumentNullException">When options is null.</exception>
    /// <exception cref="ArgumentException">When AppId is not specified.</exception>
    /// <exception cref="ContentDownloaderException">When download fails.</exception>
    /// <exception cref="InsufficientDiskSpaceException">When there is not enough disk space and VerifyDiskSpace is true.</exception>
    /// <exception cref="OperationCanceledException">When the operation is cancelled via CancellationToken.</exception>
    public async Task DownloadAppAsync(DepotDownloadOptions options)
    {
        ThrowIfDisposed();
        ThrowIfNotLoggedIn();
        ArgumentNullException.ThrowIfNull(options);

        if (options.AppId == ContentDownloader.InvalidAppId)
            throw new ArgumentException("AppId must be specified", nameof(options));

        // Apply configuration from options
        ApplyConfiguration(options);

        // Check disk space if enabled
        if (options.VerifyDiskSpace && !options.DownloadManifestOnly)
        {
            var spaceCheck = await CheckDiskSpaceAsync(options);
            if (!spaceCheck.HasSufficientSpace)
                throw new InsufficientDiskSpaceException(
                    spaceCheck.RequiredBytes,
                    spaceCheck.AvailableBytes,
                    spaceCheck.TargetDrive);
        }

        // Create progress callback if there are subscribers
        DownloadProgressCallback progressCallback = null;
        if (DownloadProgress is not null) progressCallback = args => DownloadProgress?.Invoke(this, args);

        // Perform download with cancellation token and progress callback
        await _downloader.DownloadAppAsync(
            options.AppId,
            options.DepotManifestIds,
            options.Branch,
            options.Os,
            options.Architecture,
            options.Language,
            options.LowViolence,
            false,
            progressCallback,
            options.CancellationToken
        );
    }

    /// <summary>
    ///     Downloads a Steam Workshop published file.
    /// </summary>
    /// <param name="appId">The Steam AppID that owns the workshop item.</param>
    /// <param name="publishedFileId">The workshop PublishedFileId.</param>
    public async Task DownloadPublishedFileAsync(uint appId, ulong publishedFileId)
    {
        ThrowIfDisposed();

        if (appId == ContentDownloader.InvalidAppId)
            throw new ArgumentException("AppId must be specified", nameof(appId));

        if (publishedFileId == ContentDownloader.InvalidManifestId)
            throw new ArgumentException("PublishedFileId must be specified", nameof(publishedFileId));

        await _downloader.DownloadPubfileAsync(appId, publishedFileId);
    }

    /// <summary>
    ///     Downloads Steam UGC (User Generated Content).
    /// </summary>
    /// <param name="appId">The Steam AppID that owns the UGC.</param>
    /// <param name="ugcId">The UGC ID.</param>
    public async Task DownloadUgcAsync(uint appId, ulong ugcId)
    {
        ThrowIfDisposed();

        if (appId == ContentDownloader.InvalidAppId)
            throw new ArgumentException("AppId must be specified", nameof(appId));

        if (ugcId == ContentDownloader.InvalidManifestId)
            throw new ArgumentException("UGC ID must be specified", nameof(ugcId));

        await _downloader.DownloadUgcAsync(appId, ugcId);
    }

    /// <summary>
    ///     Applies download options to the downloader configuration.
    /// </summary>
    private void ApplyConfiguration(DepotDownloadOptions options)
    {
        _downloader.Config.InstallDirectory = options.InstallDirectory;
        _downloader.Config.FilesToDownload = options.FilesToDownload;
        _downloader.Config.FilesToDownloadRegex = options.FilesToDownloadRegex;
        _downloader.Config.UsingFileList =
            options.FilesToDownload?.Count > 0 ||
            options.FilesToDownloadRegex?.Count > 0;
        _downloader.Config.VerifyAll = options.VerifyAll;
        _downloader.Config.DownloadManifestOnly = options.DownloadManifestOnly;
        _downloader.Config.MaxDownloads = options.MaxDownloads;
        _downloader.Config.CellId = options.CellId;
        _downloader.Config.LoginId = options.LoginId;
        _downloader.Config.BetaPassword = options.BranchPassword;
        _downloader.Config.DownloadAllPlatforms = options.DownloadAllPlatforms;
        _downloader.Config.DownloadAllArchs = options.DownloadAllArchs;
        _downloader.Config.DownloadAllLanguages = options.DownloadAllLanguages;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(DepotDownloaderClient));
    }

    private void ThrowIfNotLoggedIn()
    {
        if (!_downloader.IsLoggedOn)
            throw new InvalidOperationException("Must be logged in to Steam before querying app information.");
    }
}
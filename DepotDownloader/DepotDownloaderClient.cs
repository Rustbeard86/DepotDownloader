using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;

namespace DepotDownloader.Lib;

/// <summary>
///     Main client for downloading Steam depot content programmatically.
/// </summary>
public sealed class DepotDownloaderClient : IDisposable
{
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

        // Initialize all components
        AccountSettingsStore.Initialize(_userInterface);
        DepotConfigStore.Initialize(_userInterface);
        ContentDownloader.Initialize(_userInterface);
        Util.Initialize(_userInterface);

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
        ContentDownloader.ShutdownSteam3();

        _disposed = true;
    }

    /// <summary>
    ///     Event raised when download progress is updated.
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

        ContentDownloader.Config.RememberPassword = rememberPassword;
        ContentDownloader.Config.SkipAppConfirmation = skipAppConfirmation;

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

        return ContentDownloader.InitializeSteam3(username, password);
    }

    /// <summary>
    ///     Authenticates with Steam anonymously (limited access).
    /// </summary>
    /// <param name="skipAppConfirmation">Prefer entering a 2FA code instead of prompting to accept in the Steam mobile app.</param>
    /// <returns>True if authentication succeeded.</returns>
    public bool LoginAnonymous(bool skipAppConfirmation = false)
    {
        ThrowIfDisposed();
        ContentDownloader.Config.SkipAppConfirmation = skipAppConfirmation;
        return ContentDownloader.InitializeSteam3(null, null);
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

        ContentDownloader.Config.UseQrCode = true;
        ContentDownloader.Config.RememberPassword = rememberPassword;
        ContentDownloader.Config.SkipAppConfirmation = skipAppConfirmation;

        return ContentDownloader.InitializeSteam3(null, null);
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

        await ContentDownloader.Steam3.RequestAppInfo(appId);
        return AppInfoService.GetAppInfo(ContentDownloader.Steam3, appId);
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

        await ContentDownloader.Steam3.RequestAppInfo(appId);
        return AppInfoService.GetDepots(ContentDownloader.Steam3, appId);
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

        await ContentDownloader.Steam3.RequestAppInfo(appId);
        return AppInfoService.GetBranches(ContentDownloader.Steam3, appId);
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

        return await ContentDownloader.GetDownloadPlanAsync(
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

        // Create progress context if we have subscribers
        DownloadProgressContext progressContext = null;
        if (DownloadProgress is not null)
        {
            var plan = await GetDownloadPlanAsync(options);
            progressContext = new DownloadProgressContext(
                plan.TotalDownloadSize,
                plan.TotalFileCount,
                args => DownloadProgress?.Invoke(this, args));
        }

        // Perform download with cancellation and progress support
        await ContentDownloader.DownloadAppAsync(
            options.AppId,
            options.DepotManifestIds,
            options.Branch,
            options.Os,
            options.Architecture,
            options.Language,
            options.LowViolence,
            false,
            progressContext, options.CancellationToken);
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

        await ContentDownloader.DownloadPubfileAsync(appId, publishedFileId);
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

        await ContentDownloader.DownloadUgcAsync(appId, ugcId);
    }

    /// <summary>
    ///     Applies download options to the ContentDownloader configuration.
    /// </summary>
    /// <remarks>
    ///     This method is intentionally not static as it's part of the client's instance API.
    ///     It may need to access instance state in future enhancements.
    /// </remarks>
    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Method is part of instance API and may need instance access in future")]
    private void ApplyConfiguration(DepotDownloadOptions options)
    {
        ContentDownloader.Config.InstallDirectory = options.InstallDirectory;
        ContentDownloader.Config.FilesToDownload = options.FilesToDownload;
        ContentDownloader.Config.FilesToDownloadRegex = options.FilesToDownloadRegex;
        ContentDownloader.Config.UsingFileList =
            options.FilesToDownload?.Count > 0 ||
            options.FilesToDownloadRegex?.Count > 0;
        ContentDownloader.Config.VerifyAll = options.VerifyAll;
        ContentDownloader.Config.DownloadManifestOnly = options.DownloadManifestOnly;
        ContentDownloader.Config.MaxDownloads = options.MaxDownloads;
        ContentDownloader.Config.CellId = options.CellId;
        ContentDownloader.Config.LoginId = options.LoginId;
        ContentDownloader.Config.BetaPassword = options.BranchPassword;
        ContentDownloader.Config.DownloadAllPlatforms = options.DownloadAllPlatforms;
        ContentDownloader.Config.DownloadAllArchs = options.DownloadAllArchs;
        ContentDownloader.Config.DownloadAllLanguages = options.DownloadAllLanguages;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(DepotDownloaderClient));
    }

    private static void ThrowIfNotLoggedIn()
    {
        if (ContentDownloader.Steam3 is null || !ContentDownloader.Steam3.IsLoggedOn)
            throw new InvalidOperationException("Must be logged in to Steam before querying app information.");
    }
}

/// <summary>
///     Internal context for tracking download progress and calculating speed/ETA.
/// </summary>
internal sealed class DownloadProgressContext(
    ulong totalBytes,
    int totalFiles,
    Action<DownloadProgressEventArgs> progressCallback)
{
    private const int SpeedSampleWindowMs = 5000;
    private readonly Lock _lock = new();

    // Rolling average for speed calculation
    private readonly Queue<(long timestamp, ulong bytes)> _speedSamples = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private ulong _bytesDownloaded;
    private uint _currentDepotId;
    private string _currentFile;
    private int _filesCompleted;

    public ulong TotalBytes { get; } = totalBytes;
    public int TotalFiles { get; } = totalFiles;

    /// <summary>
    ///     Reports progress for a chunk download.
    /// </summary>
    public void ReportProgress(ulong bytesDownloaded, string currentFile, uint depotId, bool fileCompleted)
    {
        lock (_lock)
        {
            _bytesDownloaded += bytesDownloaded;
            _currentFile = currentFile;
            _currentDepotId = depotId;

            if (fileCompleted)
                _filesCompleted++;

            // Update speed samples
            var now = _stopwatch.ElapsedMilliseconds;
            _speedSamples.Enqueue((now, _bytesDownloaded));

            // Remove old samples outside the window
            while (_speedSamples.Count > 0 && _speedSamples.Peek().timestamp < now - SpeedSampleWindowMs)
                _speedSamples.Dequeue();

            // Calculate speed from samples
            var speed = 0.0;
            var eta = TimeSpan.MaxValue;

            if (_speedSamples.Count >= 2)
            {
                var (timestamp, bytes) = _speedSamples.Peek();
                var timeDelta = (now - timestamp) / 1000.0;
                var bytesDelta = _bytesDownloaded - bytes;

                if (timeDelta > 0)
                {
                    speed = bytesDelta / timeDelta;

                    if (speed > 0)
                    {
                        var remainingBytes = TotalBytes - _bytesDownloaded;
                        eta = TimeSpan.FromSeconds(remainingBytes / speed);
                    }
                }
            }

            var args = new DownloadProgressEventArgs
            {
                BytesDownloaded = _bytesDownloaded,
                TotalBytes = TotalBytes,
                CurrentFile = _currentFile,
                FilesCompleted = _filesCompleted,
                TotalFiles = TotalFiles,
                SpeedBytesPerSecond = speed,
                EstimatedTimeRemaining = eta,
                CurrentDepotId = _currentDepotId
            };

            progressCallback?.Invoke(args);
        }
    }
}
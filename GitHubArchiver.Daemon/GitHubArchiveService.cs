using System.IO.Compression;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Octokit;

namespace GitHubArchiver.Daemon;

/// <summary>
///     Implementation of <see cref="IGitHubArchiveService" /> using GitHub API via Octokit.
///     Handles zipping content, uploading to GitHub, and updating the workshopcontent.json manifest.
///     Files under 100MB use the Contents API, larger files use Releases.
/// </summary>
public sealed class GitHubArchiveService : IGitHubArchiveService
{
    private const long MaxContentApiSize = 100 * 1024 * 1024; // 100 MB limit for Contents API
    private const string LargeFilesReleaseName = "workshop-content";
    private const string LargeFilesReleaseTag = "workshop-content";

    // Files and folders created by DepotDownloader that should not be archived
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DepotDownloader",
        "depot.config"
    };

    private readonly GitHubClient _gitHubClient;
    private readonly GitHubOptions _gitHubOptions;
    private readonly ILogger<GitHubArchiveService> _logger;
    private readonly ISteamMetadataService _steamMetadataService;

    // In-memory manifest cache to avoid constant GitHub API calls
    private List<RemoteRoomMeta> _manifestCache;
    private string _manifestSha;
    private int _pendingManifestEntries;

    // Cached release for large files
    private Release _largeFilesRelease;

    public GitHubArchiveService(
        IOptions<GitHubOptions> gitHubOptions,
        ISteamMetadataService steamMetadataService,
        ILogger<GitHubArchiveService> logger)
    {
        _gitHubOptions = gitHubOptions.Value;
        _steamMetadataService = steamMetadataService;
        _logger = logger;

        if (string.IsNullOrEmpty(_gitHubOptions.Token))
            throw new InvalidOperationException("GitHub:Token is not configured in appsettings.json.");

        _gitHubClient = new GitHubClient(new ProductHeaderValue(_gitHubOptions.AgentName))
        {
            Credentials = new Credentials(_gitHubOptions.Token)
        };

        // Set longer timeout for large file uploads (default is 100 seconds)
        _gitHubClient.SetRequestTimeout(TimeSpan.FromMinutes(30));
    }

    public async Task<bool> ArchiveAndPushAsync(string workshopId, string contentFolderPath,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Processing Workshop ID: {WorkshopId}...", workshopId);

        if (string.IsNullOrEmpty(_gitHubOptions.ProxyUrl))
        {
            _logger.LogError("GitHub:ProxyUrl is not configured in appsettings.json.");
            return false;
        }

        try
        {
            // Verify repository access first
            try
            {
                await _gitHubClient.Repository.Get(_gitHubOptions.Owner, _gitHubOptions.Repository);
                _logger.LogDebug("Repository {Owner}/{Repo} is accessible", _gitHubOptions.Owner,
                    _gitHubOptions.Repository);
            }
            catch (NotFoundException)
            {
                _logger.LogError("Repository {Owner}/{Repo} not found. Please create it on GitHub first.",
                    _gitHubOptions.Owner, _gitHubOptions.Repository);
                return false;
            }

            // Fetch official metadata from Steam
            var steamMeta = await _steamMetadataService.GetMetadataAsync(workshopId, ct);
            if (steamMeta is null)
            {
                _logger.LogError("Failed to fetch Steam metadata for {WorkshopId}", workshopId);
                return false;
            }

            // Create ZIP archive in temp folder
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"{workshopId}.zip");
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            _logger.LogInformation("Zipping content from {ContentPath} to {ZipPath}...", contentFolderPath,
                tempZipPath);

            if (!Directory.Exists(contentFolderPath))
            {
                _logger.LogError("Content folder does not exist: {ContentPath}", contentFolderPath);
                return false;
            }

            // Create ZIP excluding DepotDownloader files
            var filesAdded = CreateFilteredZip(contentFolderPath, tempZipPath);
            _logger.LogInformation("Added {FileCount} files to ZIP (excluded DepotDownloader metadata)", filesAdded);

            if (filesAdded == 0)
            {
                _logger.LogError("No content files found to archive for {WorkshopId}", workshopId);
                return false;
            }

            var zipInfo = new FileInfo(tempZipPath);
            var sizeMB = zipInfo.Length / 1024.0 / 1024.0;
            _logger.LogInformation("Created ZIP: {ZipPath} ({SizeMB:F2} MB)", tempZipPath, sizeMB);

            string downloadUrl;

            if (zipInfo.Length >= MaxContentApiSize)
            {
                // Large file - use Releases
                _logger.LogInformation("Large file ({SizeMB:F0} MB) - uploading as release asset...", sizeMB);
                downloadUrl = await UploadAsReleaseAssetAsync(workshopId, tempZipPath, ct);
            }
            else
            {
                // Small file - use Contents API
                var zipBytes = await File.ReadAllBytesAsync(tempZipPath, ct);
                var targetRepoPath = $"maps/{workshopId}.zip";

                _logger.LogInformation("Uploading to GitHub: {Owner}/{Repo}/{Path} ({SizeMB:F2} MB)...",
                    _gitHubOptions.Owner, _gitHubOptions.Repository, targetRepoPath, sizeMB);

                var uploadStart = DateTime.UtcNow;
                await CreateOrUpdateBinaryFileAsync(targetRepoPath, zipBytes, $"Add/Update map {workshopId}");
                var uploadDuration = DateTime.UtcNow - uploadStart;

                _logger.LogInformation("Uploaded ZIP to {TargetPath} in {Duration:F1} seconds",
                    targetRepoPath, uploadDuration.TotalSeconds);

                downloadUrl = $"{_gitHubOptions.ProxyUrl.TrimEnd('/')}/maps/{workshopId}.zip";
            }

            // Add entry to manifest cache
            var entry = new RemoteRoomMeta
            {
                Id = steamMeta.Publishedfileid,
                Name = steamMeta.Title,
                ImageUrl = steamMeta.Preview_url,
                DownloadUrl = downloadUrl,
                Timestamp = steamMeta.Time_updated
            };

            await AddToManifestCacheAsync(entry);

            // Flush manifest every 10 entries to avoid losing too much progress
            if (_pendingManifestEntries >= 10)
            {
                _logger.LogInformation("Flushing manifest (10 pending entries)...");
                await FlushManifestAsync();
            }

            // Cleanup temp file
            File.Delete(tempZipPath);

            _logger.LogInformation("Successfully archived {WorkshopId}!", workshopId);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Archive operation for {WorkshopId} was cancelled", workshopId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation failed for {WorkshopId}", workshopId);
            return false;
        }
    }

    /// <summary>
    ///     Uploads a large file as a GitHub Release asset.
    /// </summary>
    private async Task<string> UploadAsReleaseAssetAsync(string workshopId, string filePath, CancellationToken ct)
    {
        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var assetName = $"{workshopId}.zip";

        // Get or create the release for large files
        var release = await GetOrCreateLargeFilesReleaseAsync();

        // Check if asset already exists and delete it
        var existingAsset = release.Assets.FirstOrDefault(a => a.Name == assetName);
        if (existingAsset is not null)
        {
            _logger.LogDebug("Deleting existing release asset {AssetName}", assetName);
            await _gitHubClient.Repository.Release.DeleteAsset(owner, repo, existingAsset.Id);
        }

        // Upload the new asset
        await using var stream = File.OpenRead(filePath);
        var uploadStart = DateTime.UtcNow;

        var assetUpload = new ReleaseAssetUpload
        {
            FileName = assetName,
            ContentType = "application/zip",
            RawData = stream
        };

        _logger.LogDebug("Uploading release asset {AssetName}...", assetName);
        var asset = await _gitHubClient.Repository.Release.UploadAsset(release, assetUpload, ct);

        var uploadDuration = DateTime.UtcNow - uploadStart;
        _logger.LogInformation("Uploaded release asset {AssetName} in {Duration:F1} seconds",
            assetName, uploadDuration.TotalSeconds);

        // Refresh the cached release to include the new asset
        _largeFilesRelease = await _gitHubClient.Repository.Release.Get(owner, repo, release.Id);

        // Return the proxy URL for the release asset
        // Format: /releases/{assetName} - the Cloudflare worker will need to handle this
        return $"{_gitHubOptions.ProxyUrl.TrimEnd('/')}/releases/{assetName}";
    }

    /// <summary>
    ///     Gets or creates the release used for storing large files.
    /// </summary>
    private async Task<Release> GetOrCreateLargeFilesReleaseAsync()
    {
        if (_largeFilesRelease is not null)
            return _largeFilesRelease;

        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;

        try
        {
            _largeFilesRelease = await _gitHubClient.Repository.Release.Get(owner, repo, LargeFilesReleaseTag);
            _logger.LogDebug("Found existing release: {ReleaseName}", _largeFilesRelease.Name);
        }
        catch (NotFoundException)
        {
            _logger.LogInformation("Creating release for large files: {ReleaseName}", LargeFilesReleaseName);

            var newRelease = new NewRelease(LargeFilesReleaseTag)
            {
                Name = LargeFilesReleaseName,
                Body = "Workshop content files that exceed GitHub's 100MB file size limit.",
                Draft = false,
                Prerelease = false
            };

            _largeFilesRelease = await _gitHubClient.Repository.Release.Create(owner, repo, newRelease);
        }

        return _largeFilesRelease;
    }

    /// <summary>
    ///     Flushes the manifest cache to GitHub if there are pending changes.
    ///     Call this after processing a batch of items.
    /// </summary>
    public async Task FlushManifestAsync()
    {
        if (_pendingManifestEntries == 0 || _manifestCache is null)
        {
            _logger.LogDebug("Manifest cache is clean, nothing to flush");
            return;
        }

        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var branch = _gitHubOptions.Branch;
        var manifestPath = _gitHubOptions.ManifestPath;

        // Sort by name for consistent ordering
        var sortedList = _manifestCache.OrderBy(x => x.Name).ToList();

        var newJson = JsonConvert.SerializeObject(sortedList, Formatting.Indented);

        try
        {
            if (_manifestSha is null)
                await _gitHubClient.Repository.Content.CreateFile(
                    owner, repo, manifestPath,
                    new CreateFileRequest("Update manifest", newJson, branch));
            else
                await _gitHubClient.Repository.Content.UpdateFile(
                    owner, repo, manifestPath,
                    new UpdateFileRequest("Update manifest", newJson, _manifestSha, branch));

            // Refresh SHA after update
            var contents =
                await _gitHubClient.Repository.Content.GetAllContentsByRef(owner, repo, manifestPath, branch);
            if (contents.Count > 0) _manifestSha = contents[0].Sha;

            _logger.LogInformation("Manifest flushed to GitHub with {Count} entries.", sortedList.Count);
            _pendingManifestEntries = 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush manifest to GitHub");
            // Don't reset pending count - we'll try again next time
        }
    }

    /// <summary>
    ///     Loads the manifest from GitHub into cache if not already loaded.
    /// </summary>
    private async Task EnsureManifestLoadedAsync()
    {
        if (_manifestCache is not null)
            return;

        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var branch = _gitHubOptions.Branch;
        var manifestPath = _gitHubOptions.ManifestPath;

        _manifestCache = [];
        _manifestSha = null;
        _pendingManifestEntries = 0;

        try
        {
            var contents =
                await _gitHubClient.Repository.Content.GetAllContentsByRef(owner, repo, manifestPath, branch);
            if (contents.Count > 0)
            {
                var jsonContent = contents[0].Content;
                _manifestSha = contents[0].Sha;
                _manifestCache = JsonConvert.DeserializeObject<List<RemoteRoomMeta>>(jsonContent) ?? [];
                _logger.LogInformation("Loaded manifest with {Count} existing entries", _manifestCache.Count);
            }
        }
        catch (NotFoundException)
        {
            _logger.LogInformation("{ManifestPath} not found, will create new one.", manifestPath);
        }
    }

    /// <summary>
    ///     Adds or updates an entry in the manifest cache.
    /// </summary>
    private async Task AddToManifestCacheAsync(RemoteRoomMeta entry)
    {
        await EnsureManifestLoadedAsync();

        var existingIndex = _manifestCache.FindIndex(x => x.Id == entry.Id);
        if (existingIndex >= 0)
            _manifestCache[existingIndex] = entry;
        else
            _manifestCache.Add(entry);

        _pendingManifestEntries++;

        _logger.LogDebug("Added {Id} to manifest cache (now {Count} entries, {Pending} pending flush)",
            entry.Id, _manifestCache.Count, _pendingManifestEntries);
    }

    /// <summary>
    ///     Creates a ZIP archive excluding DepotDownloader metadata files.
    /// </summary>
    private int CreateFilteredZip(string sourceDirectory, string destinationZipPath)
    {
        var filesAdded = 0;

        using var archive = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);

        var sourceDir = new DirectoryInfo(sourceDirectory);
        var basePath = sourceDir.FullName;

        foreach (var file in sourceDir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            // Skip files in excluded directories
            if (IsExcluded(file.FullName, basePath))
            {
                _logger.LogDebug("Excluding file: {File}", file.FullName);
                continue;
            }

            // Get relative path for the archive
            var relativePath = Path.GetRelativePath(basePath, file.FullName);

            archive.CreateEntryFromFile(file.FullName, relativePath, CompressionLevel.Optimal);
            filesAdded++;
        }

        return filesAdded;
    }

    /// <summary>
    ///     Checks if a file path should be excluded from the archive.
    /// </summary>
    private static bool IsExcluded(string filePath, string basePath)
    {
        var relativePath = Path.GetRelativePath(basePath, filePath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var part in parts)
            if (ExcludedNames.Contains(part))
                return true;

        return false;
    }

    /// <summary>
    ///     Creates or updates a binary file (e.g., ZIP) in the repository.
    /// </summary>
    private async Task CreateOrUpdateBinaryFileAsync(string path, byte[] content, string message)
    {
        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var branch = _gitHubOptions.Branch;

        string? sha = null;
        try
        {
            var existingFiles = await _gitHubClient.Repository.Content.GetAllContentsByRef(owner, repo, path, branch);
            if (existingFiles.Count > 0) sha = existingFiles[0].Sha;
        }
        catch (NotFoundException)
        {
            // File doesn't exist, we'll create it
        }

        var base64Content = Convert.ToBase64String(content);
        _logger.LogDebug("Uploading {Bytes} bytes as {Base64Len} base64 chars", content.Length, base64Content.Length);

        if (sha is null)
        {
            // Create new file - convertContentToBase64: false because we're already providing base64
            var request = new CreateFileRequest(message, base64Content, branch, false);
            await _gitHubClient.Repository.Content.CreateFile(owner, repo, path, request);
        }
        else
        {
            // Update existing file
            var request = new UpdateFileRequest(message, base64Content, sha, branch, false);
            await _gitHubClient.Repository.Content.UpdateFile(owner, repo, path, request);
        }
    }
}
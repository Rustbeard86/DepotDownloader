using System.IO.Compression;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Octokit;

namespace GitHubArchiver.Daemon;

/// <summary>
///     Implementation of <see cref="IGitHubArchiveService" /> using GitHub API via Octokit.
///     Handles zipping content, uploading to GitHub Releases, and updating the workshopcontent.json manifest.
///     Each workshop item gets its own release, making updates simple (just replace the asset).
/// </summary>
public sealed class GitHubArchiveService : IGitHubArchiveService
{
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
    private List<RemoteRoomMeta>? _manifestCache;
    private string? _manifestSha;
    private int _pendingManifestEntries;

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

        // Set longer timeout for large file uploads
        _gitHubClient.SetRequestTimeout(TimeSpan.FromMinutes(30));
    }

    public async Task<ArchiveResult> ArchiveAndPushAsync(string workshopId, string contentFolderPath,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Processing Workshop ID: {WorkshopId}...", workshopId);

        if (string.IsNullOrEmpty(_gitHubOptions.ProxyUrl))
        {
            _logger.LogError("GitHub:ProxyUrl is not configured in appsettings.json.");
            return ArchiveResult.ConfigurationError;
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
            catch (AuthorizationException ex)
            {
                _logger.LogError(ex, "GitHub authentication failed - token may be invalid or expired");
                return ArchiveResult.AuthenticationFailure;
            }
            catch (NotFoundException)
            {
                _logger.LogError("Repository {Owner}/{Repo} not found. Please create it on GitHub first.",
                    _gitHubOptions.Owner, _gitHubOptions.Repository);
                return ArchiveResult.ConfigurationError;
            }

            // Ensure repo is not empty (required for releases)
            await EnsureRepoInitializedAsync();

            // Fetch official metadata from Steam
            var steamMeta = await _steamMetadataService.GetMetadataAsync(workshopId, ct);
            if (steamMeta is null)
            {
                _logger.LogError("Failed to fetch Steam metadata for {WorkshopId}", workshopId);
                return ArchiveResult.ContentError;
            }

            // Create ZIP archive in temp folder
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"{workshopId}.zip");
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            _logger.LogInformation("Zipping content from {ContentPath} to {ZipPath}...", contentFolderPath,
                tempZipPath);

            if (!Directory.Exists(contentFolderPath))
            {
                _logger.LogError("Content folder does not exist: {ContentPath}", contentFolderPath);
                return ArchiveResult.ContentError;
            }

            // Create ZIP excluding DepotDownloader files
            var filesAdded = CreateFilteredZip(contentFolderPath, tempZipPath);
            _logger.LogInformation("Added {FileCount} files to ZIP (excluded DepotDownloader metadata)", filesAdded);

            if (filesAdded == 0)
            {
                _logger.LogError("No content files found to archive for {WorkshopId}", workshopId);
                return ArchiveResult.ContentError;
            }

            var zipInfo = new FileInfo(tempZipPath);
            var sizeMB = zipInfo.Length / 1024.0 / 1024.0;
            _logger.LogInformation("Created ZIP: {ZipPath} ({SizeMB:F2} MB)", tempZipPath, sizeMB);

            string downloadUrl;

            try
            {
                // Upload as release asset - each workshop item gets its own release
                downloadUrl = await UploadAsReleaseAsync(workshopId, steamMeta.Title, tempZipPath, ct);
            }
            catch (AuthorizationException ex)
            {
                _logger.LogError(ex, "GitHub authentication failed during upload - token may be invalid or expired");
                try { File.Delete(tempZipPath); } catch { /* ignore */ }
                return ArchiveResult.AuthenticationFailure;
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
            return ArchiveResult.Success;
        }
        catch (AuthorizationException ex)
        {
            _logger.LogError(ex, "GitHub authentication failed for {WorkshopId}", workshopId);
            return ArchiveResult.AuthenticationFailure;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Archive operation for {WorkshopId} was cancelled", workshopId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation failed for {WorkshopId}", workshopId);
            return ArchiveResult.TransientFailure;
        }
    }

    /// <summary>
    ///     Uploads a file as a GitHub Release asset.
    ///     Each workshop item gets its own release (tag = workshop ID).
    ///     Updates are handled by deleting the old asset and uploading a new one.
    /// </summary>
    private async Task<string> UploadAsReleaseAsync(string workshopId, string title, string filePath, CancellationToken ct)
    {
        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var tag = workshopId;
        var assetName = $"{workshopId}.zip";

        Release release;

        // Try to get existing release, or create a new one
        try
        {
            release = await _gitHubClient.Repository.Release.Get(owner, repo, tag);
            _logger.LogDebug("Found existing release for {WorkshopId}", workshopId);

            // Delete existing asset if present (for updates)
            var existingAsset = release.Assets.FirstOrDefault(a => a.Name == assetName);
            if (existingAsset is not null)
            {
                _logger.LogDebug("Deleting existing asset for update");
                await _gitHubClient.Repository.Release.DeleteAsset(owner, repo, existingAsset.Id);
            }
        }
        catch (NotFoundException)
        {
            _logger.LogDebug("Creating new release for {WorkshopId}", workshopId);

            var newRelease = new NewRelease(tag)
            {
                Name = workshopId,
                Body = workshopId,
                Draft = false,
                Prerelease = false
            };

            release = await _gitHubClient.Repository.Release.Create(owner, repo, newRelease);
        }

        // Upload the asset
        await using var stream = File.OpenRead(filePath);
        var fileInfo = new FileInfo(filePath);
        var uploadStart = DateTime.UtcNow;

        var assetUpload = new ReleaseAssetUpload
        {
            FileName = assetName,
            ContentType = "application/zip",
            RawData = stream
        };

        _logger.LogInformation("Uploading {AssetName} ({SizeMB:F2} MB) to release...", 
            assetName, fileInfo.Length / 1024.0 / 1024.0);
        
        await _gitHubClient.Repository.Release.UploadAsset(release, assetUpload, ct);

        var uploadDuration = DateTime.UtcNow - uploadStart;
        _logger.LogInformation("Uploaded {AssetName} in {Duration:F1} seconds",
            assetName, uploadDuration.TotalSeconds);

        // Return the proxy URL for the release asset
        return $"{_gitHubOptions.ProxyUrl.TrimEnd('/')}/releases/{workshopId}/{assetName}";
    }

    /// <summary>
    ///     Flushes the manifest cache to GitHub if there are pending changes.
    /// </summary>
    public async Task<bool> FlushManifestAsync()
    {
        if (_pendingManifestEntries == 0 || _manifestCache is null)
        {
            _logger.LogDebug("Manifest cache is clean, nothing to flush");
            return true;
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
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush manifest to GitHub");
            return false;
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

        var existingIndex = _manifestCache!.FindIndex(x => x.Id == entry.Id);
        if (existingIndex >= 0)
            _manifestCache[existingIndex] = entry;
        else
            _manifestCache.Add(entry);

        _pendingManifestEntries++;

        _logger.LogDebug("Added {Id} to manifest cache (now {Count} entries, {Pending} pending flush)",
            entry.Id, _manifestCache.Count, _pendingManifestEntries);
    }

    /// <summary>
    ///     Ensures the repository has at least one commit (required for creating releases).
    ///     Creates an initial README.md if the repo is empty.
    /// </summary>
    private async Task EnsureRepoInitializedAsync()
    {
        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var branch = _gitHubOptions.Branch;

        try
        {
            // Try to get README.md from the repo - if it exists, repo is initialized
            await _gitHubClient.Repository.Content.GetAllContentsByRef(owner, repo, "README.md", branch);
            _logger.LogDebug("Repository has content, no initialization needed");
        }
        catch (NotFoundException)
        {
            // Repo is empty or README doesn't exist, create initial commit
            _logger.LogInformation("Repository is empty, creating initial commit...");

            var readme = "# Workshop Content Archive\n\nThis repository contains archived Steam Workshop content.";
            
            await _gitHubClient.Repository.Content.CreateFile(
                owner, repo, "README.md",
                new CreateFileRequest("Initial commit", readme, branch));

            _logger.LogInformation("Created initial README.md");
        }
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
            if (IsExcluded(file.FullName, basePath))
            {
                _logger.LogDebug("Excluding file: {File}", file.FullName);
                continue;
            }

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
}
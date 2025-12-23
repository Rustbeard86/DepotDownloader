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
    private readonly HttpClient _httpClient;
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
        _httpClient = new HttpClient();

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

            // Download preview image from Steam
            string? tempImagePath = null;
            string? imageExtension = null;
            if (!string.IsNullOrEmpty(steamMeta.Preview_url))
                try
                {
                    (tempImagePath, imageExtension) =
                        await DownloadPreviewImageAsync(workshopId, steamMeta.Preview_url, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download preview image for {WorkshopId}, continuing without it",
                        workshopId);
                }

            string downloadUrl;
            var imageUrl = string.Empty;

            try
            {
                // Upload ZIP as release asset
                downloadUrl = await UploadAsReleaseAsync(workshopId, tempZipPath, ct);

                // Upload image to /images/ directory in repo
                if (tempImagePath is not null && imageExtension is not null)
                    try
                    {
                        imageUrl = await UploadPreviewImageToRepoAsync(workshopId, tempImagePath, imageExtension, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to upload preview image for {WorkshopId}, continuing without it",
                            workshopId);
                    }
            }
            catch (AuthorizationException ex)
            {
                _logger.LogError(ex, "GitHub authentication failed during upload - token may be invalid or expired");
                try
                {
                    File.Delete(tempZipPath);
                }
                catch
                {
                    /* ignore */
                }

                if (tempImagePath is not null)
                    try
                    {
                        File.Delete(tempImagePath);
                    }
                    catch
                    {
                        /* ignore */
                    }

                return ArchiveResult.AuthenticationFailure;
            }

            // Add entry to manifest cache
            var entry = new RemoteRoomMeta
            {
                Id = steamMeta.Publishedfileid,
                Name = steamMeta.Title,
                ImageUrl = imageUrl,
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

            // Cleanup temp files
            File.Delete(tempZipPath);
            if (tempImagePath is not null)
                try
                {
                    File.Delete(tempImagePath);
                }
                catch
                {
                    /* ignore */
                }

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
    ///     Rebuilds the manifest from GitHub releases and Steam metadata.
    ///     Useful when manifest gets out of sync with actual releases.
    /// </summary>
    public async Task<int> RebuildManifestAsync(CancellationToken ct = default)
    {
        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var branch = _gitHubOptions.Branch;

        _logger.LogInformation("Rebuilding manifest from GitHub releases...");

        // Fetch all releases
        var allReleases = new List<Release>();
        var page = 1;
        while (true)
        {
            var releases = await _gitHubClient.Repository.Release.GetAll(owner, repo, new ApiOptions
            {
                PageCount = 1,
                PageSize = 100,
                StartPage = page
            });

            if (releases.Count == 0)
                break;

            allReleases.AddRange(releases);

            if (releases.Count < 100)
                break;

            page++;
        }

        _logger.LogInformation("Found {Count} releases on GitHub", allReleases.Count);

        // Get list of images in /images/ directory
        var imageFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var imagesContent =
                await _gitHubClient.Repository.Content.GetAllContentsByRef(owner, repo, "images", branch);
            foreach (var file in imagesContent) imageFiles.Add(file.Name);
            _logger.LogInformation("Found {Count} images in /images/ directory", imageFiles.Count);
        }
        catch (NotFoundException)
        {
            _logger.LogInformation("No /images/ directory found");
        }

        // Build new manifest
        var newManifest = new List<RemoteRoomMeta>();
        var processed = 0;
        var failed = 0;

        foreach (var release in allReleases)
        {
            ct.ThrowIfCancellationRequested();

            // Skip non-numeric tags (like README)
            if (!ulong.TryParse(release.TagName, out _))
            {
                _logger.LogDebug("Skipping non-workshop release: {Tag}", release.TagName);
                continue;
            }

            var workshopId = release.TagName;

            // Check if it has a ZIP asset
            var zipAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip"));
            if (zipAsset is null)
            {
                _logger.LogWarning("Release {WorkshopId} has no ZIP asset, skipping", workshopId);
                continue;
            }

            // Find image in /images/ directory
            var imageUrl = string.Empty;
            var possibleExtensions = new[] { ".jpg", ".png", ".gif", ".webp" };
            foreach (var ext in possibleExtensions)
            {
                var imageName = $"{workshopId}{ext}";
                if (imageFiles.Contains(imageName))
                {
                    imageUrl = $"{_gitHubOptions.ProxyUrl.TrimEnd('/')}/images/{imageName}";
                    break;
                }
            }

            // Fetch Steam metadata
            var steamMeta = await _steamMetadataService.GetMetadataAsync(workshopId, ct);
            if (steamMeta is null)
            {
                _logger.LogWarning("Could not fetch Steam metadata for {WorkshopId}", workshopId);
                failed++;
                continue;
            }

            var entry = new RemoteRoomMeta
            {
                Id = steamMeta.Publishedfileid,
                Name = steamMeta.Title,
                ImageUrl = imageUrl,
                DownloadUrl = $"{_gitHubOptions.ProxyUrl.TrimEnd('/')}/releases/{workshopId}/{zipAsset.Name}",
                Timestamp = steamMeta.Time_updated
            };

            newManifest.Add(entry);
            processed++;

            if (processed % 50 == 0) _logger.LogInformation("Processed {Count} releases...", processed);

            // Rate limit Steam API
            await Task.Delay(100, ct);
        }

        // Replace manifest cache and flush
        _manifestCache = newManifest;
        _manifestSha = null; // Force re-fetch SHA
        _pendingManifestEntries = newManifest.Count;

        // Load existing SHA
        try
        {
            var contents = await _gitHubClient.Repository.Content.GetAllContentsByRef(
                owner, repo, _gitHubOptions.ManifestPath, branch);
            if (contents.Count > 0)
                _manifestSha = contents[0].Sha;
        }
        catch (NotFoundException)
        {
            // Will create new file
        }

        await FlushManifestAsync();

        _logger.LogInformation("Manifest rebuilt with {Count} entries ({Failed} failed to fetch metadata)",
            newManifest.Count, failed);

        return newManifest.Count;
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
    ///     Downloads the preview image from Steam and saves to temp file.
    /// </summary>
    private async Task<(string path, string extension)> DownloadPreviewImageAsync(string workshopId, string previewUrl,
        CancellationToken ct)
    {
        _logger.LogDebug("Downloading preview image for {WorkshopId} from {Url}", workshopId, previewUrl);

        var response = await _httpClient.GetAsync(previewUrl, ct);
        response.EnsureSuccessStatusCode();

        // Determine file extension from content type or URL
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"{workshopId}_preview{extension}");

        await using var fileStream = File.Create(tempPath);
        await response.Content.CopyToAsync(fileStream, ct);

        _logger.LogDebug("Downloaded preview image to {Path}", tempPath);
        return (tempPath, extension);
    }

    /// <summary>
    ///     Uploads preview image to the images directory in the repo.
    /// </summary>
    private async Task<string> UploadPreviewImageToRepoAsync(string workshopId, string imagePath, string extension,
        CancellationToken ct)
    {
        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var branch = _gitHubOptions.Branch;
        var imageName = $"{workshopId}{extension}";
        var repoPath = $"images/{imageName}";

        // Read raw bytes and convert to base64 - GitHub API expects base64 encoded content
        var imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
        var base64Content = Convert.ToBase64String(imageBytes);

        try
        {
            // Check if file already exists
            var existingFile =
                await _gitHubClient.Repository.Content.GetAllContentsByRef(owner, repo, repoPath, branch);
            if (existingFile.Count > 0)
            {
                // Update existing file - use raw API call since Octokit expects string content
                await UpdateBinaryFileAsync(owner, repo, repoPath, base64Content, existingFile[0].Sha, branch, 
                    $"Update image for {workshopId}");
                _logger.LogDebug("Updated preview image for {WorkshopId}", workshopId);
            }
        }
        catch (NotFoundException)
        {
            // Create new file - use raw API call since Octokit expects string content
            await CreateBinaryFileAsync(owner, repo, repoPath, base64Content, branch,
                $"Add image for {workshopId}");
            _logger.LogDebug("Created preview image for {WorkshopId}", workshopId);
        }

        return $"{_gitHubOptions.ProxyUrl.TrimEnd('/')}/images/{imageName}";
    }

    /// <summary>
    ///     Creates a binary file in the repo using the GitHub API directly.
    /// </summary>
    private async Task CreateBinaryFileAsync(string owner, string repo, string path, string base64Content, 
        string branch, string message)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";
        
        var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Add("Authorization", $"Bearer {_gitHubOptions.Token}");
        request.Headers.Add("User-Agent", _gitHubOptions.AgentName);
        request.Headers.Add("Accept", "application/vnd.github.v3+json");
        
        var payload = new
        {
            message,
            content = base64Content,
            branch
        };
        
        request.Content = new StringContent(
            JsonConvert.SerializeObject(payload),
            System.Text.Encoding.UTF8,
            "application/json");
        
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    ///     Updates a binary file in the repo using the GitHub API directly.
    /// </summary>
    private async Task UpdateBinaryFileAsync(string owner, string repo, string path, string base64Content,
        string sha, string branch, string message)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";
        
        var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Add("Authorization", $"Bearer {_gitHubOptions.Token}");
        request.Headers.Add("User-Agent", _gitHubOptions.AgentName);
        request.Headers.Add("Accept", "application/vnd.github.v3+json");
        
        var payload = new
        {
            message,
            content = base64Content,
            sha,
            branch
        };
        
        request.Content = new StringContent(
            JsonConvert.SerializeObject(payload),
            System.Text.Encoding.UTF8,
            "application/json");
        
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    ///     Uploads ZIP as GitHub Release asset.
    ///     Each workshop item gets its own release (tag = workshop ID).
    ///     Updates are handled by deleting old asset and uploading new one.
    /// </summary>
    private async Task<string> UploadAsReleaseAsync(string workshopId, string zipPath, CancellationToken ct)
    {
        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var tag = workshopId;
        var zipAssetName = $"{workshopId}.zip";

        Release release;

        // Try to get existing release, or create a new one
        try
        {
            release = await _gitHubClient.Repository.Release.Get(owner, repo, tag);
            _logger.LogDebug("Found existing release for {WorkshopId}", workshopId);

            // Delete existing ZIP asset if present (for updates)
            var existingAsset = release.Assets.FirstOrDefault(a => a.Name == zipAssetName);
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

        // Upload ZIP asset
        await using var stream = File.OpenRead(zipPath);
        var fileInfo = new FileInfo(zipPath);
        var uploadStart = DateTime.UtcNow;

        var assetUpload = new ReleaseAssetUpload
        {
            FileName = zipAssetName,
            ContentType = "application/zip",
            RawData = stream
        };

        _logger.LogInformation("Uploading {AssetName} ({SizeMB:F2} MB) to release...",
            zipAssetName, fileInfo.Length / 1024.0 / 1024.0);

        await _gitHubClient.Repository.Release.UploadAsset(release, assetUpload, ct);

        var uploadDuration = DateTime.UtcNow - uploadStart;
        _logger.LogInformation("Uploaded {AssetName} in {Duration:F1} seconds",
            zipAssetName, uploadDuration.TotalSeconds);

        // Return the proxy URL
        return $"{_gitHubOptions.ProxyUrl.TrimEnd('/')}/releases/{workshopId}/{zipAssetName}";
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
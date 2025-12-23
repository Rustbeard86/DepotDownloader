using System.IO.Compression;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Octokit;

namespace GitHubArchiver.Daemon;

/// <summary>
///     Implementation of <see cref="IGitHubArchiveService" /> using GitHub API via Octokit.
///     Handles zipping content, uploading to GitHub, and updating the workshopcontent.json manifest.
/// </summary>
public sealed class GitHubArchiveService : IGitHubArchiveService
{
    private readonly GitHubClient _gitHubClient;
    private readonly GitHubOptions _gitHubOptions;
    private readonly ILogger<GitHubArchiveService> _logger;
    private readonly ISteamMetadataService _steamMetadataService;

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
            // Fetch official metadata from Steam
            var steamMeta = await _steamMetadataService.GetMetadataAsync(workshopId, ct);
            if (steamMeta is null)
            {
                _logger.LogError("Failed to fetch Steam metadata for {WorkshopId}", workshopId);
                return false;
            }

            // Create ZIP archive
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"{workshopId}.zip");
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            _logger.LogInformation("Zipping content from {ContentPath}...", contentFolderPath);
            ZipFile.CreateFromDirectory(contentFolderPath, tempZipPath);
            var zipBytes = await File.ReadAllBytesAsync(tempZipPath, ct);

            // Upload ZIP to private GitHub repo (binary file - needs base64)
            var targetRepoPath = $"maps/{workshopId}.zip";
            await CreateOrUpdateBinaryFileAsync(targetRepoPath, zipBytes, $"Add/Update map {workshopId}");
            _logger.LogInformation("Uploaded ZIP to {TargetPath}", targetRepoPath);

            // Update the manifest
            var entry = new RemoteRoomMeta
            {
                Id = steamMeta.Publishedfileid,
                Name = steamMeta.Title,
                ImageUrl = steamMeta.Preview_url,
                DownloadUrl = $"{_gitHubOptions.ProxyUrl.TrimEnd('/')}/maps/{workshopId}.zip",
                Timestamp = steamMeta.Time_updated
            };

            await UpdateManifestAsync(entry);

            // Cleanup temp file
            File.Delete(tempZipPath);

            _logger.LogInformation("Successfully archived {WorkshopId}!", workshopId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation failed for {WorkshopId}", workshopId);
            return false;
        }
    }

    /// <summary>
    ///     Creates or updates a binary file (e.g., ZIP) in the repository.
    ///     Binary content must be base64 encoded.
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

        if (sha is null)
            await _gitHubClient.Repository.Content.CreateFile(
                owner, repo, path,
                new CreateFileRequest(message, base64Content, branch));
        else
            await _gitHubClient.Repository.Content.UpdateFile(
                owner, repo, path,
                new UpdateFileRequest(message, base64Content, sha, branch));
    }

    /// <summary>
    ///     Creates or updates a text file in the repository.
    /// </summary>
    private async Task CreateOrUpdateTextFileAsync(string path, string content, string message)
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

        if (sha is null)
            await _gitHubClient.Repository.Content.CreateFile(
                owner, repo, path,
                new CreateFileRequest(message, content, branch));
        else
            await _gitHubClient.Repository.Content.UpdateFile(
                owner, repo, path,
                new UpdateFileRequest(message, content, sha, branch));
    }

    private async Task UpdateManifestAsync(RemoteRoomMeta newEntry)
    {
        var owner = _gitHubOptions.Owner;
        var repo = _gitHubOptions.Repository;
        var branch = _gitHubOptions.Branch;
        var manifestPath = _gitHubOptions.ManifestPath;

        var currentList = new List<RemoteRoomMeta>();

        // Fetch existing manifest
        try
        {
            var contents = await _gitHubClient.Repository.Content.GetAllContentsByRef(owner, repo, manifestPath, branch);
            if (contents.Count > 0)
            {
                var jsonContent = contents[0].Content;
                currentList = JsonConvert.DeserializeObject<List<RemoteRoomMeta>>(jsonContent) ?? [];
            }
        }
        catch (NotFoundException)
        {
            _logger.LogInformation("{ManifestPath} not found, creating new one.", manifestPath);
        }

        // Remove existing entry with same ID to prevent duplicates, then add updated entry
        currentList.RemoveAll(x => x.Id == newEntry.Id);
        currentList.Add(newEntry);

        // Sort by name for consistent ordering
        currentList = currentList.OrderBy(x => x.Name).ToList();

        var newJson = JsonConvert.SerializeObject(currentList, Formatting.Indented);
        await CreateOrUpdateTextFileAsync(manifestPath, newJson, $"Update manifest: {newEntry.Name}");

        _logger.LogInformation("Manifest updated with {Count} entries.", currentList.Count);
    }
}
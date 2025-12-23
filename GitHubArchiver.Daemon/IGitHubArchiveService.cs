namespace GitHubArchiver.Daemon;

/// <summary>
///     Service for uploading workshop archives to GitHub and managing the manifest.
/// </summary>
public interface IGitHubArchiveService
{
    /// <summary>
    ///     Archives workshop content to GitHub and updates the rooms.json manifest.
    /// </summary>
    /// <param name="workshopId">The Steam Workshop ID.</param>
    /// <param name="contentFolderPath">Path to the downloaded content folder.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> ArchiveAndPushAsync(string workshopId, string contentFolderPath, CancellationToken ct = default);
}
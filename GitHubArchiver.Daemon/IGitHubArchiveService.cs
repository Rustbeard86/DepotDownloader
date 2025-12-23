namespace GitHubArchiver.Daemon;

/// <summary>
///     Service for uploading workshop archives to GitHub and managing the manifest.
/// </summary>
public interface IGitHubArchiveService
{
    /// <summary>
    ///     Archives workshop content to GitHub. The manifest is updated in memory
    ///     and should be flushed after a batch of uploads using <see cref="FlushManifestAsync" />.
    /// </summary>
    /// <param name="workshopId">The Steam Workshop ID.</param>
    /// <param name="contentFolderPath">Path to the downloaded content folder.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> ArchiveAndPushAsync(string workshopId, string contentFolderPath, CancellationToken ct = default);

    /// <summary>
    ///     Flushes the manifest cache to GitHub if there are pending changes.
    ///     Call this after processing a batch of items to update workshopcontent.json.
    /// </summary>
    Task FlushManifestAsync();
}
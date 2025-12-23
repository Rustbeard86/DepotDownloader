namespace GitHubArchiver.Daemon;

/// <summary>
///     Result of an archive operation.
/// </summary>
public enum ArchiveResult
{
    /// <summary>Successfully archived to GitHub.</summary>
    Success,
    
    /// <summary>Failed due to a transient error (network, rate limit, etc.).</summary>
    TransientFailure,
    
    /// <summary>Failed due to bad credentials - token is invalid or expired.</summary>
    AuthenticationFailure,
    
    /// <summary>Failed due to missing configuration.</summary>
    ConfigurationError,
    
    /// <summary>Failed due to missing content or metadata.</summary>
    ContentError
}

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
    /// <returns>Result indicating success or type of failure.</returns>
    Task<ArchiveResult> ArchiveAndPushAsync(string workshopId, string contentFolderPath, CancellationToken ct = default);

    /// <summary>
    ///     Flushes the manifest cache to GitHub if there are pending changes.
    ///     Call this after processing a batch of items to update workshopcontent.json.
    /// </summary>
    /// <returns>True if successful or nothing to flush, false if failed.</returns>
    Task<bool> FlushManifestAsync();
}
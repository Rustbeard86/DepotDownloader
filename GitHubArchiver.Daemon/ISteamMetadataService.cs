namespace GitHubArchiver.Daemon;

/// <summary>
///     Service for fetching metadata from the Steam Workshop API.
/// </summary>
public interface ISteamMetadataService
{
    /// <summary>
    ///     Fetches workshop item details from the Steam API.
    /// </summary>
    /// <param name="workshopId">The Steam Workshop ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Steam details or null if not found.</returns>
    Task<SteamDetails?> GetMetadataAsync(string workshopId, CancellationToken ct = default);
}
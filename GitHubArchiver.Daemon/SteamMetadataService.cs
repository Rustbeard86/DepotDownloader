using Newtonsoft.Json;

namespace GitHubArchiver.Daemon;

/// <summary>
///     Implementation of <see cref="ISteamMetadataService" /> using the Steam Web API.
/// </summary>
public sealed class SteamMetadataService : ISteamMetadataService, IDisposable
{
    private const string SteamApiUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamMetadataService> _logger;
    private bool _disposed;

    public SteamMetadataService(ILogger<SteamMetadataService> logger)
    {
        _httpClient = new HttpClient();
        _logger = logger;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _disposed = true;
    }

    public async Task<SteamDetails?> GetMetadataAsync(string workshopId, CancellationToken ct = default)
    {
        try
        {
            var formContent = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("itemcount", "1"),
                new KeyValuePair<string, string>("publishedfileids[0]", workshopId)
            ]);

            var response = await _httpClient.PostAsync(SteamApiUrl, formContent, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var data = JsonConvert.DeserializeObject<SteamApiResponse>(json);

            if (data?.Response?.Publishedfiledetails is null || data.Response.Publishedfiledetails.Count == 0)
            {
                _logger.LogWarning("Steam API returned no data for workshop ID {WorkshopId}", workshopId);
                return null;
            }

            return data.Response.Publishedfiledetails[0];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch metadata for workshop ID {WorkshopId}", workshopId);
            return null;
        }
    }
}
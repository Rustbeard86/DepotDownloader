namespace GitHubArchiver.Daemon;

/// <summary>
///     Configuration for GitHub repository operations.
/// </summary>
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>
    ///     GitHub Personal Access Token with repo scope.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    ///     GitHub repository owner (username or organization).
    /// </summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>
    ///     GitHub repository name.
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    ///     GitHub branch to push to.
    /// </summary>
    public string Branch { get; set; } = "main";

    /// <summary>
    ///     User agent for GitHub API requests.
    /// </summary>
    public string AgentName { get; set; } = "GitHubArchiverDaemon";

    /// <summary>
    ///     Proxy URL for download links (e.g., Cloudflare Worker URL).
    /// </summary>
    public string ProxyUrl { get; set; } = string.Empty;

    /// <summary>
    ///     Path to the manifest file in the repository.
    /// </summary>
    public string ManifestPath { get; set; } = "workshopcontent.json";
}

/// <summary>
///     Configuration for Workshop archiving operations.
/// </summary>
public sealed class WorkshopOptions
{
    public const string SectionName = "Workshop";

    /// <summary>
    ///     Steam AppId to archive workshop items for. Can be changed at runtime.
    /// </summary>
    public uint AppId { get; set; }

    /// <summary>
    ///     Path for temporary downloads during processing.
    /// </summary>
    public string DownloadPath { get; set; } = GetDefaultDownloadPath();

    /// <summary>
    ///     Path to SQLite database file for tracking workshop items.
    /// </summary>
    public string DatabasePath { get; set; } = GetDefaultDatabasePath();

    /// <summary>
    ///     How often to poll Steam Workshop for updates (minutes).
    /// </summary>
    public int PollIntervalMinutes { get; set; } = 60;

    /// <summary>
    ///     Delay between downloading items to avoid rate limiting (seconds).
    /// </summary>
    public int DelayBetweenDownloadsSeconds { get; set; } = 2;

    /// <summary>
    ///     Maximum retry attempts for failed downloads.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    ///     Minimum time before retrying a failed download (minutes).
    /// </summary>
    public int RetryBackoffMinutes { get; set; } = 30;

    private static string GetDefaultDownloadPath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "GitHubArchiver", "downloads");
        return "/var/lib/github-archiver/downloads";
    }

    private static string GetDefaultDatabasePath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "GitHubArchiver", "workshop.db");
        return "/var/lib/github-archiver/workshop.db";
    }
}

/// <summary>
///     Configuration for Steam authentication.
/// </summary>
public sealed class SteamOptions
{
    public const string SectionName = "Steam";

    public string? Username { get; set; }
    public string? Password { get; set; }
}
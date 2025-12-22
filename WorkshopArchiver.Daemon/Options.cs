namespace WorkshopArchiver.Daemon;

public sealed class WorkshopOptions
{
    public const string SectionName = "Workshop";

    /// <summary>
    ///     Steam AppId to archive workshop items for. Can be changed at runtime.
    /// </summary>
    public uint AppId { get; set; }

    /// <summary>
    ///     Path where compressed archives are placed (for upload daemon to watch).
    /// </summary>
    public string OutputPath { get; set; } = GetDefaultOutputPath();

    /// <summary>
    ///     Path for temporary downloads during processing.
    /// </summary>
    public string DownloadPath { get; set; } = GetDefaultDownloadPath();

    /// <summary>
    ///     Path to SQLite database file.
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
    ///     7-Zip compression level (0-9).
    /// </summary>
    public int CompressionLevel { get; set; } = 9;

    /// <summary>
    ///     Maximum retry attempts for failed downloads.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    ///     Minimum time before retrying a failed download (minutes).
    /// </summary>
    public int RetryBackoffMinutes { get; set; } = 30;

    /// <summary>
    ///     Path to 7z executable (auto-detected if not specified).
    /// </summary>
    public string? SevenZipPath { get; set; }

    private static string GetDefaultOutputPath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WorkshopArchiver", "output");
        return "/var/lib/gofile-daemon/watch";
    }

    private static string GetDefaultDownloadPath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WorkshopArchiver", "downloads");
        return "/var/lib/workshop-archiver/downloads";
    }

    private static string GetDefaultDatabasePath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WorkshopArchiver", "workshop.db");
        return "/var/lib/workshop-archiver/workshop.db";
    }
}

public sealed class SteamOptions
{
    public const string SectionName = "Steam";

    public string? Username { get; set; }
    public string? Password { get; set; }
}
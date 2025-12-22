namespace WorkshopArchiver.Daemon;

public sealed class WorkshopOptions
{
    public const string SectionName = "Workshop";

    public uint AppId { get; set; }
    public string OutputPath { get; set; } = "/var/lib/gofile-daemon/watch";
    public string DownloadPath { get; set; } = "/var/lib/workshop-archiver/downloads";
    public string DatabasePath { get; set; } = "/var/lib/workshop-archiver/workshop.db";
    public int PollIntervalMinutes { get; set; } = 60;
    public int DelayBetweenDownloadsSeconds { get; set; } = 2;
    public int CompressionLevel { get; set; } = 9;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryBackoffMinutes { get; set; } = 30;
}

public sealed class SteamOptions
{
    public const string SectionName = "Steam";

    public string? Username { get; set; }
    public string? Password { get; set; }
}

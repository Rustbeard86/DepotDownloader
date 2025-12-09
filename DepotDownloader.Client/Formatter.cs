namespace DepotDownloader.Client;

/// <summary>
///     Formatting utilities for human-readable output.
/// </summary>
public static class Formatter
{
    public static string Size(ulong bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    public static string Duration(ulong seconds)
    {
        if (seconds < 60)
            return $"{seconds} seconds";
        if (seconds < 3600)
            return $"{seconds / 60} min {seconds % 60} sec";
        return $"{seconds / 3600}h {seconds % 3600 / 60}m";
    }

    public static string Truncate(string value, int maxLength, string suffix = "...")
    {
        return value.Length <= maxLength ? value : value[..(maxLength - suffix.Length)] + suffix;
    }
}
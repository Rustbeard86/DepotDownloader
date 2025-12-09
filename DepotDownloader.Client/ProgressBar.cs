using System;
using System.Diagnostics.CodeAnalysis;

namespace DepotDownloader.Client;

/// <summary>
///     ANSI-based progress bar for console display.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static")]
internal sealed class ProgressBar(int barWidth = ProgressBar.DefaultBarWidth)
{
    private const int DefaultBarWidth = 40;
    private readonly bool _useAnsi = !Console.IsOutputRedirected && SupportsAnsi();
    private int _lastLineLength;

    /// <summary>
    ///     Updates the progress bar display.
    /// </summary>
    /// <param name="bytesDownloaded">Total bytes downloaded so far.</param>
    /// <param name="totalBytes">Total bytes to download.</param>
    /// <param name="speedBytesPerSecond">Current download speed.</param>
    /// <param name="eta">Estimated time remaining.</param>
    /// <param name="filesCompleted">Number of files completed.</param>
    /// <param name="totalFiles">Total number of files.</param>
    public void Update(
        ulong bytesDownloaded,
        ulong totalBytes,
        double speedBytesPerSecond,
        TimeSpan eta,
        int filesCompleted,
        int totalFiles)
    {
        if (!_useAnsi)
            return;

        var percent = totalBytes > 0 ? (double)bytesDownloaded / totalBytes : 0;
        var filledWidth = (int)(percent * barWidth);
        var emptyWidth = barWidth - filledWidth;

        var bar = new string('█', filledWidth) + new string('░', emptyWidth);
        var percentStr = $"{percent * 100:F1}%";
        var sizeStr = $"{FormatSize(bytesDownloaded)}/{FormatSize(totalBytes)}";
        var speedStr = $"{FormatSize((ulong)speedBytesPerSecond)}/s";
        var etaStr = FormatEta(eta);
        var filesStr = $"[{filesCompleted}/{totalFiles}]";

        var line = $"\r  [{bar}] {percentStr,6} | {sizeStr} | {speedStr,10} | ETA: {etaStr,8} | Files: {filesStr}";

        // Pad with spaces to clear any previous longer line
        if (line.Length < _lastLineLength)
            line += new string(' ', _lastLineLength - line.Length);

        _lastLineLength = line.Length;

        Console.Write(line);
    }

    /// <summary>
    ///     Completes the progress bar and moves to the next line.
    /// </summary>
    public void Complete()
    {
        if (_useAnsi)
            Console.WriteLine();
    }

    /// <summary>
    ///     Clears the progress bar line.
    /// </summary>
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members",
        Justification = "Public API for consumers")]
    public void Clear()
    {
        if (!_useAnsi || _lastLineLength == 0)
            return;

        Console.Write('\r' + new string(' ', _lastLineLength) + '\r');
        _lastLineLength = 0;
    }

    private static bool SupportsAnsi()
    {
        // Most modern terminals support ANSI
        // Windows 10+ supports it via Virtual Terminal Processing
        try
        {
            // Simple check - if TERM is set, or we're on a Unix-like system
            var term = Environment.GetEnvironmentVariable("TERM");
            if (!string.IsNullOrEmpty(term))
                return true;

            // On Windows, check if virtual terminal is available
            if (OperatingSystem.IsWindows())
            {
                // Windows Terminal, VS Code terminal, and modern PowerShell support ANSI
                var wtSession = Environment.GetEnvironmentVariable("WT_SESSION");
                var vsCodeTerm = Environment.GetEnvironmentVariable("VSCODE_INJECTION");
                if (!string.IsNullOrEmpty(wtSession) || !string.IsNullOrEmpty(vsCodeTerm))
                    return true;

                // Try to enable virtual terminal processing
                return TryEnableVirtualTerminal();
            }

            return true; // Assume support on Unix-like systems
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEnableVirtualTerminal()
    {
        try
        {
            // On modern Windows, Console class handles ANSI automatically
            // Just check if we can write to console
            return Console.CursorLeft >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatSize(ulong bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.#} {sizes[order]}";
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta == TimeSpan.Zero || eta.TotalDays > 1)
            return "--:--";

        if (eta.TotalHours >= 1)
            return $"{(int)eta.TotalHours}:{eta.Minutes:D2}:{eta.Seconds:D2}";

        return $"{eta.Minutes:D2}:{eta.Seconds:D2}";
    }
}
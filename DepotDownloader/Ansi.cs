using System;
using Spectre.Console;

namespace DepotDownloader;

internal static class Ansi
{
    // https://conemu.github.io/en/AnsiEscapeCodes.html#ConEmu_specific_OSC
    // https://learn.microsoft.com/en-us/windows/terminal/tutorials/progress-bar-sequences
    public enum ProgressState
    {
        Hidden = 0,
        Default = 1,
        Error = 2,
        Indeterminate = 3,
        Warning = 4
    }

    private const char Esc = (char)0x1B;
    private const char Bel = (char)0x07;

    private static bool _useProgress;

    public static void Init()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected) return;

        if (OperatingSystem.IsLinux()) return;

        var (supportsAnsi, legacyConsole) = AnsiDetector.Detect(false, true);

        _useProgress = supportsAnsi && !legacyConsole;
    }

    public static void Progress(ulong downloaded, ulong total)
    {
        var progress = (byte)MathF.Round(downloaded / (float)total * 100.0f);
        Progress(ProgressState.Default, progress);
    }

    public static void Progress(ProgressState state, byte progress = 0)
    {
        if (!_useProgress) return;

        Console.Write($"{Esc}]9;4;{(byte)state};{progress}{Bel}");
    }
}
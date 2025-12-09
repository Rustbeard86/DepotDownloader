using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DepotDownloader.Lib;

internal static partial class AnsiDetector
{
    // Console mode flags
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

    // Standard handles
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;

    private static readonly Regex[] Regexes =
    [
        XtermRegex(), // xterm, PuTTY, Mintty
        RxvtRegex(), // RXVT
        EtermRegex(), // Eterm
        ScreenRegex(), // GNU screen, tmux
        TmuxRegex(), // tmux
        Vt100Regex(), // DEC VT series
        Vt102Regex(), // DEC VT series
        Vt220Regex(), // DEC VT series
        Vt320Regex(), // DEC VT series
        AnsiRegex(), // ANSI
        ScoansiRegex(), // SCO ANSI
        CygwinRegex(), // Cygwin, MinGW
        LinuxRegex(), // Linux console
        KonsoleRegex(), // Konsole
        BvtermRegex(), // Bitvise SSH Client
        st_256colorRegex(), // Suckless Simple Terminal, st
        AlacrittyRegex() // Alacritty
    ];

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    public static (bool SupportsAnsi, bool LegacyConsole) Detect(bool stdError, bool upgrade)
    {
        // Running on Windows?
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return DetectFromTerm();
        // Running under ConEmu?
        var conEmu = Environment.GetEnvironmentVariable("ConEmuANSI");
        if (!string.IsNullOrEmpty(conEmu) && conEmu.Equals("On", StringComparison.OrdinalIgnoreCase))
            return (true, false);

        var supportsAnsi = WindowsSupportsAnsi(upgrade, stdError, out var legacyConsole);
        return (supportsAnsi, legacyConsole);
    }

    private static (bool SupportsAnsi, bool LegacyConsole) DetectFromTerm()
    {
        // Check if the terminal is of type ANSI/VT100/xterm compatible.
        var term = Environment.GetEnvironmentVariable("TERM");
        if (string.IsNullOrWhiteSpace(term)) return (false, true);
        return Regexes.Any(regex => regex.IsMatch(term)) ? (true, false) : (false, true);
    }

    private static bool WindowsSupportsAnsi(bool upgrade, bool stdError, out bool isLegacy)
    {
        isLegacy = false;

        try
        {
            var handle = GetStdHandle(stdError ? STD_ERROR_HANDLE : STD_OUTPUT_HANDLE);

            if (!GetConsoleMode(handle, out var mode))
            {
                // Could not get console mode, try TERM (set in cygwin, WSL-Shell).
                var (ansiFromTerm, legacyFromTerm) = DetectFromTerm();

                isLegacy = ansiFromTerm ? legacyFromTerm : isLegacy;
                return ansiFromTerm;
            }

            if ((mode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0) return true;
            isLegacy = true;

            if (!upgrade) return false;

            // Try to enable ANSI support.
            mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN;
            if (!SetConsoleMode(handle, mode))
                // Enabling failed.
                return false;

            isLegacy = false;

            return true;
        }
        catch
        {
            // All we know here is that we don't support ANSI.
            return false;
        }
    }

    [GeneratedRegex("^xterm")]
    private static partial Regex XtermRegex();

    [GeneratedRegex("^rxvt")]
    private static partial Regex RxvtRegex();

    [GeneratedRegex("^eterm")]
    private static partial Regex EtermRegex();

    [GeneratedRegex("^screen")]
    private static partial Regex ScreenRegex();

    [GeneratedRegex("tmux")]
    private static partial Regex TmuxRegex();

    [GeneratedRegex("^vt100")]
    private static partial Regex Vt100Regex();

    [GeneratedRegex("^vt102")]
    private static partial Regex Vt102Regex();

    [GeneratedRegex("^vt220")]
    private static partial Regex Vt220Regex();

    [GeneratedRegex("^vt320")]
    private static partial Regex Vt320Regex();

    [GeneratedRegex("ansi")]
    private static partial Regex AnsiRegex();

    [GeneratedRegex("scoansi")]
    private static partial Regex ScoansiRegex();

    [GeneratedRegex("cygwin")]
    private static partial Regex CygwinRegex();

    [GeneratedRegex("linux")]
    private static partial Regex LinuxRegex();

    [GeneratedRegex("konsole")]
    private static partial Regex KonsoleRegex();

    [GeneratedRegex("bvterm")]
    private static partial Regex BvtermRegex();

    [GeneratedRegex("^st-256color")]
    private static partial Regex st_256colorRegex();

    [GeneratedRegex("alacritty")]
    private static partial Regex AlacrittyRegex();
}
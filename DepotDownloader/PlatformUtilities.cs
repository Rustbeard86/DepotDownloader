using System.IO;
using System.Runtime.InteropServices;

namespace DepotDownloader.Lib;

internal static class PlatformUtilities
{
    public static void SetExecutable(string path, bool value)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        const UnixFileMode modeExecute =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        var mode = File.GetUnixFileMode(path);
        var hasExecuteMask = (mode & modeExecute) == modeExecute;
        if (hasExecuteMask != value)
            File.SetUnixFileMode(path, value
                ? mode | modeExecute
                : mode & ~modeExecute);
    }
}
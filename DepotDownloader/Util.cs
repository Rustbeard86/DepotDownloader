using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using QRCoder;
using SteamKit2;

namespace DepotDownloader;

internal static class Util
{
    private static Process _currentQrViewerProcess;
    private static string _currentQrImagePath;

    public static string GetSteamOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";

        return RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)
            ?
            // Return linux as freebsd steam client doesn't exist yet
            "linux"
            : "unknown";
    }

    public static string GetSteamArch()
    {
        return Environment.Is64BitOperatingSystem ? "64" : "32";
    }

    public static string ReadPassword()
    {
        ConsoleKeyInfo keyInfo;
        var password = new StringBuilder();

        do
        {
            keyInfo = Console.ReadKey(true);

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }

                continue;
            }

            /* Printable ASCII characters only */
            var c = keyInfo.KeyChar;
            if (c is < ' ' or > '~') continue;

            password.Append(c);
            Console.Write('*');
        } while (keyInfo.Key != ConsoleKey.Enter);

        return password.ToString();
    }

    public static void DisplayQrCode(string challengeUrl)
    {
        // Close any existing QR viewer before displaying a new one
        CloseQrViewer();

        // Generate QR code
        using var qrGenerator = new QRCodeGenerator();
        var qrCodeData = qrGenerator.CreateQrCode(challengeUrl, QRCodeGenerator.ECCLevel.L);

        // Try to display as image first (better UX)
        if (TryDisplayQrCodeAsImage(qrCodeData))
        {
            Console.WriteLine("A QR code has been displayed. Use the Steam Mobile App to sign in.");
            return;
        }

        // Fallback to ASCII art in console
        using var qrCode = new AsciiQRCode(qrCodeData);
        var qrCodeAsAsciiArt = qrCode.GetLineByLineGraphic(1, drawQuietZones: true);

        Console.WriteLine("Use the Steam Mobile App to sign in with this QR code:");
        Console.WriteLine();

        foreach (var line in qrCodeAsAsciiArt) Console.WriteLine(line);
    }

    public static void CloseQrViewer()
    {
        // Close the viewer process if it exists and hasn't exited
        if (_currentQrViewerProcess is { HasExited: false })
            try
            {
                _currentQrViewerProcess.Kill();
                _currentQrViewerProcess.Dispose();
            }
            catch
            {
                // Ignore errors if process already closed
            }

        // Clean up the temp image file
        if (!string.IsNullOrEmpty(_currentQrImagePath) && File.Exists(_currentQrImagePath))
            try
            {
                File.Delete(_currentQrImagePath);
            }
            catch
            {
                // Ignore cleanup errors
            }

        _currentQrViewerProcess = null;
        _currentQrImagePath = null;
    }

    private static bool TryDisplayQrCodeAsImage(QRCodeData qrCodeData)
    {
        try
        {
            // Generate PNG image
            using var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeBytes = qrCode.GetGraphic(20); // 20 pixels per module

            // Save to temp file
            var tempPath = Path.Combine(Path.GetTempPath(), $"steam_qr_{Guid.NewGuid()}.png");
            File.WriteAllBytes(tempPath, qrCodeBytes);
            _currentQrImagePath = tempPath;

            // Try to open with platform-specific command
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: Use default image viewer
                _currentQrViewerProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                });
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: Use 'open' command
                _currentQrViewerProcess = Process.Start("open", tempPath);
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: Try common image viewers
                var viewers = new[] { "xdg-open", "eog", "display", "feh", "gpicview" };
                foreach (var viewer in viewers)
                    try
                    {
                        _currentQrViewerProcess = Process.Start(viewer, tempPath);
                        return true;
                    }
                    catch
                    {
                        // Try next viewer
                    }
            }

            // Cleanup if we couldn't open it
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Ignored - temp file cleanup is not critical
            }

            _currentQrImagePath = null;
            return false;
        }
        catch
        {
            // If anything fails, fall back to ASCII art
            return false;
        }
    }

    // Validate a file against Steam3 Chunk data
    public static List<DepotManifest.ChunkData> ValidateSteam3FileChecksums(FileStream fs,
        DepotManifest.ChunkData[] chunkdata)
    {
        var neededChunks = new List<DepotManifest.ChunkData>();

        foreach (var data in chunkdata)
        {
            fs.Seek((long)data.Offset, SeekOrigin.Begin);

            var adler = AdlerHash(fs, (int)data.UncompressedLength);
            if (!adler.SequenceEqual(BitConverter.GetBytes(data.Checksum))) neededChunks.Add(data);
        }

        return neededChunks;
    }

    internal static byte[] AdlerHash(Stream stream, int length)
    {
        uint a = 0, b = 0;
        for (var i = 0; i < length; i++)
        {
            var c = (uint)stream.ReadByte();

            a = (a + c) % 65521;
            b = (b + a) % 65521;
        }

        return BitConverter.GetBytes(a | (b << 16));
    }

    private static byte[] FileShaHash(string filename)
    {
        using var fs = File.Open(filename, FileMode.Open);
        using var sha = SHA1.Create();
        var output = sha.ComputeHash(fs);

        return output;
    }

    public static DepotManifest LoadManifestFromFile(string directory, uint depotId, ulong manifestId,
        bool badHashWarning)
    {
        // Try loading Steam format manifest first.
        var filename = Path.Combine(directory, $"{depotId}_{manifestId}.manifest");

        if (File.Exists(filename))
        {
            byte[] expectedChecksum;

            try
            {
                expectedChecksum = File.ReadAllBytes(filename + ".sha");
            }
            catch (IOException)
            {
                expectedChecksum = null;
            }

            var currentChecksum = FileShaHash(filename);

            if (expectedChecksum != null && expectedChecksum.SequenceEqual(currentChecksum))
                return DepotManifest.LoadFromFile(filename);

            if (badHashWarning)
                Console.WriteLine("Manifest {0} on disk did not match the expected checksum.", manifestId);
        }

        // Try converting legacy manifest format.
        filename = Path.Combine(directory, $"{depotId}_{manifestId}.bin");

        if (!File.Exists(filename)) return null;

        {
            byte[] expectedChecksum;

            try
            {
                expectedChecksum = File.ReadAllBytes(filename + ".sha");
            }
            catch (IOException)
            {
                expectedChecksum = null;
            }

            var oldManifest = ProtoManifest.LoadFromFile(filename, out var currentChecksum);

            if (oldManifest != null && (expectedChecksum == null || !expectedChecksum.SequenceEqual(currentChecksum)))
            {
                oldManifest = null;

                if (badHashWarning)
                    Console.WriteLine("Manifest {0} on disk did not match the expected checksum.", manifestId);
            }

            if (oldManifest != null) return oldManifest.ConvertToSteamManifest(depotId);
        }

        return null;
    }

    public static bool SaveManifestToFile(string directory, DepotManifest manifest)
    {
        try
        {
            var filename = Path.Combine(directory,
                $"{manifest.DepotID}_{manifest.ManifestGID}.manifest");
            manifest.SaveToFile(filename);
            File.WriteAllBytes(filename + ".sha", FileShaHash(filename));
            return true; // If serialization completes without throwing an exception, return true
        }
        catch (Exception)
        {
            return false; // Return false if an error occurs
        }
    }
}
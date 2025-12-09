using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SteamKit2;

namespace DepotDownloader.Lib;

public static class Util
{
    private static IUserInterface _userInterface;

    public static void Initialize(IUserInterface userInterface)
    {
        _userInterface = userInterface ?? throw new ArgumentNullException(nameof(userInterface));
    }

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

            if (expectedChecksum is not null && expectedChecksum.SequenceEqual(currentChecksum))
                return DepotManifest.LoadFromFile(filename);

            if (badHashWarning)
                _userInterface?.WriteLine("Manifest {0} on disk did not match the expected checksum.", manifestId);
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

            if (oldManifest is not null &&
                (expectedChecksum is null || !expectedChecksum.SequenceEqual(currentChecksum)))
            {
                oldManifest = null;

                if (badHashWarning)
                    _userInterface?.WriteLine("Manifest {0} on disk did not match the expected checksum.", manifestId);
            }

            if (oldManifest is not null) return oldManifest.ConvertToSteamManifest(depotId);
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
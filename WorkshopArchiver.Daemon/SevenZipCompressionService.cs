using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace WorkshopArchiver.Daemon;

public sealed class SevenZipCompressionService : ICompressionService
{
    private readonly ILogger<SevenZipCompressionService> _logger;
    private readonly int _compressionLevel;

    public SevenZipCompressionService(IOptions<WorkshopOptions> options, ILogger<SevenZipCompressionService> logger)
    {
        _logger = logger;
        _compressionLevel = options.Value.CompressionLevel;
    }

    public async Task<string> CompressDirectoryAsync(string sourceDir, string outputPath, CancellationToken ct = default)
    {
        var tempPath = outputPath + ".tmp";
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        // Delete temp file if it exists from a previous failed attempt
        if (File.Exists(tempPath))
            File.Delete(tempPath);

        // 7z a -mx=9 -mmt=on -xr!.DepotDownloader output.7z.tmp ./content/*
        // Exclude .DepotDownloader directory which contains manifest/config files
        var args = $"a -mx={_compressionLevel} -mmt=on -xr!.DepotDownloader \"{tempPath}\" \"{sourceDir}\"/*";

        _logger.LogDebug("Running: 7z {Args}", args);

        var psi = new ProcessStartInfo
        {
            FileName = "7z",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("7z failed with exit code {ExitCode}: {Stderr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"7z compression failed: {stderr}");
        }

        // Atomic rename
        if (File.Exists(outputPath))
            File.Delete(outputPath);
        File.Move(tempPath, outputPath);

        var fileInfo = new FileInfo(outputPath);
        _logger.LogInformation("Compressed {Source} to {Output} ({Size:N0} bytes)", sourceDir, outputPath, fileInfo.Length);

        return outputPath;
    }
}

using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace WorkshopArchiver.Daemon;

public sealed class SevenZipCompressionService : ICompressionService
{
    private readonly ILogger<SevenZipCompressionService> _logger;
    private readonly IOptionsMonitor<WorkshopOptions> _optionsMonitor;
    private string? _resolvedSevenZipPath;

    public SevenZipCompressionService(IOptionsMonitor<WorkshopOptions> optionsMonitor,
        ILogger<SevenZipCompressionService> logger)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    public async Task<string> CompressDirectoryAsync(string sourceDir, string outputPath,
        CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var tempPath = outputPath + ".tmp";
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        // Delete temp file if it exists from a previous failed attempt
        if (File.Exists(tempPath))
            File.Delete(tempPath);

        var sevenZipPath = ResolveSevenZipPath(options.SevenZipPath);

        // 7z a -mx=9 -mmt=on -xr!.DepotDownloader output.7z.tmp ./content/*
        // Exclude .DepotDownloader directory which contains manifest/config files
        var args = $"a -mx={options.CompressionLevel} -mmt=on -xr!.DepotDownloader \"{tempPath}\" \"{sourceDir}\"/*";

        _logger.LogDebug("Running: {SevenZip} {Args}", sevenZipPath, args);

        var psi = new ProcessStartInfo
        {
            FileName = sevenZipPath,
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
        _logger.LogInformation("Compressed {Source} to {Output} ({Size:N0} bytes)", sourceDir, outputPath,
            fileInfo.Length);

        return outputPath;
    }

    private string ResolveSevenZipPath(string? configuredPath)
    {
        // Use configured path if specified
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (File.Exists(configuredPath))
                return configuredPath;

            _logger.LogWarning("Configured 7z path not found: {Path}, falling back to auto-detection", configuredPath);
        }

        // Return cached path if already resolved
        if (_resolvedSevenZipPath is not null)
            return _resolvedSevenZipPath;

        // On Linux, just use "7z" and rely on PATH
        if (!OperatingSystem.IsWindows())
        {
            _resolvedSevenZipPath = "7z";
            return _resolvedSevenZipPath;
        }

        // On Windows, try common installation locations
        var searchPaths = new[]
        {
            // 7-Zip standard installation paths
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
            // Portable/custom locations
            Path.Combine(AppContext.BaseDirectory, "7z.exe"),
            Path.Combine(AppContext.BaseDirectory, "7-Zip", "7z.exe"),
            // Chocolatey
            @"C:\ProgramData\chocolatey\bin\7z.exe",
            // Scoop
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "7zip",
                "current", "7z.exe")
        };

        foreach (var path in searchPaths)
            if (File.Exists(path))
            {
                _logger.LogInformation("Found 7-Zip at: {Path}", path);
                _resolvedSevenZipPath = path;
                return _resolvedSevenZipPath;
            }

        // Try PATH as last resort
        _logger.LogWarning("7-Zip not found in common locations, assuming 7z is in PATH");
        _resolvedSevenZipPath = "7z";
        return _resolvedSevenZipPath;
    }
}
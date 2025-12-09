using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DepotDownloader.Client;

/// <summary>
///     Source-generated JSON serialization context for CLI output types.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ErrorResultJson))]
[JsonSerializable(typeof(DownloadResultJson))]
[JsonSerializable(typeof(DepotsResultJson))]
[JsonSerializable(typeof(BranchesResultJson))]
[JsonSerializable(typeof(DryRunResultJson))]
internal partial class JsonOutputContext : JsonSerializerContext;

/// <summary>
///     JSON output models and serialization helpers for CLI output.
/// </summary>
internal static class JsonOutput
{
    public static void WriteSuccess(DownloadResultJson result)
    {
        result.Success = true;
        result.Status = "completed";
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOutputContext.Default.DownloadResultJson));
    }

    public static void WritePartialSuccess(DownloadResultJson result)
    {
        result.Success = false;
        result.Status = "partial";
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOutputContext.Default.DownloadResultJson));
    }

    public static void WriteError(string error)
    {
        var result = new ErrorResultJson { Success = false, Error = error };
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOutputContext.Default.ErrorResultJson));
    }

    public static void WriteDepots(DepotsResultJson result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOutputContext.Default.DepotsResultJson));
    }

    public static void WriteBranches(BranchesResultJson result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOutputContext.Default.BranchesResultJson));
    }

    public static void WriteDryRun(DryRunResultJson result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOutputContext.Default.DryRunResultJson));
    }
}

/// <summary>
///     Base result with success/error status.
/// </summary>
internal sealed class ErrorResultJson
{
    public bool Success { get; set; }
    public string Error { get; set; }
}

/// <summary>
///     Result of a download operation.
/// </summary>
internal sealed class DownloadResultJson
{
    public bool Success { get; set; } = true;
    public uint AppId { get; set; }
    public string Status { get; set; } = "completed";
    public double DurationSeconds { get; set; }
    public ulong TotalBytesDownloaded { get; set; }
    public int TotalFilesDownloaded { get; set; }
    public int SuccessfulDepots { get; set; }
    public int FailedDepots { get; set; }
    public List<DepotFailureJson> Failures { get; set; }
}

/// <summary>
///     Information about a failed depot download.
/// </summary>
internal sealed class DepotFailureJson
{
    public uint DepotId { get; set; }
    public string ErrorMessage { get; set; }
}

/// <summary>
///     Result of listing depots.
/// </summary>
internal sealed class DepotsResultJson
{
    public bool Success { get; set; } = true;
    public uint AppId { get; set; }
    public string AppName { get; set; }
    public string AppType { get; set; }
    public List<DepotJson> Depots { get; set; }
}

/// <summary>
///     Depot information for JSON output.
/// </summary>
internal sealed class DepotJson
{
    public uint DepotId { get; set; }
    public string Name { get; set; }
    public string Os { get; set; }
    public string Architecture { get; set; }
    public string Language { get; set; }
    public ulong? MaxSize { get; set; }
    public bool IsSharedInstall { get; set; }
}

/// <summary>
///     Result of listing branches.
/// </summary>
internal sealed class BranchesResultJson
{
    public bool Success { get; set; } = true;
    public uint AppId { get; set; }
    public string AppName { get; set; }
    public List<BranchJson> Branches { get; set; }
}

/// <summary>
///     Branch information for JSON output.
/// </summary>
internal sealed class BranchJson
{
    public string Name { get; set; }
    public uint BuildId { get; set; }
    public DateTime? TimeUpdated { get; set; }
    public bool IsPasswordProtected { get; set; }
    public string Description { get; set; }
}

/// <summary>
///     Result of a dry-run operation.
/// </summary>
internal sealed class DryRunResultJson
{
    public bool Success { get; set; } = true;
    public uint AppId { get; set; }
    public string AppName { get; set; }
    public int TotalDepots { get; set; }
    public int TotalFiles { get; set; }
    public ulong TotalBytes { get; set; }
    public string TotalSize { get; set; }
    public List<DepotPlanJson> Depots { get; set; }
}

/// <summary>
///     Depot download plan for JSON output.
/// </summary>
internal sealed class DepotPlanJson
{
    public uint DepotId { get; set; }
    public ulong ManifestId { get; set; }
    public int FileCount { get; set; }
    public ulong TotalBytes { get; set; }
    public string TotalSize { get; set; }
    public List<FilePlanJson> Files { get; set; }
}

/// <summary>
///     File information for dry-run JSON output.
/// </summary>
internal sealed class FilePlanJson
{
    public string FileName { get; set; }
    public ulong Size { get; set; }
    public string Hash { get; set; }
}
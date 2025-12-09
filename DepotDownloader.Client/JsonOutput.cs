using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DepotDownloader.Client;

/// <summary>
///     JSON output models and serialization helpers for CLI output.
/// </summary>
internal static class JsonOutput
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void WriteSuccess(DownloadResultJson result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
    }

    public static void WriteError(string error)
    {
        var result = new ErrorResultJson { Success = false, Error = error };
        Console.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
    }

    public static void WriteDepots(DepotsResultJson result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
    }

    public static void WriteBranches(BranchesResultJson result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
    }

    public static void WriteDryRun(DryRunResultJson result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
    }
}

/// <summary>
///     Base result with success/error status.
/// </summary>
internal record ErrorResultJson
{
    public bool Success { get; init; }
    public string Error { get; init; }
}

/// <summary>
///     Result of a successful download operation.
/// </summary>
internal record DownloadResultJson
{
    public bool Success { get; init; } = true;
    public uint AppId { get; init; }
    public string Status { get; init; } = "completed";
    public double DurationSeconds { get; init; }
}

/// <summary>
///     Result of listing depots.
/// </summary>
internal record DepotsResultJson
{
    public bool Success { get; init; } = true;
    public uint AppId { get; init; }
    public string AppName { get; init; }
    public string AppType { get; init; }
    public List<DepotJson> Depots { get; init; }
}

/// <summary>
///     Depot information for JSON output.
/// </summary>
internal record DepotJson
{
    public uint DepotId { get; init; }
    public string Name { get; init; }
    public string Os { get; init; }
    public string Architecture { get; init; }
    public string Language { get; init; }
    public ulong? MaxSize { get; init; }
    public bool IsSharedInstall { get; init; }
}

/// <summary>
///     Result of listing branches.
/// </summary>
internal record BranchesResultJson
{
    public bool Success { get; init; } = true;
    public uint AppId { get; init; }
    public string AppName { get; init; }
    public List<BranchJson> Branches { get; init; }
}

/// <summary>
///     Branch information for JSON output.
/// </summary>
internal record BranchJson
{
    public string Name { get; init; }
    public uint BuildId { get; init; }
    public DateTime? TimeUpdated { get; init; }
    public bool IsPasswordProtected { get; init; }
    public string Description { get; init; }
}

/// <summary>
///     Result of a dry-run operation.
/// </summary>
internal record DryRunResultJson
{
    public bool Success { get; init; } = true;
    public uint AppId { get; init; }
    public string AppName { get; init; }
    public int TotalDepots { get; init; }
    public int TotalFiles { get; init; }
    public ulong TotalBytes { get; init; }
    public string TotalSize { get; init; }
    public List<DepotPlanJson> Depots { get; init; }
}

/// <summary>
///     Depot download plan for JSON output.
/// </summary>
internal record DepotPlanJson
{
    public uint DepotId { get; init; }
    public ulong ManifestId { get; init; }
    public int FileCount { get; init; }
    public ulong TotalBytes { get; init; }
    public string TotalSize { get; init; }
    public List<FilePlanJson> Files { get; init; }
}

/// <summary>
///     File information for dry-run JSON output.
/// </summary>
internal record FilePlanJson
{
    public string FileName { get; init; }
    public ulong Size { get; init; }
    public string Hash { get; init; }
}
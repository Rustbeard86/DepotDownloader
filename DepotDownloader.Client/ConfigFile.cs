using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DepotDownloader.Client;

/// <summary>
///     Configuration file model for JSON-based configuration.
/// </summary>
internal sealed class ConfigFile
{
    /// <summary>
    ///     Steam username for authentication.
    /// </summary>
    [JsonPropertyName("username")]
    public string Username { get; set; }

    /// <summary>
    ///     Whether to remember the password/token for future sessions.
    /// </summary>
    [JsonPropertyName("rememberPassword")]
    public bool RememberPassword { get; set; }

    /// <summary>
    ///     Use QR code for authentication.
    /// </summary>
    [JsonPropertyName("qr")]
    public bool UseQrCode { get; set; }

    /// <summary>
    ///     Prefer 2FA code over mobile app confirmation.
    /// </summary>
    [JsonPropertyName("noMobile")]
    public bool NoMobile { get; set; }

    /// <summary>
    ///     Steam AppID to download.
    /// </summary>
    [JsonPropertyName("app")]
    public uint? AppId { get; set; }

    /// <summary>
    ///     List of depot IDs to download.
    /// </summary>
    [JsonPropertyName("depots")]
    public List<uint> Depots { get; set; }

    /// <summary>
    ///     List of manifest IDs corresponding to depots.
    /// </summary>
    [JsonPropertyName("manifests")]
    public List<ulong> Manifests { get; set; }

    /// <summary>
    ///     Branch to download from.
    /// </summary>
    [JsonPropertyName("branch")]
    public string Branch { get; set; }

    /// <summary>
    ///     Password for protected branches.
    /// </summary>
    [JsonPropertyName("branchPassword")]
    public string BranchPassword { get; set; }

    /// <summary>
    ///     Target operating system.
    /// </summary>
    [JsonPropertyName("os")]
    public string Os { get; set; }

    /// <summary>
    ///     Target architecture.
    /// </summary>
    [JsonPropertyName("osarch")]
    public string OsArch { get; set; }

    /// <summary>
    ///     Target language.
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; }

    /// <summary>
    ///     Download all platform-specific depots.
    /// </summary>
    [JsonPropertyName("allPlatforms")]
    public bool AllPlatforms { get; set; }

    /// <summary>
    ///     Download all architecture-specific depots.
    /// </summary>
    [JsonPropertyName("allArchs")]
    public bool AllArchs { get; set; }

    /// <summary>
    ///     Download all language-specific depots.
    /// </summary>
    [JsonPropertyName("allLanguages")]
    public bool AllLanguages { get; set; }

    /// <summary>
    ///     Include low-violence depots.
    /// </summary>
    [JsonPropertyName("lowViolence")]
    public bool LowViolence { get; set; }

    /// <summary>
    ///     Installation directory.
    /// </summary>
    [JsonPropertyName("dir")]
    public string InstallDirectory { get; set; }

    /// <summary>
    ///     Path to filelist for filtering downloads.
    /// </summary>
    [JsonPropertyName("filelist")]
    public string FileList { get; set; }

    /// <summary>
    ///     Verify all existing files.
    /// </summary>
    [JsonPropertyName("validate")]
    public bool Validate { get; set; }

    /// <summary>
    ///     Download manifest metadata only.
    /// </summary>
    [JsonPropertyName("manifestOnly")]
    public bool ManifestOnly { get; set; }

    /// <summary>
    ///     Maximum concurrent downloads.
    /// </summary>
    [JsonPropertyName("maxDownloads")]
    public int? MaxDownloads { get; set; }

    /// <summary>
    ///     Steam Cell ID override.
    /// </summary>
    [JsonPropertyName("cellId")]
    public int? CellId { get; set; }

    /// <summary>
    ///     Login ID for concurrent instances.
    /// </summary>
    [JsonPropertyName("loginId")]
    public uint? LoginId { get; set; }

    /// <summary>
    ///     Use Lancache for downloads.
    /// </summary>
    [JsonPropertyName("useLancache")]
    public bool UseLancache { get; set; }

    /// <summary>
    ///     Skip disk space verification.
    /// </summary>
    [JsonPropertyName("skipDiskCheck")]
    public bool SkipDiskCheck { get; set; }

    /// <summary>
    ///     Enable debug logging.
    /// </summary>
    [JsonPropertyName("debug")]
    public bool Debug { get; set; }
}

/// <summary>
///     Source-generated JSON serialization context for config file.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ConfigFile))]
internal partial class ConfigFileContext : JsonSerializerContext;
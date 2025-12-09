namespace DepotDownloader.Lib;

/// <summary>
///     Public constants for Steam content downloading.
/// </summary>
public static class SteamConstants
{
    /// <summary>
    ///     Sentinel value representing an invalid app ID.
    /// </summary>
    public const uint InvalidAppId = uint.MaxValue;

    /// <summary>
    ///     Sentinel value representing an invalid manifest ID.
    /// </summary>
    public const ulong InvalidManifestId = ulong.MaxValue;

    /// <summary>
    ///     The default branch name for Steam apps.
    /// </summary>
    public const string DefaultBranch = "public";
}
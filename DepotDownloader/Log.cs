using Microsoft.Extensions.Logging;
using SteamKit2;

namespace DepotDownloader.Lib;

/// <summary>
///     Source-generated logging methods for performance optimization.
///     Using LoggerMessage source generators avoids string interpolation when logging is disabled.
/// </summary>
internal static partial class Log
{
    // ContentDownloader logging
    [LoggerMessage(Level = LogLevel.Debug, Message = "Initializing Steam3 session for user: {Username}")]
    public static partial void InitializingSteam3(this ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get Steam3 credentials")]
    public static partial void FailedToGetCredentials(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Steam3 session initialized successfully")]
    public static partial void Steam3Initialized(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shutting down Steam3 session")]
    public static partial void ShuttingDownSteam3(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Disposing ContentDownloader")]
    public static partial void DisposingContentDownloader(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Starting download for app {AppId}, branch: {Branch}, depots: {DepotCount}")]
    public static partial void StartingDownload(this ILogger logger, uint appId, string branch, int depotCount);

    // Steam3Session logging
    [LoggerMessage(Level = LogLevel.Debug, Message = "Creating Steam3Session, authenticated user: {IsAuthenticated}")]
    public static partial void CreatingSteam3Session(this ILogger logger, bool isAuthenticated);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Requesting app info for {AppId}")]
    public static partial void RequestingAppInfo(this ILogger logger, uint appId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Access token denied for app {AppId}")]
    public static partial void AccessTokenDenied(this ILogger logger, uint appId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Got AppInfo for {AppId}")]
    public static partial void GotAppInfo(this ILogger logger, uint appId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Requesting depot key for depot {DepotId}, app {AppId}")]
    public static partial void RequestingDepotKey(this ILogger logger, uint depotId, uint appId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Got depot key for {DepotId}, result: {Result}")]
    public static partial void GotDepotKey(this ILogger logger, uint depotId, EResult result);

    [LoggerMessage(Level = LogLevel.Debug, Message = "LogOn callback received, result: {Result}")]
    public static partial void LogOnCallback(this ILogger logger, EResult result);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get license list: {Result}")]
    public static partial void FailedToGetLicenseList(this ILogger logger, EResult result);

    [LoggerMessage(Level = LogLevel.Information, Message = "Got {Count} licenses for account")]
    public static partial void GotLicenses(this ILogger logger, int count);

    // DepotDownloaderClient logging
    [LoggerMessage(Level = LogLevel.Debug, Message = "Initializing DepotDownloaderClient")]
    public static partial void InitializingClient(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "DepotDownloaderClient initialized")]
    public static partial void ClientInitialized(this ILogger logger);
}
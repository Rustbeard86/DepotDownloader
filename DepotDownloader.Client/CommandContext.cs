using DepotDownloader.Lib;

namespace DepotDownloader.Client;

/// <summary>
///     Shared context for all commands.
/// </summary>
public sealed record CommandContext(
    DepotDownloaderClient Client,
    IUserInterface UserInterface,
    ArgumentParser Args,
    ConfigFile Config,
    bool JsonOutput);
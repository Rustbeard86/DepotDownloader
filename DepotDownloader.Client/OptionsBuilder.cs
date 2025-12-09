using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DepotDownloader.Lib;

namespace DepotDownloader.Client;

/// <summary>
///     Builds DepotDownloadOptions from arguments and config with CLI override precedence.
/// </summary>
internal sealed class OptionsBuilder(ArgumentParser args, ConfigFile config, IUserInterface ui)
{
    public async Task<DepotDownloadOptions> BuildAsync(uint appId)
    {
        var options = new DepotDownloadOptions
        {
            AppId = appId,
            DownloadManifestOnly = args.Has("-manifest-only") || (config?.ManifestOnly ?? false),
            InstallDirectory = args.Get<string>(null, "-dir") ?? config?.InstallDirectory,
            VerifyAll = args.Has("-verify-all", "-verify_all", "-validate") || (config?.Validate ?? false),
            MaxDownloads = args.Get(config?.MaxDownloads ?? 8, "-max-downloads"),
            LoginId = args.Has("-loginid") ? args.Get<uint>(0, "-loginid") : config?.LoginId,
            VerifyDiskSpace = !args.Has("-skip-disk-check", "--skip-disk-check") && !(config?.SkipDiskCheck ?? false)
        };

        ConfigureSpeed(options);
        ConfigureRetry(options);
        ConfigureCell(options);
        await ConfigureLancacheAsync(options);
        await ConfigureFileListAsync(options);
        ConfigureBranch(options);
        ConfigurePlatform(options);
        ConfigureArchitecture(options);
        ConfigureLanguage(options);
        ConfigureDepots(options);

        options.LowViolence = args.Has("-lowviolence") || (config?.LowViolence ?? false);

        return options;
    }

    private void ConfigureSpeed(DepotDownloadOptions options)
    {
        var maxSpeed = args.Get<double?>(null, "-max-speed");
        if (maxSpeed.HasValue)
        {
            options.MaxBytesPerSecond = maxSpeed.Value <= 0 ? null : (long)(maxSpeed.Value * 1024 * 1024);
            ui.WriteLine(maxSpeed.Value <= 0 ? "Speed limit: unlimited" : $"Speed limit: {maxSpeed.Value:F1} MB/s");
        }
    }

    private void ConfigureRetry(DepotDownloadOptions options)
    {
        var maxRetries = args.Get(-1, "-retries");
        if (maxRetries >= 0)
        {
            options.RetryPolicy = maxRetries == 0 ? RetryPolicy.None : RetryPolicy.Create(maxRetries);
            ui.WriteLine("Max retries: {0}", maxRetries);
        }
    }

    private void ConfigureCell(DepotDownloadOptions options)
    {
        var cellId = args.Get(config?.CellId ?? -1, "-cellid");
        options.CellId = cellId == -1 ? 0 : cellId;
    }

    private async Task ConfigureLancacheAsync(DepotDownloadOptions options)
    {
        if (!(args.Has("-use-lancache") || (config?.UseLancache ?? false)))
            return;

        await SteamKit2.CDN.Client.DetectLancacheServerAsync();
        if (!SteamKit2.CDN.Client.UseLancacheServer)
            return;

        ui.WriteLine("Detected Lancache server! Downloads will be directed through the Lancache.");
        if (!args.Has("-max-downloads") && config?.MaxDownloads is null)
            options.MaxDownloads = 25;
    }

    private async Task ConfigureFileListAsync(DepotDownloadOptions options)
    {
        var fileList = args.Get<string>(null, "-filelist") ?? config?.FileList;
        if (fileList is null)
            return;

        const string regexPrefix = "regex:";
        try
        {
            options.FilesToDownload = [];
            options.FilesToDownloadRegex = [];

            var files = await File.ReadAllLinesAsync(fileList);
            foreach (var entry in files)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                if (entry.StartsWith(regexPrefix))
                    options.FilesToDownloadRegex.Add(new Regex(entry[regexPrefix.Length..],
                        RegexOptions.Compiled | RegexOptions.IgnoreCase));
                else
                    options.FilesToDownload.Add(entry.Replace('\\', '/'));
            }

            ui.WriteLine("Using filelist: '{0}'.", fileList);
        }
        catch (Exception ex)
        {
            ui.WriteLine("Warning: Unable to load filelist: {0}", ex);
        }
    }

    private void ConfigureBranch(DepotDownloadOptions options)
    {
        options.Branch = args.Get<string>(null, "-branch", "-beta") ?? config?.Branch ?? SteamConstants.DefaultBranch;
        options.BranchPassword = args.Get<string>(null, "-branchpassword", "-betapassword") ?? config?.BranchPassword;

        if (!string.IsNullOrEmpty(options.BranchPassword) && string.IsNullOrEmpty(options.Branch))
            throw new ArgumentException("Cannot specify -branchpassword when -branch is not specified.");
    }

    private void ConfigurePlatform(DepotDownloadOptions options)
    {
        options.DownloadAllPlatforms = args.Has("-all-platforms") || (config?.AllPlatforms ?? false);
        options.Os = args.Get<string>(null, "-os") ?? config?.Os;

        if (options.DownloadAllPlatforms && !string.IsNullOrEmpty(options.Os))
            throw new ArgumentException("Cannot specify -os when -all-platforms is specified.");
    }

    private void ConfigureArchitecture(DepotDownloadOptions options)
    {
        options.DownloadAllArchs = args.Has("-all-archs") || (config?.AllArchs ?? false);
        options.Architecture = args.Get<string>(null, "-osarch") ?? config?.OsArch;

        if (options.DownloadAllArchs && !string.IsNullOrEmpty(options.Architecture))
            throw new ArgumentException("Cannot specify -osarch when -all-archs is specified.");
    }

    private void ConfigureLanguage(DepotDownloadOptions options)
    {
        options.DownloadAllLanguages = args.Has("-all-languages") || (config?.AllLanguages ?? false);
        options.Language = args.Get<string>(null, "-language") ?? config?.Language;

        if (options.DownloadAllLanguages && !string.IsNullOrEmpty(options.Language))
            throw new ArgumentException("Cannot specify -language when -all-languages is specified.");
    }

    private void ConfigureDepots(DepotDownloadOptions options)
    {
        var depotIds = args.GetList<uint>("-depot");
        var manifestIds = args.GetList<ulong>("-manifest");

        // Use config if CLI didn't specify
        if (depotIds.Count == 0 && config?.Depots is { Count: > 0 })
        {
            depotIds = config.Depots;
            manifestIds = config.Manifests ?? [];
        }

        if (manifestIds.Count > 0 && depotIds.Count != manifestIds.Count)
            throw new ArgumentException("-manifest requires one id for every -depot specified");

        options.DepotManifestIds = manifestIds.Count > 0
            ? [.. depotIds.Zip(manifestIds, (d, m) => (d, m))]
            : [.. depotIds.Select(d => (d, SteamConstants.InvalidManifestId))];
    }
}
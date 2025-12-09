using System;
using System.Linq;
using System.Threading.Tasks;
using DepotDownloader.Lib;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Downloads Steam application content, workshop items, or UGC content.
/// </summary>
/// <remarks>
///     The main download command supports downloading entire apps, specific depots,
///     workshop published files, and UGC content. Includes automatic resume support,
///     speed limiting, and concurrent chunk downloads.
/// </remarks>
[Command("download", "Download app content, workshop items, or UGC",
    Aliases = ["get", "fetch"],
    Examples =
    [
        "depotdownloader -app 730",
        "depotdownloader -app 730 -depot 731 -dir \"C:\\Games\\CS2\"",
        "depotdownloader -app 730 -pubfile 1885082371",
        "depotdownloader -app 730 -branch beta -username myaccount"
    ])]
[CommandParameter("-app", "Steam AppID", Required = true, Example = "730")]
[CommandParameter("-depot", "Specific depot ID (optional)")]
[CommandParameter("-manifest", "Specific manifest ID (requires -depot)")]
[CommandParameter("-branch", "Branch name (default: public)", Example = "beta")]
[CommandParameter("-dir", "Installation directory", Example = "C:\\Games\\CS2")]
[CommandParameter("-pubfile", "Workshop PublishedFileId")]
[CommandParameter("-ugc", "UGC ID")]
[CommandParameter("-max-speed", "Max download speed in MB/s (0=unlimited)", Example = "10")]
[CommandParameter("-no-progress", "Disable progress bar")]
internal sealed class DownloadCommand(
    DepotDownloadOptions options,
    ulong pubFile,
    ulong ugcId,
    bool noProgress) : ICommand
{
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            ProgressBar progressBar = null;
            var downloadStartTime = DateTime.UtcNow;

            if (!context.JsonOutput && !noProgress && !Console.IsOutputRedirected)
            {
                progressBar = new ProgressBar();
                context.Client.DownloadProgress += (_, e) =>
                {
                    progressBar.Update(e.BytesDownloaded, e.TotalBytes, e.SpeedBytesPerSecond,
                        e.EstimatedTimeRemaining, e.FilesCompleted, e.TotalFiles);
                };
            }

            DownloadResult result = null;

            if (pubFile != SteamConstants.InvalidManifestId)
                await context.Client.DownloadPublishedFileAsync(options.AppId, pubFile);
            else if (ugcId != SteamConstants.InvalidManifestId)
                await context.Client.DownloadUgcAsync(options.AppId, ugcId);
            else
                result = await context.Client.DownloadAppAsync(options);

            progressBar?.Complete();

            if (result is not null && result.FailedDepots > 0)
            {
                var duration = DateTime.UtcNow - downloadStartTime;
                if (context.JsonOutput)
                {
                    WriteJsonPartialSuccess(options.AppId, duration, result);
                }
                else
                {
                    var ui = context.UserInterface;
                    ui.WriteLine();
                    ui.WriteError("Download completed with errors:");
                    ui.WriteError("  Successful depots: {0}", result.SuccessfulDepots);
                    ui.WriteError("  Failed depots:     {0}", result.FailedDepots);

                    foreach (var failure in result.Failures)
                        ui.WriteError("    Depot {0}: {1}", failure.DepotId, failure.ErrorMessage);
                }

                return result.AllFailed ? 1 : 2;
            }

            if (context.JsonOutput)
            {
                var duration = DateTime.UtcNow - downloadStartTime;
                WriteJsonSuccess(options.AppId, duration, result);
            }

            return 0;
        }
        catch (InsufficientDiskSpaceException ex)
        {
            if (context.JsonOutput)
            {
                JsonOutput.WriteError(
                    $"Insufficient disk space on {ex.TargetDrive}. Required: {ex.RequiredBytes}, Available: {ex.AvailableBytes}");
            }
            else
            {
                var ui = context.UserInterface;
                ui.WriteLine();
                ui.WriteError("Error: Insufficient disk space!");
                ui.WriteError("  Drive:      {0}", ex.TargetDrive);
                ui.WriteError("  Required:   {0}", Formatter.Size(ex.RequiredBytes));
                ui.WriteError("  Available:  {0}", Formatter.Size(ex.AvailableBytes));
                ui.WriteError("  Shortfall:  {0}", Formatter.Size(ex.ShortfallBytes));
                ui.WriteLine();
                ui.WriteLine("Free up disk space or use -skip-disk-check to bypass this check.");
            }

            return 1;
        }
        catch (ContentDownloaderException ex)
        {
            if (context.JsonOutput)
                JsonOutput.WriteError(ex.Message);
            else
                context.UserInterface.WriteLine(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            if (context.JsonOutput)
                JsonOutput.WriteError("Download was cancelled");
            else
                context.UserInterface.WriteLine("Download was cancelled.");
            return 1;
        }
        catch (Exception e)
        {
            if (context.JsonOutput)
                JsonOutput.WriteError($"Download failed: {e.Message}");
            else
                context.UserInterface.WriteLine("Download failed due to an unhandled exception: {0}", e.Message);
            throw;
        }
    }

    private static void WriteJsonSuccess(uint appId, TimeSpan duration, DownloadResult result)
    {
        JsonOutput.WriteSuccess(new DownloadResultJson
        {
            AppId = appId,
            DurationSeconds = duration.TotalSeconds,
            TotalBytesDownloaded = result?.TotalBytesDownloaded ?? 0,
            TotalFilesDownloaded = result?.TotalFilesDownloaded ?? 0,
            SuccessfulDepots = result?.SuccessfulDepots ?? 0,
            FailedDepots = result?.FailedDepots ?? 0
        });
    }

    private static void WriteJsonPartialSuccess(uint appId, TimeSpan duration, DownloadResult result)
    {
        JsonOutput.WritePartialSuccess(new DownloadResultJson
        {
            AppId = appId,
            DurationSeconds = duration.TotalSeconds,
            TotalBytesDownloaded = result.TotalBytesDownloaded,
            TotalFilesDownloaded = result.TotalFilesDownloaded,
            SuccessfulDepots = result.SuccessfulDepots,
            FailedDepots = result.FailedDepots,
            Failures =
            [
                .. result.Failures.Select(f => new DepotFailureJson
                {
                    DepotId = f.DepotId,
                    ErrorMessage = f.ErrorMessage
                })
            ]
        });
    }
}
using System;
using System.Linq;
using System.Threading.Tasks;
using DepotDownloader.Lib;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Shows what would be downloaded without actually downloading files.
/// </summary>
/// <remarks>
///     Dry-run mode analyzes the download plan and shows file counts, sizes, and
///     estimated download times at various speeds. Use -verbose to see individual files.
/// </remarks>
[Command("dry-run", "Show what would be downloaded without downloading",
    Aliases = ["plan", "preview"],
    Examples =
    [
        "depotdownloader -app 730 -dry-run",
        "depotdownloader -app 730 -depot 731 -dry-run -verbose"
    ])]
[CommandParameter("-app", "Steam AppID", Required = true, Example = "730")]
[CommandParameter("-depot", "Specific depot ID (optional)")]
[CommandParameter("-verbose", "Show detailed file list")]
[CommandParameter("-json", "Output results in JSON format")]
internal sealed class DryRunCommand(DepotDownloadOptions options, bool verbose) : ICommand
{
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            if (!context.JsonOutput)
            {
                context.UserInterface.WriteLine();
                context.UserInterface.WriteLine("Analyzing download plan (dry-run mode)...");
                context.UserInterface.WriteLine();
            }

            var plan = await context.Client.GetDownloadPlanAsync(options);

            if (context.JsonOutput)
            {
                JsonOutput.WriteDryRun(new DryRunResultJson
                {
                    AppId = plan.AppId,
                    AppName = plan.AppName,
                    TotalDepots = plan.Depots.Count,
                    TotalFiles = plan.TotalFileCount,
                    TotalBytes = plan.TotalDownloadSize,
                    TotalSize = Formatter.Size(plan.TotalDownloadSize),
                    Depots =
                    [
                        .. plan.Depots.Select(d => new DepotPlanJson
                        {
                            DepotId = d.DepotId,
                            ManifestId = d.ManifestId,
                            FileCount = d.Files.Count,
                            TotalBytes = d.TotalSize,
                            TotalSize = Formatter.Size(d.TotalSize),
                            Files = verbose
                                ?
                                [
                                    .. d.Files.Select(f => new FilePlanJson
                                    {
                                        FileName = f.FileName,
                                        Size = f.Size,
                                        Hash = f.Hash
                                    })
                                ]
                                : null
                        })
                    ]
                });
                return 0;
            }

            var ui = context.UserInterface;
            ui.WriteLine("Download Plan for {0} ({1}):", plan.AppName, plan.AppId);
            ui.WriteLine();

            if (plan.Depots.Count == 0)
            {
                ui.WriteLine("  No depots would be downloaded.");
                return 0;
            }

            foreach (var depot in plan.Depots)
            {
                ui.WriteLine("  Depot {0} (Manifest {1})", depot.DepotId, depot.ManifestId);
                ui.WriteLine("    Files: {0}", depot.Files.Count);
                ui.WriteLine("    Size:  {0}", Formatter.Size(depot.TotalSize));

                if (verbose && depot.Files.Count > 0)
                {
                    ui.WriteLine();
                    ui.WriteLine("    Files:");
                    foreach (var file in depot.Files.OrderBy(f => f.FileName).Take(50))
                    {
                        var fileName = file.FileName.Length > 58 ? "..." + file.FileName[^55..] : file.FileName;
                        ui.WriteLine("      {0,-60} {1,12} {2}", fileName, Formatter.Size(file.Size), file.Hash[..8]);
                    }

                    if (depot.Files.Count > 50)
                        ui.WriteLine("      ... and {0} more files", depot.Files.Count - 50);
                }

                ui.WriteLine();
            }

            ui.WriteLine(new string('-', 50));
            ui.WriteLine();
            ui.WriteLine("Summary:");
            ui.WriteLine("  Total depots:     {0}", plan.Depots.Count);
            ui.WriteLine("  Total files:      {0:N0}", plan.TotalFileCount);
            ui.WriteLine("  Total size:       {0}", Formatter.Size(plan.TotalDownloadSize));

            if (plan.TotalDownloadSize > 0)
            {
                ui.WriteLine();
                ui.WriteLine("Estimated download time:");
                ui.WriteLine("  At 10 MB/s:  {0}", Formatter.Duration(plan.TotalDownloadSize / (10 * 1024 * 1024)));
                ui.WriteLine("  At 50 MB/s:  {0}", Formatter.Duration(plan.TotalDownloadSize / (50 * 1024 * 1024)));
                ui.WriteLine("  At 100 MB/s: {0}", Formatter.Duration(plan.TotalDownloadSize / (100 * 1024 * 1024)));
            }

            ui.WriteLine();
            ui.WriteLine("To download, run the same command without --dry-run");

            return 0;
        }
        catch (ContentDownloaderException ex)
        {
            if (context.JsonOutput)
                JsonOutput.WriteError(ex.Message);
            else
                context.UserInterface.WriteLine("Error: {0}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            if (context.JsonOutput)
                JsonOutput.WriteError($"Error getting download plan: {ex.Message}");
            else
                context.UserInterface.WriteLine("Error getting download plan: {0}", ex.Message);
            return 1;
        }
    }
}
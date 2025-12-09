using System;
using System.Linq;
using System.Threading.Tasks;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Lists all available depots for a Steam application.
/// </summary>
/// <remarks>
///     This command queries Steam for all depot information associated with an app,
///     including platform-specific depots, language packs, and shared install depots.
///     The output includes depot IDs, names, target OS, architecture, language, and size estimates.
/// </remarks>
[Command("list-depots", "List all depots for the specified app",
    Aliases = ["depots", "list-depot"],
    Examples =
    [
        "depotdownloader -app 730 -list-depots",
        "depotdownloader -app 730 -list-depots -json"
    ])]
[CommandParameter("-app", "Steam AppID to query", Required = true, Example = "730")]
[CommandParameter("-json", "Output results in JSON format")]
internal sealed class ListDepotsCommand(uint appId) : ICommand
{
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            var appInfo = await context.Client.GetAppInfoAsync(appId);
            var depots = await context.Client.GetDepotsAsync(appId);

            if (context.JsonOutput)
            {
                JsonOutput.WriteDepots(new DepotsResultJson
                {
                    AppId = appId,
                    AppName = appInfo.Name,
                    AppType = appInfo.Type,
                    Depots =
                    [
                        .. depots.Select(d => new DepotJson
                        {
                            DepotId = d.DepotId,
                            Name = d.Name,
                            Os = d.Os,
                            Architecture = d.Architecture,
                            Language = d.Language,
                            MaxSize = d.MaxSize,
                            IsSharedInstall = d.IsSharedInstall
                        })
                    ]
                });
                return 0;
            }

            var ui = context.UserInterface;
            ui.WriteLine();
            ui.WriteLine("Depots for {0} ({1}) [Type: {2}]:", appInfo.Name, appInfo.AppId, appInfo.Type);
            ui.WriteLine();

            if (depots.Count == 0)
            {
                ui.WriteLine("  No depots found.");
                return 0;
            }

            ui.WriteLine("  {0,-10} {1,-40} {2,-15} {3,-6} {4,-10} {5}",
                "DepotID", "Name", "OS", "Arch", "Language", "Size");
            ui.WriteLine("  {0}", new string('-', 100));

            foreach (var depot in depots.OrderBy(d => d.DepotId))
            {
                var os = depot.Os ?? "all";
                var arch = depot.Architecture ?? "-";
                var lang = depot.Language ?? "-";
                var size = depot.MaxSize.HasValue ? Formatter.Size(depot.MaxSize.Value) : "-";
                var name = Formatter.Truncate(depot.Name ?? "(unnamed)", 40);
                var shared = depot.IsSharedInstall ? " [shared]" : "";

                ui.WriteLine("  {0,-10} {1,-40} {2,-15} {3,-6} {4,-10} {5}{6}",
                    depot.DepotId, name, os, arch, lang, size, shared);
            }

            ui.WriteLine();
            ui.WriteLine("Total: {0} depot(s)", depots.Count);
            return 0;
        }
        catch (Exception ex)
        {
            if (context.JsonOutput)
                JsonOutput.WriteError($"Error listing depots: {ex.Message}");
            else
                context.UserInterface.WriteLine("Error listing depots: {0}", ex.Message);
            return 1;
        }
    }
}
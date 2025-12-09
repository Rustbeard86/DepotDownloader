using System;
using System.Linq;
using System.Threading.Tasks;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Lists all available branches for a Steam application.
/// </summary>
/// <remarks>
///     This command displays all branches (public, beta, etc.) available for an app,
///     including build IDs, last update times, and password protection status.
/// </remarks>
[Command("list-branches", "List all branches for the specified app",
    Aliases = ["branches", "list-branch"],
    Examples =
    [
        "depotdownloader -app 730 -list-branches",
        "depotdownloader -app 730 -list-branches -json"
    ])]
[CommandParameter("-app", "Steam AppID to query", Required = true, Example = "730")]
[CommandParameter("-json", "Output results in JSON format")]
internal sealed class ListBranchesCommand(uint appId) : ICommand
{
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            var appInfo = await context.Client.GetAppInfoAsync(appId);
            var branches = await context.Client.GetBranchesAsync(appId);

            if (context.JsonOutput)
            {
                JsonOutput.WriteBranches(new BranchesResultJson
                {
                    AppId = appId,
                    AppName = appInfo.Name,
                    Branches =
                    [
                        .. branches.Select(b => new BranchJson
                        {
                            Name = b.Name,
                            BuildId = b.BuildId,
                            TimeUpdated = b.TimeUpdated,
                            IsPasswordProtected = b.IsPasswordProtected,
                            Description = b.Description
                        })
                    ]
                });
                return 0;
            }

            var ui = context.UserInterface;
            ui.WriteLine();
            ui.WriteLine("Branches for {0} ({1}):", appInfo.Name, appId);
            ui.WriteLine();

            if (branches.Count == 0)
            {
                ui.WriteLine("  No branches found.");
                return 0;
            }

            ui.WriteLine("  {0,-20} {1,-12} {2,-22} {3,-12} {4}",
                "Branch", "BuildID", "Updated", "Protected", "Description");
            ui.WriteLine("  {0}", new string('-', 90));

            foreach (var branch in branches.OrderBy(b => b.Name == "public" ? 0 : 1).ThenBy(b => b.Name))
            {
                var updated = branch.TimeUpdated?.ToString("yyyy-MM-dd HH:mm") ?? "-";
                var protection = branch.IsPasswordProtected ? "Yes" : "No";
                var description = Formatter.Truncate(branch.Description ?? "", 30);

                ui.WriteLine("  {0,-20} {1,-12} {2,-22} {3,-12} {4}",
                    branch.Name, branch.BuildId, updated, protection, description);
            }

            ui.WriteLine();
            ui.WriteLine("Total: {0} branch(es)", branches.Count);
            return 0;
        }
        catch (Exception ex)
        {
            if (context.JsonOutput)
                JsonOutput.WriteError($"Error listing branches: {ex.Message}");
            else
                context.UserInterface.WriteLine("Error listing branches: {0}", ex.Message);
            return 1;
        }
    }
}
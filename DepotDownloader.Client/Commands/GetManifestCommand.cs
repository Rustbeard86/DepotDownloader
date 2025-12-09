using System;
using System.Threading.Tasks;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Retrieves the latest manifest ID for a specific depot.
/// </summary>
/// <remarks>
///     Manifest IDs are version identifiers for depot content. This command is useful
///     for pinning downloads to specific versions or investigating depot history.
/// </remarks>
[Command("get-manifest", "Get the latest manifest ID for a depot",
    Aliases = ["manifest", "show-manifest"],
    Examples =
    [
        "depotdownloader -app 730 -depot 731 -get-manifest",
        "depotdownloader -app 730 -depot 731 -branch beta -get-manifest"
    ])]
[CommandParameter("-app", "Steam AppID", Required = true, Example = "730")]
[CommandParameter("-depot", "Depot ID to query", Required = true, Example = "731")]
[CommandParameter("-branch", "Branch name (default: public)", Example = "beta")]
[CommandParameter("-branchpassword", "Password for protected branches")]
internal sealed class GetManifestCommand(uint appId, uint depotId, string branch, string branchPassword) : ICommand
{
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            if (depotId == 0)
            {
                var errorMsg = "Depot ID must be specified with -depot when using -get-manifest";
                if (context.JsonOutput)
                    JsonOutput.WriteError(errorMsg);
                else
                    context.UserInterface.WriteError("Error: {0}", errorMsg);
                return 1;
            }

            var manifestId = await context.Client.GetLatestManifestIdAsync(appId, depotId, branch, branchPassword);

            if (context.JsonOutput)
            {
                JsonOutput.WriteManifest(new ManifestResultJson
                {
                    AppId = appId,
                    DepotId = depotId,
                    Branch = branch,
                    ManifestId = manifestId,
                    Found = manifestId.HasValue
                });
                return manifestId.HasValue ? 0 : 1;
            }

            var ui = context.UserInterface;
            if (manifestId.HasValue)
            {
                ui.WriteLine();
                ui.WriteLine("Latest manifest for app {0}, depot {1}, branch '{2}':", appId, depotId, branch);
                ui.WriteLine("  Manifest ID: {0}", manifestId.Value);
                ui.WriteLine();
                ui.WriteLine("To download this specific manifest:");
                ui.WriteLine("  depotdownloader -app {0} -depot {1} -manifest {2}", appId, depotId, manifestId.Value);
            }
            else
            {
                ui.WriteError("No manifest found for app {0}, depot {1}, branch '{2}'", appId, depotId, branch);
                ui.WriteError("This could mean:");
                ui.WriteError("  - The depot doesn't exist");
                ui.WriteError("  - The branch doesn't exist or requires a password");
                ui.WriteError("  - Your account doesn't have access to this depot");
            }

            return manifestId.HasValue ? 0 : 1;
        }
        catch (Exception ex)
        {
            if (context.JsonOutput)
                JsonOutput.WriteError($"Error getting manifest: {ex.Message}");
            else
                context.UserInterface.WriteError("Error getting manifest: {0}", ex.Message);
            return 1;
        }
    }
}
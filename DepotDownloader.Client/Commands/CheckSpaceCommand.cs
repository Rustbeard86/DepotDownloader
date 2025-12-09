using System;
using System.IO;
using System.Threading.Tasks;
using DepotDownloader.Lib;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Checks if sufficient disk space is available before downloading.
/// </summary>
/// <remarks>
///     This command calculates the total space required for the download without
///     actually downloading any files. Useful for planning downloads or validating
///     available storage before starting large downloads.
/// </remarks>
[Command("check-space", "Check required disk space without downloading",
    Aliases = ["space", "disk-space"],
    Examples =
    [
        "depotdownloader -app 730 -check-space",
        "depotdownloader -app 730 -depot 731 -check-space -dir \"C:\\Games\""
    ])]
[CommandParameter("-app", "Steam AppID", Required = true, Example = "730")]
[CommandParameter("-depot", "Specific depot ID (optional)")]
[CommandParameter("-dir", "Target directory path", Example = "C:\\Games\\CS2")]
internal sealed class CheckSpaceCommand(DepotDownloadOptions options) : ICommand
{
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            if (!context.JsonOutput)
            {
                context.UserInterface.WriteLine();
                context.UserInterface.WriteLine("Checking required disk space...");
            }

            var requiredBytes = await context.Client.GetRequiredDiskSpaceAsync(options);
            var targetPath = options.InstallDirectory ?? Environment.CurrentDirectory;
            var fullPath = Path.GetFullPath(targetPath);
            var root = Path.GetPathRoot(fullPath) ?? fullPath;

            ulong availableBytes = 0;
            try
            {
                var driveInfo = new DriveInfo(root);
                availableBytes = (ulong)driveInfo.AvailableFreeSpace;
            }
            catch
            {
                // Ignore drive info errors
            }

            var hasSufficientSpace = availableBytes >= requiredBytes;

            if (context.JsonOutput)
            {
                JsonOutput.WriteSpaceCheck(new SpaceCheckResultJson
                {
                    AppId = options.AppId,
                    RequiredBytes = requiredBytes,
                    RequiredSize = Formatter.Size(requiredBytes),
                    AvailableBytes = availableBytes,
                    AvailableSize = Formatter.Size(availableBytes),
                    TargetDrive = root,
                    HasSufficientSpace = hasSufficientSpace
                });
                return hasSufficientSpace ? 0 : 1;
            }

            var ui = context.UserInterface;
            ui.WriteLine();
            ui.WriteLine("Disk Space Check for app {0}:", options.AppId);
            ui.WriteLine("  Target:     {0}", fullPath);
            ui.WriteLine("  Drive:      {0}", root);
            ui.WriteLine("  Required:   {0}", Formatter.Size(requiredBytes));
            ui.WriteLine("  Available:  {0}", Formatter.Size(availableBytes));
            ui.WriteLine();

            if (hasSufficientSpace)
            {
                ui.WriteLine("✓ Sufficient disk space available.");
                return 0;
            }

            ui.WriteError("✗ Insufficient disk space!");
            ui.WriteError("  Shortfall: {0}", Formatter.Size(requiredBytes - availableBytes));
            return 1;
        }
        catch (Exception ex)
        {
            if (context.JsonOutput)
                JsonOutput.WriteError($"Error checking disk space: {ex.Message}");
            else
                context.UserInterface.WriteError("Error checking disk space: {0}", ex.Message);
            return 1;
        }
    }
}
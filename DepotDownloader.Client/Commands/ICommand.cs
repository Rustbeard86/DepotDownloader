using System.Threading.Tasks;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Base interface for all command handlers.
/// </summary>
internal interface ICommand
{
    Task<int> ExecuteAsync(CommandContext context);
}
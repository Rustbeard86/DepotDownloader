using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using DepotDownloader.Lib;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Auto-generates help text from command attributes and XML documentation.
/// </summary>
public static class HelpGenerator
{
    private static readonly Dictionary<string, Type> CommandRegistry = [];
    private static readonly Dictionary<string, string> CommandAliases = [];

    static HelpGenerator()
    {
        // Register all commands
        var commandTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.GetCustomAttribute<CommandAttribute>() != null);

        foreach (var type in commandTypes)
        {
            var attr = type.GetCustomAttribute<CommandAttribute>();
            if (attr == null) continue;

            CommandRegistry[attr.Name] = type;

            foreach (var alias in attr.Aliases)
                CommandAliases[alias] = attr.Name;
        }
    }

    /// <summary>
    ///     Gets the command type for a given name or alias.
    /// </summary>
    public static Type GetCommandType(string name)
    {
        if (CommandRegistry.TryGetValue(name, out var type))
            return type;

        if (CommandAliases.TryGetValue(name, out var primaryName))
            return CommandRegistry[primaryName];

        return null;
    }

    /// <summary>
    ///     Checks if a command name or alias exists.
    /// </summary>
    public static bool IsValidCommand(string name)
    {
        return CommandRegistry.ContainsKey(name) || CommandAliases.ContainsKey(name);
    }

    /// <summary>
    ///     Generates complete help text with all commands.
    /// </summary>
    public static void PrintFullHelp(IUserInterface ui)
    {
        ui.WriteLine();
        ui.WriteLine("=== DepotDownloader Commands ===");
        ui.WriteLine();

        PrintQuickStart(ui);
        ui.WriteLine();

        PrintCommands(ui);
        ui.WriteLine();

        PrintGlobalParameters(ui);
        ui.WriteLine();

        PrintExamples(ui);
    }

    /// <summary>
    ///     Generates help text for a specific command.
    /// </summary>
    public static void PrintCommandHelp(IUserInterface ui, string commandName)
    {
        var type = GetCommandType(commandName);
        if (type == null)
        {
            ui.WriteError("Unknown command: {0}", commandName);
            ui.WriteLine("Use 'depotdownloader --help' to see all available commands.");
            return;
        }

        var attr = type.GetCustomAttribute<CommandAttribute>();
        if (attr == null)
        {
            ui.WriteError("Command {0} is missing CommandAttribute metadata.", commandName);
            return;
        }

        ui.WriteLine();
        ui.WriteLine("Command: {0}", attr.Name);
        if (attr.Aliases.Length > 0)
            ui.WriteLine("Aliases: {0}", string.Join(", ", attr.Aliases));

        ui.WriteLine();
        ui.WriteLine("Description:");
        ui.WriteLine("  {0}", attr.Description);

        var remarks = GetXmlRemarks(type);
        if (!string.IsNullOrEmpty(remarks))
        {
            ui.WriteLine();
            ui.WriteLine("Details:");
            foreach (var line in remarks.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                ui.WriteLine("  {0}", line.Trim());
        }

        var parameters = type.GetCustomAttributes<CommandParameterAttribute>().ToArray();
        if (parameters.Length > 0)
        {
            ui.WriteLine();
            ui.WriteLine("Parameters:");
            foreach (var param in parameters)
            {
                var required = param.Required ? " (required)" : "";
                var aliases = param.Aliases.Length > 0 ? $" [{string.Join(", ", param.Aliases)}]" : "";
                ui.WriteLine("  {0}{1}{2}", param.Name, aliases, required);
                ui.WriteLine("    {0}", param.Description);
                if (!string.IsNullOrEmpty(param.Example))
                    ui.WriteLine("    Example: {0}", param.Example);
            }
        }

        if (attr.Examples.Length > 0)
        {
            ui.WriteLine();
            ui.WriteLine("Examples:");
            foreach (var example in attr.Examples)
                ui.WriteLine("  {0}", example);
        }

        ui.WriteLine();
    }

    private static void PrintQuickStart(IUserInterface ui)
    {
        ui.WriteLine("Quick Start:");
        ui.WriteLine("  depotdownloader -app <appid>                    # Download an app");
        ui.WriteLine("  depotdownloader -app <appid> -list-depots       # List available depots");
        ui.WriteLine("  depotdownloader -app <appid> -list-branches     # List available branches");
        ui.WriteLine("  depotdownloader --help <command>                # Get help for a command");
    }

    private static void PrintCommands(IUserInterface ui)
    {
        ui.WriteLine("Available Commands:");

        var commands = CommandRegistry
            .Select(kvp => (Name: kvp.Key, Attr: kvp.Value.GetCustomAttribute<CommandAttribute>()))
            .Where(x => x.Attr != null) // Protect against null attributes
            .OrderBy(x => x.Name)
            .ToList();

        foreach (var (name, attr) in commands)
        {
            var aliases = attr.Aliases.Length > 0 ? $" (aliases: {string.Join(", ", attr.Aliases)})" : "";
            ui.WriteLine("  {0,-20} {1}{2}", name, attr.Description, aliases);
        }
    }

    private static void PrintGlobalParameters(IUserInterface ui)
    {
        ui.WriteLine("Global Parameters:");
        ui.WriteLine("  -app <#>                 Steam AppID (required for most commands)");
        ui.WriteLine("  -username <user>         Steam account username");
        ui.WriteLine("  -password <pass>         Steam account password");
        ui.WriteLine("  -qr                      Login with QR code");
        ui.WriteLine("  -remember-password       Save credentials for future use");
        ui.WriteLine("  -json                    Output results in JSON format");
        ui.WriteLine("  -debug                   Enable debug logging");
        ui.WriteLine("  -config <file>           Load settings from JSON file");
        ui.WriteLine("  -V, --version            Show version information");
        ui.WriteLine("  --help [command]         Show this help or help for a specific command");
    }

    private static void PrintExamples(IUserInterface ui)
    {
        ui.WriteLine("Common Examples:");
        ui.WriteLine("  # Download Counter-Strike 2");
        ui.WriteLine("  depotdownloader -app 730 -username myaccount");
        ui.WriteLine();
        ui.WriteLine("  # Download specific depot from beta branch");
        ui.WriteLine("  depotdownloader -app 730 -depot 731 -branch beta");
        ui.WriteLine();
        ui.WriteLine("  # Check what would be downloaded");
        ui.WriteLine("  depotdownloader -app 730 -dry-run");
        ui.WriteLine();
        ui.WriteLine("  # Get manifest ID for a depot");
        ui.WriteLine("  depotdownloader -app 730 -depot 731 -get-manifest");
        ui.WriteLine();
        ui.WriteLine("  # Download workshop item");
        ui.WriteLine("  depotdownloader -app 730 -pubfile 1885082371");
        ui.WriteLine();
        ui.WriteLine("For more information, visit: https://github.com/Rustbeard86/DepotDownloader");
    }

    private static string GetXmlRemarks(Type type)
    {
        // In a real implementation, you would parse XML documentation
        // For now, we'll extract from the XML summary attribute if available
        var xmlSummary = type.GetCustomAttribute<DescriptionAttribute>();
        return xmlSummary?.Description ?? string.Empty;
        // TODO: Implement this shit
    }
}
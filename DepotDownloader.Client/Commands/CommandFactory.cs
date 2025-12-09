using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Factory for creating command instances with alias support.
/// </summary>
public static class CommandFactory
{
    private static readonly Dictionary<string, Type> CommandRegistry = [];
    private static readonly Dictionary<string, string> AliasMap = [];

    static CommandFactory()
    {
        RegisterCommands();
    }

    /// <summary>
    ///     Resolves a command name or alias to its primary name.
    /// </summary>
    public static string ResolveAlias(string nameOrAlias)
    {
        return CommandRegistry.ContainsKey(nameOrAlias) ? nameOrAlias : AliasMap.GetValueOrDefault(nameOrAlias);
    }

    /// <summary>
    ///     Checks if a command name or alias exists.
    /// </summary>
    public static bool IsValidCommand(string nameOrAlias)
    {
        return ResolveAlias(nameOrAlias) != null;
    }

    /// <summary>
    ///     Gets all registered command names.
    /// </summary>
    public static IEnumerable<string> GetAllCommandNames()
    {
        return CommandRegistry.Keys;
    }

    /// <summary>
    ///     Gets all aliases for a command.
    /// </summary>
    public static string[] GetAliases(string commandName)
    {
        return
        [
            .. AliasMap
                .Where(kvp => kvp.Value == commandName)
                .Select(kvp => kvp.Key)
        ];
    }

    private static void RegisterCommands()
    {
        var commandTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => !t.IsAbstract &&
                        !t.IsInterface &&
                        typeof(ICommand).IsAssignableFrom(t) &&
                        t.GetCustomAttribute<CommandAttribute>() != null);

        foreach (var type in commandTypes)
        {
            var attr = type.GetCustomAttribute<CommandAttribute>();
            if (attr == null) continue;

            CommandRegistry[attr.Name] = type;

            foreach (var alias in attr.Aliases)
            {
                if (AliasMap.TryGetValue(alias, out var value))
                    throw new InvalidOperationException(
                        $"Duplicate alias '{alias}' found for commands '{value}' and '{attr.Name}'");

                AliasMap[alias] = attr.Name;
            }
        }
    }
}
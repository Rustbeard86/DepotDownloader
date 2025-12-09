using System;

namespace DepotDownloader.Client.Commands;

/// <summary>
///     Metadata attribute for command registration and help generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class CommandAttribute(string name, string description) : Attribute
{
    /// <summary>
    ///     Primary command name (e.g., "list-depots").
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    ///     Command description shown in help text.
    /// </summary>
    public string Description { get; } = description;

    /// <summary>
    ///     Alternative names/aliases for this command.
    /// </summary>
    public string[] Aliases { get; init; } = [];

    /// <summary>
    ///     Example usage patterns.
    /// </summary>
    public string[] Examples { get; init; } = [];

    /// <summary>
    ///     Whether this command requires authentication.
    /// </summary>
    public bool RequiresAuth { get; init; } = true;
}

/// <summary>
///     Metadata for command parameters shown in help.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class CommandParameterAttribute(string name, string description) : Attribute
{
    /// <summary>
    ///     Parameter name (e.g., "-app").
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    ///     Parameter description.
    /// </summary>
    public string Description { get; } = description;

    /// <summary>
    ///     Whether this parameter is required.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    ///     Alternative parameter names.
    /// </summary>
    public string[] Aliases { get; init; } = [];

    /// <summary>
    ///     Example value.
    /// </summary>
    public string Example { get; init; }
}
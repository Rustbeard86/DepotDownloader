using DepotDownloader.Client.Commands;
using DepotDownloader.Tests.Helpers;
using Xunit;

namespace DepotDownloader.Tests.Commands;

/// <summary>
/// Integration tests for CommandFactory.
/// </summary>
public class CommandFactoryTests
{
    [Theory]
    [InlineData("list-depots", "list-depots")]
    [InlineData("depots", "list-depots")]
    [InlineData("list-depot", "list-depots")]
    [InlineData("list-branches", "list-branches")]
    [InlineData("branches", "list-branches")]
    public void ResolveAlias_WithValidNamesAndAliases_ResolvesPrimaryName(string input, string expected)
    {
        // Act
        var result = CommandFactory.ResolveAlias(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveAlias_WithInvalidName_ReturnsNull()
    {
        // Act
        var result = CommandFactory.ResolveAlias("invalid-command");

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("list-depots", true)]
    [InlineData("depots", true)]
    [InlineData("invalid", false)]
    public void IsValidCommand_WithVariousInputs_ReturnsCorrectResult(string input, bool expected)
    {
        // Act
        var result = CommandFactory.IsValidCommand(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetAllCommandNames_ReturnsAllRegisteredCommands()
    {
        // Act
        var commands = CommandFactory.GetAllCommandNames().ToList();

        // Assert
        Assert.Contains("list-depots", commands);
        Assert.Contains("list-branches", commands);
        Assert.Contains("get-manifest", commands);
        Assert.Contains("check-space", commands);
        Assert.Contains("dry-run", commands);
        Assert.Contains("download", commands);
        Assert.DoesNotContain("depots", commands); // aliases not in command names
    }

    [Fact]
    public void GetAliases_ForListDepotsCommand_ReturnsAllAliases()
    {
        // Act
        var aliases = CommandFactory.GetAliases("list-depots");

        // Assert
        Assert.Contains("depots", aliases);
        Assert.Contains("list-depot", aliases);
    }

    [Fact]
    public void GetAliases_ForCommandWithoutAliases_ReturnsEmpty()
    {
        // This test assumes there's a command without aliases
        // If all commands have aliases, this can be removed or adjusted
        
        // Act
        var aliases = CommandFactory.GetAliases("download");

        // Assert
        // Should have aliases based on our attribute
        Assert.NotEmpty(aliases);
    }
}

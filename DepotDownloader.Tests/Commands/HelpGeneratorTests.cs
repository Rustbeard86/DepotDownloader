using DepotDownloader.Client.Commands;
using DepotDownloader.Tests.Helpers;
using Xunit;

namespace DepotDownloader.Tests.Commands;

/// <summary>
/// Integration tests for the help generation system.
/// </summary>
public class HelpGeneratorTests
{
    [Fact]
    public void PrintFullHelp_ShouldOutputAllCommands()
    {
        // Arrange
        var ui = new TestUserInterface();

        // Act
        HelpGenerator.PrintFullHelp(ui);

        // Assert
        Assert.True(ui.ContainsMessage("Available Commands"));
        Assert.True(ui.ContainsMessage("list-depots"));
        Assert.True(ui.ContainsMessage("list-branches"));
        Assert.True(ui.ContainsMessage("get-manifest"));
        Assert.True(ui.ContainsMessage("check-space"));
        Assert.True(ui.ContainsMessage("dry-run"));
        Assert.True(ui.ContainsMessage("download"));
    }

    [Fact]
    public void PrintCommandHelp_WithValidCommand_ShouldOutputDetails()
    {
        // Arrange
        var ui = new TestUserInterface();

        // Act
        HelpGenerator.PrintCommandHelp(ui, "list-depots");

        // Assert
        Assert.True(ui.ContainsMessage("Command: list-depots"));
        Assert.True(ui.ContainsMessage("List all depots"));
        Assert.True(ui.ContainsMessage("Parameters:"));
        Assert.True(ui.ContainsMessage("-app"));
    }

    [Fact]
    public void PrintCommandHelp_WithAlias_ShouldResolveAndOutputDetails()
    {
        // Arrange
        var ui = new TestUserInterface();

        // Act
        HelpGenerator.PrintCommandHelp(ui, "depots"); // alias

        // Assert
        Assert.True(ui.ContainsMessage("Command: list-depots"));
    }

    [Fact]
    public void PrintCommandHelp_WithInvalidCommand_ShouldOutputError()
    {
        // Arrange
        var ui = new TestUserInterface();

        // Act
        HelpGenerator.PrintCommandHelp(ui, "invalid-command");

        // Assert
        Assert.True(ui.ContainsError("Unknown command"));
    }

    [Theory]
    [InlineData("list-depots", true)]
    [InlineData("depots", true)]
    [InlineData("list-branches", true)]
    [InlineData("branches", true)]
    [InlineData("invalid", false)]
    public void IsValidCommand_ShouldRecognizeCommandsAndAliases(string name, bool expected)
    {
        // Act
        var result = HelpGenerator.IsValidCommand(name);

        // Assert
        Assert.Equal(expected, result);
    }
}

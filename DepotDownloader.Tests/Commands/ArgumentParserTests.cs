using DepotDownloader.Client;
using DepotDownloader.Tests.Helpers;
using Xunit;

namespace DepotDownloader.Tests.Commands;

/// <summary>
/// Integration tests for ArgumentParser.
/// </summary>
public class ArgumentParserTests
{
    [Fact]
    public void Has_WithExistingParameter_ReturnsTrue()
    {
        // Arrange
        var parser = new ArgumentParser(["-app", "730", "-debug"]);

        // Act & Assert
        Assert.True(parser.Has("-debug"));
        Assert.True(parser.Has("-app"));
    }

    [Fact]
    public void Has_WithMissingParameter_ReturnsFalse()
    {
        // Arrange
        var parser = new ArgumentParser(["-app", "730"]);

        // Act & Assert
        Assert.False(parser.Has("-debug"));
    }

    [Fact]
    public void Has_WithMultipleAliases_ReturnsTrue()
    {
        // Arrange
        var parser = new ArgumentParser(["-username", "test"]);

        // Act & Assert
        Assert.True(parser.Has("-username", "-user"));
    }

    [Fact]
    public void Get_WithExistingParameter_ReturnsValue()
    {
        // Arrange
        var parser = new ArgumentParser(["-app", "730"]);

        // Act
        var result = parser.Get<uint>(0, "-app");

        // Assert
        Assert.Equal(730u, result);
    }

    [Fact]
    public void Get_WithMissingParameter_ReturnsDefault()
    {
        // Arrange
        var parser = new ArgumentParser(["-debug"]);

        // Act
        var result = parser.Get(999u, "-app");

        // Assert
        Assert.Equal(999u, result);
    }

    [Fact]
    public void GetList_WithMultipleValues_ReturnsAllValues()
    {
        // Arrange
        var parser = new ArgumentParser(["-depot", "731", "732", "733", "-app", "730"]);

        // Act
        var result = parser.GetList<uint>("-depot");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(731u, result[0]);
        Assert.Equal(732u, result[1]);
        Assert.Equal(733u, result[2]);
    }

    [Fact]
    public void GetList_StopsAtNextParameter()
    {
        // Arrange
        var parser = new ArgumentParser(["-depot", "731", "732", "-app", "730"]);

        // Act
        var result = parser.GetList<uint>("-depot");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(730u, result);
    }

    [Fact]
    public void HasUnconsumedArgs_WithUnusedArguments_ReturnsTrue()
    {
        // Arrange
        var parser = new ArgumentParser(["-app", "730", "-unknown", "value"]);
        _ = parser.Get<uint>(0, "-app");

        // Act & Assert
        Assert.True(parser.HasUnconsumedArgs());
    }

    [Fact]
    public void GetUnconsumedArgs_ReturnsCorrectArguments()
    {
        // Arrange
        var parser = new ArgumentParser(["-app", "730", "-unknown", "value"]);
        _ = parser.Get<uint>(0, "-app");

        // Act
        var unconsumed = parser.GetUnconsumedArgs().ToList();

        // Assert
        Assert.Contains("Argument #3 -unknown", unconsumed);
        Assert.Contains("Argument #4 value", unconsumed);
    }
}

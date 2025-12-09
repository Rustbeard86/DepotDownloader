using DepotDownloader.Client;
using DepotDownloader.Tests.Helpers;
using Xunit;

namespace DepotDownloader.Tests.Utilities;

/// <summary>
/// Tests for the Formatter utility class.
/// </summary>
public class FormatterTests
{
    [Theory]
    [InlineData(0ul, "0 B")]
    [InlineData(512ul, "512 B")]
    [InlineData(1024ul, "1 KB")]
    [InlineData(1536ul, "1.5 KB")]
    [InlineData(1048576ul, "1 MB")]
    [InlineData(1073741824ul, "1 GB")]
    [InlineData(1099511627776ul, "1 TB")]
    [InlineData(16234567890ul, "15.12 GB")]
    public void Size_WithVariousBytes_FormatsCorrectly(ulong bytes, string expected)
    {
        // Act
        var result = Formatter.Size(bytes);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0ul, "0 seconds")]
    [InlineData(30ul, "30 seconds")]
    [InlineData(60ul, "1 min 0 sec")]
    [InlineData(90ul, "1 min 30 sec")]
    [InlineData(3600ul, "1h 0m")]
    [InlineData(3665ul, "1h 1m")]
    [InlineData(7200ul, "2h 0m")]
    public void Duration_WithVariousSeconds_FormatsCorrectly(ulong seconds, string expected)
    {
        // Act
        var result = Formatter.Duration(seconds);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("exactly ten", 11, "exactly ten")]
    [InlineData("this is a very long string", 10, "this is...")]
    [InlineData("this is a very long string", 15, "this is a ve...")]
    public void Truncate_WithVariousInputs_TruncatesCorrectly(string input, int maxLength, string expected)
    {
        // Act
        var result = Formatter.Truncate(input, maxLength);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Truncate_WithCustomSuffix_UsesCustomSuffix()
    {
        // Arrange
        var input = "this is a long string";

        // Act
        var result = Formatter.Truncate(input, 10, ">>>");

        // Assert
        Assert.Equal("this is>>>", result);
    }
}

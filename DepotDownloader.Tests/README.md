# DepotDownloader Integration Tests

This project contains integration tests for the DepotDownloader CLI application.

## Test Structure

```
DepotDownloader.Tests/
??? Commands/               # Command-related tests
?   ??? HelpGeneratorTests.cs
?   ??? ArgumentParserTests.cs
?   ??? CommandFactoryTests.cs
??? Utilities/              # Utility class tests
?   ??? FormatterTests.cs
??? Helpers/                # Test infrastructure
    ??? TestUserInterface.cs
    ??? CommandContextBuilder.cs
```

## Running Tests

### Command Line
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet-coverage collect -f cobertura -o coverage.cobertura.xml dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~HelpGeneratorTests"
```

### Visual Studio
Use Test Explorer (Test > Test Explorer) to run and debug tests.

## Test Helpers

### TestUserInterface
Mock implementation of `IUserInterface` for capturing console output during tests:

```csharp
var ui = new TestUserInterface();
HelpGenerator.PrintFullHelp(ui);

Assert.True(ui.ContainsMessage("Available Commands"));
Assert.True(ui.ContainsError("Error message"));
```

### CommandContextBuilder
Fluent builder for creating test command contexts:

```csharp
var (context, ui) = new CommandContextBuilder()
    .WithArgs("-app", "730")
    .WithMockClient()
    .WithJsonOutput()
    .BuildWithTestUi();

var command = new ListDepotsCommand(730);
var exitCode = await command.ExecuteAsync(context);

Assert.Equal(0, exitCode);
Assert.True(ui.ContainsMessage("Depots"));
```

## Test Categories

### Unit Tests
- **ArgumentParserTests**: Parameter parsing and consumption tracking
- **FormatterTests**: Size and duration formatting
- **CommandFactoryTests**: Command registration and alias resolution

### Integration Tests
- **HelpGeneratorTests**: Help text generation and command documentation
- **Command-specific tests**: (Future) End-to-end command execution

## Writing New Tests

### Example Command Test

```csharp
using DepotDownloader.Client.Commands;
using DepotDownloader.Tests.Helpers;
using NSubstitute;
using Xunit;

public class MyCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidInput_ReturnsSuccess()
    {
        // Arrange
        var (context, ui) = new CommandContextBuilder()
            .WithArgs("-app", "730")
            .WithMockClient()
            .BuildWithTestUi();

        // Setup mock behavior
        context.Client.GetAppInfoAsync(730)
            .Returns(new AppInfo { Name = "Test App", AppId = 730 });

        var command = new MyCommand(730);

        // Act
        var exitCode = await command.ExecuteAsync(context);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(ui.ContainsMessage("expected output"));
    }
}
```

### Best Practices

1. **Use descriptive test names**: `MethodName_WithCondition_ExpectedBehavior`
2. **Follow AAA pattern**: Arrange, Act, Assert
3. **Test edge cases**: null inputs, empty collections, invalid data
4. **Use parameterized tests**: `[Theory]` with `[InlineData]` for similar scenarios
5. **Mock external dependencies**: Use NSubstitute for Steam API calls
6. **Keep tests fast**: Avoid network calls and file I/O where possible
7. **Test one thing**: Each test should verify a single behavior

## Coverage Goals

- **Utilities**: 90%+ coverage (they're easy to test)
- **Parsers**: 85%+ coverage (important for CLI correctness)
- **Commands**: 70%+ coverage (some paths require real Steam connection)
- **UI/Integration**: 60%+ coverage (harder to test, focus on critical paths)

## Continuous Integration

Tests are automatically run on:
- Pull requests
- Commits to main branch
- Release builds

Failing tests will block merges to main.

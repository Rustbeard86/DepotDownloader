# ?? Testing Quick Start Guide

## For Developers New to the Test Suite

### Prerequisites

```bash
# Install dotnet-coverage (one-time)
dotnet tool install -g dotnet-coverage

# Restore packages
dotnet restore
```

### Running Tests

```bash
# Run all tests (fastest)
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter "FullyQualifiedName~ArgumentParserTests"

# Run tests matching a name
dotnet test --filter "Name~Truncate"
```

### Checking Coverage

```bash
# Generate coverage (creates coverage.cobertura.xml)
dotnet-coverage collect -f cobertura -o coverage.cobertura.xml "dotnet test"

# View summary
./show-coverage.ps1

# Read detailed report
code COVERAGE.md
```

### Writing Your First Test

#### 1. Create Test File

```csharp
// DepotDownloader.Tests/Commands/MyNewCommandTests.cs
using DepotDownloader.Client.Commands;
using DepotDownloader.Tests.Helpers;
using Xunit;

namespace DepotDownloader.Tests.Commands;

public class MyNewCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidInput_ReturnsSuccess()
    {
        // Arrange
        var (context, ui) = new CommandContextBuilder()
            .WithArgs("-app", "730")
            .WithMockClient()
            .BuildWithTestUi();

        var command = new MyNewCommand(730);

        // Act
        var exitCode = await command.ExecuteAsync(context);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(ui.ContainsMessage("Expected output"));
    }
}
```

#### 2. Run Your Test

```bash
dotnet test --filter "FullyQualifiedName~MyNewCommandTests"
```

#### 3. Check Coverage

```bash
dotnet-coverage collect -f cobertura "dotnet test"
./show-coverage.ps1
```

### Common Test Patterns

#### Testing a Command

```csharp
[Fact]
public async Task CommandName_Scenario_ExpectedBehavior()
{
    // Arrange - Set up context
    var (context, ui) = new CommandContextBuilder()
        .WithArgs("-app", "730")
        .WithMockClient()
        .BuildWithTestUi();

    // Mock client behavior
    context.Client.GetAppInfoAsync(730)
        .Returns(new AppInfo { Name = "Test App", AppId = 730 });

    var command = new ListDepotsCommand(730);

    // Act - Execute command
    var result = await command.ExecuteAsync(context);

    // Assert - Verify results
    Assert.Equal(0, result);
    Assert.True(ui.ContainsMessage("Test App"));
    Assert.False(ui.ContainsError("Error"));
}
```

#### Testing with Multiple Scenarios (Theory)

```csharp
[Theory]
[InlineData("short", 10, "short")]
[InlineData("very long string", 10, "very lo...")]
[InlineData("exactly ten", 11, "exactly ten")]
public void Truncate_WithVariousInputs_TruncatesCorrectly(
    string input, 
    int maxLength, 
    string expected)
{
    // Act
    var result = Formatter.Truncate(input, maxLength);

    // Assert
    Assert.Equal(expected, result);
}
```

#### Testing Error Cases

```csharp
[Fact]
public async Task ExecuteAsync_WithInvalidInput_ReturnsError()
{
    // Arrange
    var (context, ui) = new CommandContextBuilder()
        .WithArgs() // No arguments
        .WithMockClient()
        .BuildWithTestUi();

    var command = new MyCommand(0); // Invalid ID

    // Act
    var result = await command.ExecuteAsync(context);

    // Assert
    Assert.Equal(1, result); // Error exit code
    Assert.True(ui.ContainsError("Error"));
}
```

#### Testing JSON Output

```csharp
[Fact]
public async Task ExecuteAsync_WithJsonOutput_WritesJson()
{
    // Arrange
    var (context, ui) = new CommandContextBuilder()
        .WithArgs("-app", "730")
        .WithJsonOutput(true)
        .WithMockClient()
        .BuildWithTestUi();

    context.Client.GetAppInfoAsync(730)
        .Returns(new AppInfo { Name = "Test", AppId = 730 });

    var command = new ListDepotsCommand(730);

    // Act
    var result = await command.ExecuteAsync(context);

    // Assert
    Assert.Equal(0, result);
    Assert.True(ui.ContainsMessage("\"appId\": 730"));
    Assert.True(ui.ContainsMessage("\"success\": true"));
}
```

### Test Helpers Available

#### TestUserInterface

Captures console output for assertions:

```csharp
var ui = new TestUserInterface();
command.Execute(ui);

// Check messages
Assert.True(ui.ContainsMessage("Expected text"));
Assert.False(ui.ContainsError("Error text"));

// Get all output
var allMessages = ui.Messages;
var allErrors = ui.Errors;

// Clear for reuse
ui.Clear();
```

#### CommandContextBuilder

Fluent builder for test setup:

```csharp
var (context, ui) = new CommandContextBuilder()
    .WithArgs("-app", "730", "-depot", "731")
    .WithMockClient()
    .WithConfig(new ConfigFile { Username = "test" })
    .WithJsonOutput(true)
    .BuildWithTestUi();
```

### Best Practices

#### ? DO

- Use descriptive test names
- Test one thing per test
- Use Theory for similar scenarios
- Mock external dependencies
- Test error paths
- Keep tests fast (< 100ms each)
- Use AAA pattern (Arrange, Act, Assert)

#### ? DON'T

- Test implementation details
- Use Thread.Sleep
- Test multiple unrelated things
- Have tests depend on each other
- Use random data without seeds
- Make network calls
- Access real file system (use temp)

### Debugging Tests

#### In Visual Studio

1. Set breakpoint in test
2. Right-click test ? Debug Test(s)
3. Step through code

#### In VS Code

1. Install C# Dev Kit extension
2. Set breakpoint
3. Click "Debug Test" code lens

#### Command Line

```bash
# Run with debugger attached
dotnet test --logger "console;verbosity=detailed" -- RunConfiguration.DebugRun=true
```

### Continuous Integration

Tests run automatically on:
- Every commit
- Every pull request
- Before merging to main

**CI Requirements:**
- ? All tests must pass
- ? No new warnings
- ?? Coverage should not decrease

### Getting Help

1. **Read the docs**:
   - `DepotDownloader.Tests/README.md` - Detailed guide
   - `TEST-SUMMARY.md` - Coverage analysis
   - `COVERAGE.md` - Coverage report

2. **Check examples**:
   - Look at existing tests for patterns
   - `ArgumentParserTests.cs` - Simple unit tests
   - `HelpGeneratorTests.cs` - Testing output

3. **Ask questions**:
   - Open GitHub issue
   - Check existing issues for answers

### Common Issues

#### "Test not discovered"
```bash
# Clean and rebuild
dotnet clean
dotnet build
dotnet test
```

#### "Coverage tool not found"
```bash
# Install tool
dotnet tool install -g dotnet-coverage
```

#### "Test timeout"
```csharp
// Add timeout attribute
[Fact(Timeout = 5000)] // 5 seconds
public async Task LongRunningTest() { }
```

#### "Flaky test"
```csharp
// Make deterministic - avoid:
- DateTime.Now (use Clock abstraction)
- Random (use seeded Random)
- File system (use temp directory)
- Network calls (use mocks)
```

### Useful Links

- [xUnit Documentation](https://xunit.net/)
- [NSubstitute Guide](https://nsubstitute.github.io/)
- [.NET Testing Best Practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)

---

**Happy Testing! ??**

Remember: **Tests are documentation** - write them so others can understand your code!

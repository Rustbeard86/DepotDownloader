# Code Coverage Report

**Generated:** $(Get-Date)
**Test Project:** DepotDownloader.Tests

## Overall Coverage Summary

Based on the Cobertura coverage report:

- **Overall Line Coverage:** 4.8% (395 / 8,145 lines)
- **Overall Branch Coverage:** 2.8% (89 / 3,139 branches)
- **Total Tests:** 50 (all passing ?)

### Coverage by Project

#### DepotDownloader.Tests (Test Project)
- **Line Coverage:** 72.5% 
- **Status:** ? Good coverage of test infrastructure

#### DepotDownloader.Client (CLI Application)
- **Line Coverage:** ~5% (estimated from overall)
- **Status:** ?? Needs more coverage
- **Note:** Command classes need integration tests

#### DepotDownloader.Lib (Core Library)
- **Line Coverage:** ~3% (estimated from overall)
- **Status:** ?? Needs significant coverage
- **Note:** Core download logic untested

## What's Currently Tested ?

### Infrastructure (Well Tested)
1. **ArgumentParser** - 9 tests
   - Parameter parsing
   - Alias resolution
   - Consumed argument tracking
   - List parameter handling

2. **Formatter** - 4 tests
   - Size formatting (bytes to human-readable)
   - Duration formatting
   - String truncation

3. **HelpGenerator** - 5 tests
   - Help text generation
   - Command discovery
   - Alias resolution

4. **CommandFactory** - 6 tests
   - Command registration
   - Alias mapping
   - Command validation

### Test Helpers
- **TestUserInterface** - Mock UI for testing
- **CommandContextBuilder** - Fluent builder for test setup

## What Needs Testing ??

### High Priority (Core Functionality)

#### DepotDownloader.Lib
- [ ] `DepotDownloaderClient` - Main client API
- [ ] `ContentDownloader` - Download orchestration
- [ ] `Steam3Session` - Steam authentication
- [ ] `DepotFileDownloader` - File download logic
- [ ] `AppInfoService` - App metadata queries
- [ ] `RetryPolicy` - Retry behavior
- [ ] `SpeedLimiter` - Download throttling
- [ ] `DownloadStateStore` - Resume functionality

#### DepotDownloader.Client
- [ ] Command execution paths
- [ ] `OptionsBuilder` - Option validation
- [ ] Error handling flows
- [ ] JSON output formatting

### Medium Priority (Supporting Features)

- [ ] `CDNClientPool` - Connection pooling
- [ ] `DepotConfigStore` - Configuration persistence
- [ ] `AccountSettingsStore` - Credential storage
- [ ] `PlatformUtilities` - OS detection
- [ ] `ProtoManifest` - Manifest parsing

### Low Priority (Utilities)
- [x] `Formatter` - ? Tested
- [x] `ArgumentParser` - ? Tested
- [ ] `Util` - Helper methods
- [ ] `ConsoleAuthenticator` - Interactive auth

## Coverage Goals

### Short-term (Current Sprint)
- ? **Test Infrastructure:** 70%+ (Achieved: 72.5%)
- ? **CLI Utilities:** Target 85%
- ? **Command Handlers:** Target 60%

### Medium-term (Next Release)
- ?? **Overall:** Target 40%+
- ?? **Core Library:** Target 50%+
- ?? **CLI Client:** Target 70%+

### Long-term (Production Ready)
- ?? **Overall:** Target 70%+
- ?? **Critical Paths:** Target 90%+

## How to Improve Coverage

### 1. Add Command Integration Tests

```csharp
[Fact]
public async Task ListDepotsCommand_WithValidApp_ReturnsSuccess()
{
    // Arrange
    var (context, ui) = new CommandContextBuilder()
        .WithMockClient()
        .BuildWithTestUi();
    
    context.Client.GetAppInfoAsync(730)
        .Returns(new AppInfo { Name = "CS2", AppId = 730 });
    context.Client.GetDepotsAsync(730)
        .Returns(new List<DepotInfo> { /* test data */ });
    
    var command = new ListDepotsCommand(730);
    
    // Act
    var result = await command.ExecuteAsync(context);
    
    // Assert
    Assert.Equal(0, result);
    Assert.True(ui.ContainsMessage("Depots for CS2"));
}
```

### 2. Test Core Library with Mocks

```csharp
[Fact]
public async Task DepotDownloaderClient_WithValidOptions_DownloadsSuccessfully()
{
    // Arrange
    var mockSession = Substitute.For<Steam3Session>();
    var client = new DepotDownloaderClient(new TestUserInterface());
    
    var options = new DepotDownloadOptions
    {
        AppId = 730,
        InstallDirectory = Path.GetTempPath()
    };
    
    // Act & Assert
    // Test download flow
}
```

### 3. Test Error Handling

```csharp
[Fact]
public async Task DownloadCommand_WithInsufficientDiskSpace_ReturnsError()
{
    // Test exception paths
}

[Fact]
public async Task Steam3Session_WithInvalidCredentials_ThrowsException()
{
    // Test authentication failures
}
```

## Running Coverage Locally

### Generate Coverage Report
```bash
# Run tests with coverage
dotnet-coverage collect -f cobertura -o coverage.cobertura.xml "dotnet test"

# View in Visual Studio
# Tools > Analyze Code Coverage > View Code Coverage Results
```

### Generate HTML Report (Optional)
```bash
# Install ReportGenerator
dotnet tool install -g dotnet-reportgenerator-globaltool

# Generate HTML report
reportgenerator -reports:coverage.cobertura.xml -targetdir:coverage-report -reporttypes:Html

# Open in browser
start coverage-report/index.html
```

## CI/CD Integration

### GitHub Actions Example
```yaml
- name: Run tests with coverage
  run: dotnet-coverage collect -f cobertura -o coverage.xml "dotnet test"

- name: Upload coverage
  uses: codecov/codecov-action@v3
  with:
    files: ./coverage.xml
    fail_ci_if_error: true
```

## Next Steps

1. ? **Complete** - Test infrastructure and utilities
2. ?? **In Progress** - Add command integration tests
3. ? **Planned** - Add core library unit tests
4. ? **Planned** - Add error handling tests
5. ? **Planned** - Add performance tests

## Notes

- Current low coverage is expected for a new test project
- Focus on testing **critical paths** and **error handling** first
- Mock external dependencies (Steam API, file system) for unit tests
- Use integration tests for command execution flows
- Don't aim for 100% coverage - focus on **valuable tests**

## Useful Commands

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet-coverage collect -f cobertura -o coverage.cobertura.xml "dotnet test"

# Run specific test class
dotnet test --filter "FullyQualifiedName~HelpGeneratorTests"

# Run tests in watch mode
dotnet watch test

# List all tests
dotnet test --list-tests
```

---

**Report Generated by:** DepotDownloader Test Suite
**Coverage Tool:** dotnet-coverage v18.1.0
**Test Framework:** xUnit 2.9.2

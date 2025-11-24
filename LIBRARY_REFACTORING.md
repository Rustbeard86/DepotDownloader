# DepotDownloader Library Refactoring

## Overview

The DepotDownloader codebase has been successfully refactored to support both **CLI usage** and **library consumption**. The core downloading logic is now accessible programmatically through a clean public API.

## New Public API

### Main Entry Point: `DepotDownloaderClient`

```csharp
using DepotDownloader;

// Create client with custom UI or default (no output)
using var client = new DepotDownloaderClient(new ConsoleUserInterface());

// Enable debug logging
client.EnableDebugLogging();

// Authenticate
client.Login("username", "password", rememberPassword: true);
// OR
client.LoginAnonymous();
// OR
client.LoginWithQrCode(rememberPassword: true);

// Download app
var options = new DepotDownloadOptions
{
    AppId = 730, // Counter-Strike 2
    Branch = "public",
    InstallDirectory = @"C:\Games\CS2"
};
await client.DownloadAppAsync(options);

// Download workshop item
await client.DownloadPublishedFileAsync(appId: 730, publishedFileId: 1885082371);

// Download UGC
await client.DownloadUgcAsync(appId: 730, ugcId: 770604181014286929);
```

## New Files Created

### 1. **NullUserInterface.cs** (Public)
- No-op implementation of `IUserInterface`
- Used by default when library consumers don't need output
- All methods are empty/return defaults

### 2. **DepotDownloadOptions.cs** (Public)
- Configuration object for downloads
- Replaces command-line argument parsing
- Properties include:
  - `AppId`, `DepotManifestIds`, `Branch`, `BranchPassword`
  - `Os`, `Architecture`, `Language`, `LowViolence`
  - `InstallDirectory`, `FilesToDownload`, `FilesToDownloadRegex`
  - `VerifyAll`, `DownloadManifestOnly`
  - `MaxDownloads`, `CellId`, `LoginId`
  - `DownloadAllPlatforms`, `DownloadAllArchs`, `DownloadAllLanguages`

### 3. **DepotDownloaderClient.cs** (Public)
- Main library entry point
- Encapsulates initialization and cleanup
- Public methods:
  - `DepotDownloaderClient(IUserInterface)` - Constructor
  - `EnableDebugLogging()` - Enables verbose logging
  - `Login(username, password, rememberPassword)` - Username/password auth
  - `LoginAnonymous()` - Anonymous auth
  - `LoginWithQrCode(rememberPassword)` - QR code auth
  - `DownloadAppAsync(options)` - Download app content
  - `DownloadPublishedFileAsync(appId, publishedFileId)` - Download workshop item
  - `DownloadUgcAsync(appId, ugcId)` - Download UGC
  - `Dispose()` - Cleanup and disconnect

## Modified Files

### **ContentDownloader.cs**
- Changed `ContentDownloaderException` from `internal` to **`public`**
- Library consumers can now catch this specific exception type

### **Program.cs**
- Remains as the **CLI wrapper**
- Parses command-line arguments
- Could be refactored further to use `DepotDownloaderClient` directly
- Currently still uses direct `ContentDownloader` calls (future enhancement)

## Usage Examples

### Example 1: Silent Library Usage
```csharp
// No output, just download
using var client = new DepotDownloaderClient(); // Uses NullUserInterface by default

if (!client.LoginAnonymous())
    throw new Exception("Login failed");

var options = new DepotDownloadOptions
{
    AppId = 730,
    DepotManifestIds = [(731, 0)] // Depot 731, latest manifest
};

try
{
    await client.DownloadAppAsync(options);
}
catch (ContentDownloaderException ex)
{
    Console.Error.WriteLine($"Download failed: {ex.Message}");
}
```

### Example 2: Custom UI Implementation
```csharp
public class LoggingUserInterface : IUserInterface
{
    private readonly ILogger _logger;
    
    public void WriteLine(string message) => _logger.LogInformation(message);
    public void WriteError(string message) => _logger.LogError(message);
    public void WriteDebug(string category, string message) => 
        _logger.LogDebug("[{Category}] {Message}", category, message);
    
    // ... implement other methods
}

using var client = new DepotDownloaderClient(new LoggingUserInterface(logger));
// All output now goes through your ILogger
```

### Example 3: Download with File Filtering
```csharp
var options = new DepotDownloadOptions
{
    AppId = 730,
    InstallDirectory = @"C:\Games\CS2",
    FilesToDownload = new HashSet<string>
    {
        "csgo/bin/client.dll",
        "csgo/bin/server.dll"
    },
    FilesToDownloadRegex = new List<Regex>
    {
        new Regex(@"\.dll$", RegexOptions.IgnoreCase)
    }
};

await client.DownloadAppAsync(options);
```

## Architecture Benefits

### ✅ **Separation of Concerns**
- CLI logic in `Program.cs`
- Library logic in `ContentDownloader.cs` and related files
- UI abstraction via `IUserInterface`

### ✅ **Testability**
- Mock `IUserInterface` for unit tests
- No Console dependencies in core logic
- Pure async/await patterns

### ✅ **Flexibility**
- GUI applications can use `DepotDownloaderClient`
- Web services can use `NullUserInterface`
- Custom logging via `IUserInterface` implementations

### ✅ **Backward Compatibility**
- Existing CLI still works exactly the same
- No breaking changes to command-line usage
- Internal implementation unchanged

## Future Enhancements

### Potential Improvements:
1. **Refactor Program.cs** to use `DepotDownloaderClient` directly
2. **Add progress callbacks** to `IUserInterface` or `DepotDownloadOptions`
3. **Add cancellation support** via `CancellationToken` parameter
4. **Create separate projects**:
   - `DepotDownloader.Library` (core library)
   - `DepotDownloader.Cli` (CLI wrapper)
5. **Add more overloads** for common scenarios
6. **Add builder pattern** for fluent configuration

### Example Builder Pattern:
```csharp
var client = new DepotDownloaderClientBuilder()
    .WithUserInterface(new ConsoleUserInterface())
    .WithDebugLogging()
    .WithCredentials("username", "password", rememberPassword: true)
    .Build();

await client.DownloadAsync(builder => builder
    .ForApp(730)
    .FromBranch("public")
    .ToDirectory(@"C:\Games\CS2")
    .WithFileFilter("*.dll"));
```

## Migration Guide for External Consumers

### Before (CLI only):
```bash
./DepotDownloader -app 730 -depot 731 -username myuser -password mypass
```

### After (Library):
```csharp
using var client = new DepotDownloaderClient(new ConsoleUserInterface());
client.Login("myuser", "mypass");
await client.DownloadAppAsync(new DepotDownloadOptions 
{ 
    AppId = 730, 
    DepotManifestIds = [(731, 0)] 
});
```

## Breaking Changes

### None! 
The CLI functionality remains unchanged. All changes are additive:
- New public classes: `DepotDownloaderClient`, `DepotDownloadOptions`, `NullUserInterface`
- Changed visibility: `ContentDownloaderException` is now public
- Existing `Program.cs` CLI entry point unchanged

## Testing the Library

```csharp
[Fact]
public async Task DownloadApp_ValidAppId_Succeeds()
{
    var mockUi = new Mock<IUserInterface>();
    using var client = new DepotDownloaderClient(mockUi.Object);
    
    client.LoginAnonymous();
    
    var options = new DepotDownloadOptions { AppId = 730 };
    await client.DownloadAppAsync(options);
    
    mockUi.Verify(ui => ui.WriteLine(It.IsAny<string>()), Times.AtLeastOnce());
}
```

## Summary

The refactoring successfully transforms DepotDownloader from a CLI-only tool into a **reusable library** while maintaining full backward compatibility. External consumers can now:

- ✅ Use DepotDownloader programmatically in their own applications
- ✅ Integrate with any UI framework (WPF, Avalonia, Blazor, etc.)
- ✅ Customize logging and error handling
- ✅ Automate Steam content downloads in services/tools
- ✅ Test download logic with mocked interfaces

The existing CLI tool continues to work exactly as before with no changes required.

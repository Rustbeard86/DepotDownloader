# DepotDownloader

[![License: GPL v2](https://img.shields.io/badge/License-GPL_v2-blue.svg)](LICENSE)

Steam depot downloader utilizing the [SteamKit2](https://github.com/SteamRE/SteamKit) library. 

**Available as both a CLI tool and a .NET library!**

- **DepotDownloader.Client** - Command-line application for downloading Steam content
- **DepotDownloader.Lib** - .NET library for programmatic Steam content downloads

Supports **.NET 10.0** and later.

> **Note:** This is a fork of [SteamRE/DepotDownloader](https://github.com/SteamRE/DepotDownloader) with significant enhancements including library extraction for programmatic use.

---

## Table of Contents

- [Installation](#installation)
  - [CLI Application](#cli-application)
  - [Library (NuGet)](#library-nuget)
- [CLI Usage](#cli-usage)
  - [Download an App](#download-an-app)
  - [Download Workshop Items](#download-workshop-items)
  - [Discovery Commands](#discovery-commands)
  - [Authentication](#authentication)
  - [CLI Parameters Reference](#cli-parameters-reference)
- [Library Usage](#library-usage)
  - [Quick Start](#quick-start)
  - [Authentication Methods](#authentication-methods)
  - [Query APIs](#query-apis)
  - [Download Planning](#download-planning)
  - [Download Options](#download-options)
  - [Custom User Interface](#custom-user-interface)
  - [Error Handling](#error-handling)
- [API Reference](#api-reference)
- [FAQ](#faq)
- [License](#license)

---

## Installation

### CLI Application

#### Directly from GitHub

Download a binary from [the releases page](https://github.com/Rustbeard86/DepotDownloader/releases/latest).

#### Build from Source

```shell
git clone https://github.com/Rustbeard86/DepotDownloader.git
cd DepotDownloader
dotnet build -c Release
```

### Library (NuGet)

Install via NuGet Package Manager:

```shell
dotnet add package DepotDownloader.Lib
```

Or via Package Manager Console:

```powershell
Install-Package DepotDownloader.Lib
```

---

## CLI Usage

The CLI application must be run from a console/terminal.

### Download an App

Download all depots for an app:

```powershell
./DepotDownloader -app <appid> [-username <username>]
```

Download a specific depot:

```powershell
./DepotDownloader -app <appid> -depot <depotid> [-manifest <manifestid>]
```

**Examples:**

```powershell
# Download Counter-Strike 2 (anonymous - limited games available)
./DepotDownloader -app 730

# Download with authentication
./DepotDownloader -app 730 -username myaccount

# Download specific depot and manifest
./DepotDownloader -app 730 -depot 731 -manifest 7617088375292372759

# Download to specific directory
./DepotDownloader -app 730 -dir "C:\Games\CS2"

# Download from beta branch
./DepotDownloader -app 730 -branch beta -username myaccount
```

### Download Workshop Items

Using PublishedFileId:

```powershell
./DepotDownloader -app <appid> -pubfile <publishedfileid> [-username <username>]
```

Using UGC ID:

```powershell
./DepotDownloader -app <appid> -ugc <ugcid> [-username <username>]
```

**Examples:**

```powershell
# Download workshop item
./DepotDownloader -app 730 -pubfile 1885082371

# Download UGC content
./DepotDownloader -app 730 -ugc 770604181014286929
```

### Discovery Commands

Query app information without downloading:

```powershell
# List all depots for an app
./DepotDownloader -app 730 -list-depots

# List all branches for an app
./DepotDownloader -app 730 -list-branches

# Preview what would be downloaded (dry run)
./DepotDownloader -app 730 -dry-run
```

**Example output for `-list-depots`:**

```
Depots for Counter-Strike 2 (730):

  DepotID    Name                                     OS              Arch   Language   Size
  ----------------------------------------------------------------------------------------------------
  731        Counter-Strike 2 Content                 windows         -      -          15.1 GB
  732        Counter-Strike 2 Content                 linux           -      -          14.8 GB

Total: 2 depot(s)
```

**Example output for `-dry-run`:**

```
Download Plan for Counter-Strike 2 (730):

  Depot 731 (Manifest 7617088375292372759)
    Files: 1432
    Size:  15.1 GB

--------------------------------------------------

Summary:
  Total depots:     1
  Total files:      1,432
  Total size:       15.1 GB

Estimated download time:
  At 10 MB/s:  25 min 30 sec
  At 50 MB/s:  5 min 6 sec
  At 100 MB/s: 2 min 33 sec

To download, run the same command without --dry-run
```

### Authentication

By default, DepotDownloader uses an anonymous account. Many games require authentication.

```powershell
# Interactive password prompt
./DepotDownloader -app 730 -username myaccount

# With password (not recommended - use interactive prompt)
./DepotDownloader -app 730 -username myaccount -password mypassword

# Remember credentials for future use
./DepotDownloader -app 730 -username myaccount -remember-password

# Login via QR code (Steam mobile app)
./DepotDownloader -app 730 -qr
```

### CLI Parameters Reference

#### Authentication

| Parameter | Description |
|-----------|-------------|
| `-username <user>` | Steam account username |
| `-password <pass>` | Steam account password (interactive prompt recommended) |
| `-remember-password` | Save credentials for future logins |
| `-qr` | Login via QR code with Steam mobile app |
| `-no-mobile` | Prefer 2FA code entry over mobile app confirmation |
| `-loginid <#>` | Unique LogonID for concurrent instances |

#### Content Selection

| Parameter | Description |
|-----------|-------------|
| `-app <#>` | **Required.** Steam AppID to download |
| `-depot <#>` | Specific DepotID to download |
| `-manifest <id>` | Specific manifest ID (requires `-depot`) |
| `-branch <name>` | Branch name (default: `public`) |
| `-branchpassword <pass>` | Password for protected branches |
| `-pubfile <#>` | Workshop PublishedFileId to download |
| `-ugc <#>` | UGC ID to download |

#### Platform Filtering

| Parameter | Description |
|-----------|-------------|
| `-os <os>` | Target OS: `windows`, `macos`, or `linux` |
| `-osarch <arch>` | Target architecture: `32` or `64` |
| `-language <lang>` | Target language (default: `english`) |
| `-all-platforms` | Download all platform-specific depots |
| `-all-archs` | Download all architecture-specific depots |
| `-all-languages` | Download all language-specific depots |
| `-lowviolence` | Include low-violence depots |

#### Download Options

| Parameter | Description |
|-----------|-------------|
| `-dir <path>` | Installation directory |
| `-filelist <file>` | File containing paths to download (supports `regex:` prefix) |
| `-validate` | Verify checksums of existing files |
| `-manifest-only` | Download manifest metadata only |
| `-max-downloads <#>` | Concurrent downloads (default: 8) |
| `-cellid <#>` | Override content server CellID |
| `-use-lancache` | Route downloads through Lancache |
| `-skip-disk-check` | Skip disk space verification before downloading |

#### Discovery & Planning

| Parameter | Description |
|-----------|-------------|
| `-list-depots` | List all depots for the app and exit |
| `-list-branches` | List all branches for the app and exit |
| `-dry-run` | Show download plan without downloading |

#### Other

| Parameter | Description |
|-----------|-------------|
| `-debug` | Enable verbose debug logging |
| `-V`, `--version` | Print version information |

---

## Library Usage

The `DepotDownloader.Lib` package allows you to integrate Steam content downloading into your own .NET applications.

### Quick Start

```csharp
using DepotDownloader.Lib;

// Create client (uses silent NullUserInterface by default)
using var client = new DepotDownloaderClient();

// Authenticate anonymously
if (!client.LoginAnonymous())
{
    Console.WriteLine("Login failed");
    return;
}

// Download an app
var options = new DepotDownloadOptions
{
    AppId = 730,  // Counter-Strike 2
    InstallDirectory = @"C:\Games\CS2"
};

try
{
    await client.DownloadAppAsync(options);
    Console.WriteLine("Download complete!");
}
catch (ContentDownloaderException ex)
{
    Console.WriteLine($"Download failed: {ex.Message}");
}
```

### Authentication Methods

```csharp
using var client = new DepotDownloaderClient();

// Anonymous login (limited game access)
client.LoginAnonymous();

// Username and password
client.Login("username", "password");

// With credential persistence
client.Login("username", "password", rememberPassword: true);

// Skip mobile app confirmation (prefer 2FA code)
client.Login("username", "password", skipAppConfirmation: true);

// QR code login (requires IUserInterface that supports DisplayQrCode)
client.LoginWithQrCode(rememberPassword: true);
```

### Query APIs

Query Steam app information without downloading:

```csharp
// Get app information
var appInfo = await client.GetAppInfoAsync(730);
Console.WriteLine($"App: {appInfo.Name} (Type: {appInfo.Type})");

// List all depots
var depots = await client.GetDepotsAsync(730);
foreach (var depot in depots)
{
    Console.WriteLine($"Depot {depot.DepotId}: {depot.Name}");
    Console.WriteLine($"  OS: {depot.Os ?? "all"}, Arch: {depot.Architecture ?? "any"}");
    if (depot.MaxSize.HasValue)
        Console.WriteLine($"  Size: {depot.MaxSize.Value / 1024 / 1024 / 1024.0:F1} GB");
}

// List all branches
var branches = await client.GetBranchesAsync(730);
foreach (var branch in branches)
{
    Console.WriteLine($"Branch: {branch.Name}");
    Console.WriteLine($"  Build: {branch.BuildId}");
    Console.WriteLine($"  Updated: {branch.TimeUpdated}");
    Console.WriteLine($"  Password Protected: {branch.IsPasswordProtected}");
}
```

### Download Planning

Check what will be downloaded before starting, and verify disk space:

```csharp
var options = new DepotDownloadOptions
{
    AppId = 730,
    InstallDirectory = @"C:\Games\CS2"
};

// Get download plan (dry run)
var plan = await client.GetDownloadPlanAsync(options);
Console.WriteLine($"Would download {plan.TotalFileCount:N0} files ({plan.TotalDownloadSize / 1024.0 / 1024 / 1024:F2} GB)");

foreach (var depot in plan.Depots)
{
    Console.WriteLine($"  Depot {depot.DepotId}: {depot.Files.Count} files, {depot.TotalSize / 1024.0 / 1024:F1} MB");
}

// Check disk space before downloading
var spaceCheck = await client.CheckDiskSpaceAsync(options);
if (!spaceCheck.HasSufficientSpace)
{
    Console.WriteLine($"Insufficient disk space on {spaceCheck.TargetDrive}!");
    Console.WriteLine($"Required: {spaceCheck.RequiredBytes / 1024.0 / 1024 / 1024:F2} GB");
    Console.WriteLine($"Available: {spaceCheck.AvailableBytes / 1024.0 / 1024 / 1024:F2} GB");
    return;
}

// Proceed with download
await client.DownloadAppAsync(options);
```

### Download Options

```csharp
var options = new DepotDownloadOptions
{
    // Required
    AppId = 730,
    
    // Content selection
    Branch = "public",                    // or "beta", etc.
    BranchPassword = null,                // for protected branches
    DepotManifestIds = new List<(uint, ulong)>
    {
        (731, 0),  // Depot 731, latest manifest (0 = latest)
    },
    
    // Platform filtering
    Os = "windows",                       // null = current OS
    Architecture = "64",                  // null = current arch
    Language = "english",                 // null = english
    DownloadAllPlatforms = false,
    DownloadAllArchs = false,
    DownloadAllLanguages = false,
    LowViolence = false,
    
    // Download behavior
    InstallDirectory = @"C:\Games\CS2",   // null = default structure
    MaxDownloads = 8,                     // concurrent chunks
    VerifyAll = false,                    // validate existing files
    DownloadManifestOnly = false,         // metadata only
    
    // File filtering
    FilesToDownload = new HashSet<string>
    {
        "csgo/bin/client.dll"
    },
    FilesToDownloadRegex = new List<Regex>
    {
        new Regex(@"\.dll$", RegexOptions.IgnoreCase)
    },
    
    // Advanced
    CellId = 0,                           // content server override
    LoginId = null                        // for concurrent instances
};

await client.DownloadAppAsync(options);
```

### Workshop Content

```csharp
// Download by PublishedFileId
await client.DownloadPublishedFileAsync(appId: 730, publishedFileId: 1885082371);

// Download by UGC ID
await client.DownloadUgcAsync(appId: 730, ugcId: 770604181014286929);
```

### Custom User Interface

Implement `IUserInterface` to customize output and user interaction:

```csharp
public class MyUserInterface : IUserInterface
{
    public bool IsInputRedirected => false;
    public bool IsOutputRedirected => false;

    public void WriteLine(string message) 
        => Console.WriteLine($"[MyApp] {message}");

    public void WriteLine(string format, params object[] args) 
        => Console.WriteLine($"[MyApp] {string.Format(format, args)}");

    public void WriteError(string message) 
        => Console.Error.WriteLine($"[ERROR] {message}");

    public void WriteDebug(string category, string message) 
        => Debug.WriteLine($"[{category}] {message}");

    public string ReadLine() => Console.ReadLine();
    
    public string ReadPassword()
    {
        var password = new StringBuilder();
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                password.Length--;
            else if (!char.IsControl(key.KeyChar))
                password.Append(key.KeyChar);
        }
        return password.ToString();
    }

    public void DisplayQrCode(string challengeUrl)
    {
        // Generate and display QR code
        Console.WriteLine($"Scan QR code: {challengeUrl}");
    }

    // Implement remaining interface methods...
    public void Write(string message) => Console.Write(message);
    public void Write(string format, params object[] args) => Console.Write(format, args);
    public void WriteLine() => Console.WriteLine();
    public void WriteError(string format, params object[] args) => Console.Error.WriteLine(format, args);
    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
    public void UpdateProgress(ulong downloaded, ulong total) { }
}

// Use custom interface
using var client = new DepotDownloaderClient(new MyUserInterface());
```

For silent operation, use the built-in `NullUserInterface`:

```csharp
using var client = new DepotDownloaderClient(new NullUserInterface());
// Or simply:
using var client = new DepotDownloaderClient(); // NullUserInterface is default
```

### Error Handling

```csharp
try
{
    await client.DownloadAppAsync(options);
}
catch (InsufficientDiskSpaceException ex)
{
    // Not enough disk space (automatic check, can be disabled with VerifyDiskSpace = false)
    Console.WriteLine($"Not enough space on {ex.TargetDrive}");
    Console.WriteLine($"Need {ex.RequiredBytes} bytes, have {ex.AvailableBytes}, short by {ex.ShortfallBytes}");
}
catch (ContentDownloaderException ex)
{
    // Steam/download-specific errors
    Console.WriteLine($"Download error: {ex.Message}");
}
catch (ArgumentException ex)
{
    // Invalid options
    Console.WriteLine($"Invalid options: {ex.Message}");
}
catch (OperationCanceledException)
{
    // Download was cancelled
    Console.WriteLine("Download cancelled");
}
```

### Disk Space Verification

By default, `DownloadAppAsync` checks disk space before downloading and throws `InsufficientDiskSpaceException` if there isn't enough space. You can disable this:

```csharp
var options = new DepotDownloadOptions
{
    AppId = 730,
    InstallDirectory = @"C:\Games\CS2",
    VerifyDiskSpace = false  // Skip automatic disk space check
};
```

### Debug Logging

```csharp
using var client = new DepotDownloaderClient(myUserInterface);

// Enable verbose logging (SteamKit2 + HTTP diagnostics)
client.EnableDebugLogging();

// Debug output goes to IUserInterface.WriteDebug()
```

---

## API Reference

### DepotDownloaderClient

| Method | Description |
|--------|-------------|
| `DepotDownloaderClient(IUserInterface)` | Creates a new client instance |
| `Login(username, password, rememberPassword, skipAppConfirmation)` | Authenticate with credentials |
| `LoginAnonymous(skipAppConfirmation)` | Anonymous authentication |
| `LoginWithQrCode(rememberPassword, skipAppConfirmation)` | QR code authentication |
| `GetAppInfoAsync(appId)` | Get app name and type |
| `GetDepotsAsync(appId)` | List all depots for an app |
| `GetBranchesAsync(appId)` | List all branches for an app |
| `GetDownloadPlanAsync(options)` | Get download plan without downloading |
| `CheckDiskSpaceAsync(options)` | Check if sufficient disk space is available |
| `GetRequiredDiskSpaceAsync(options)` | Get required download size in bytes |
| `DownloadAppAsync(DepotDownloadOptions)` | Download app content |
| `DownloadPublishedFileAsync(appId, publishedFileId)` | Download workshop item |
| `DownloadUgcAsync(appId, ugcId)` | Download UGC content |
| `EnableDebugLogging()` | Enable verbose logging |
| `Dispose()` | Clean up resources |

### Query Result Types

| Type | Description |
|------|-------------|
| `AppInfo` | App ID, name, and type |
| `DepotInfo` | Depot ID, name, OS, architecture, language, size |
| `BranchInfo` | Branch name, build ID, update time, password protection |
| `DownloadPlan` | List of depots with files and total size |
| `DiskSpaceCheckResult` | Required/available space and sufficiency check |

### DepotDownloadOptions

See [Download Options](#download-options) section for complete property documentation.

### IUserInterface

Interface for customizing user interaction. See [Custom User Interface](#custom-user-interface) for implementation details.

### ContentDownloaderException

Exception thrown for Steam/download-specific errors. Contains a descriptive error message.

---

## FAQ

### Why am I prompted for a 2-factor code every time?

Use `-remember-password` (CLI) or `rememberPassword: true` (library) to persist your session credentials.

### Can I run multiple instances simultaneously?

Yes, but each instance needs a unique LoginID to avoid disconnecting other sessions:

```powershell
# CLI
./DepotDownloader -app 730 -loginid 12345
```

```csharp
// Library
options.LoginId = 12345;
```

### Why can't I download certain games anonymously?

Anonymous accounts have limited access. View available games at [SteamDB Sub 17906](https://steamdb.info/sub/17906/). For other games, authenticate with a Steam account that owns the game.

### Password with special characters doesn't work?

Use interactive password input instead of `-password`:

```powershell
./DepotDownloader -app 730 -username myaccount
# Password will be prompted securely
```

### Error 401 or "no manifest code returned"?

This typically means:
1. Try logging in with a Steam account (not anonymous)
2. The developer has blocked downloading old manifests
3. The manifest ID is invalid

### Slow download speeds?

Try increasing concurrent downloads:

```powershell
# CLI
./DepotDownloader -app 730 -max-downloads 16
```

```csharp
// Library
options.MaxDownloads = 16;
```

### Using with Lancache?

```powershell
# CLI - auto-detects and increases parallelism
./DepotDownloader -app 730 -use-lancache
```

### How do I check if I have enough disk space?

```powershell
# CLI - use dry-run to see total size
./DepotDownloader -app 730 -dry-run
```

```csharp
// Library - use CheckDiskSpaceAsync
var result = await client.CheckDiskSpaceAsync(options);
if (!result.HasSufficientSpace)
{
    Console.WriteLine($"Need {result.RequiredBytes} bytes, only {result.AvailableBytes} available");
}
```

---

## License

This project is licensed under the [GNU General Public License v2.0](LICENSE).

---

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Credits

- [SteamRE/DepotDownloader](https://github.com/SteamRE/DepotDownloader) - Original project
- [SteamKit2](https://github.com/SteamRE/SteamKit) - Steam protocol library

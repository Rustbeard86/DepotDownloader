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
  - [Cancellation Support](#cancellation-support)
  - [Progress Reporting](#progress-reporting)
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

# Limit download speed to 10 MB/s
./DepotDownloader -app 730 -max-speed 10

# Configure retry attempts (0 disables retries)
./DepotDownloader -app 730 -retries 10

# Stop on first depot failure (default: continue with other depots)
./DepotDownloader -app 730 -fail-fast

# Resume a previously interrupted download
./DepotDownloader -app 730 -dir "C:\Games\CS2" -resume
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

# Get latest manifest ID for a depot
./DepotDownloader -app 730 -depot 731 -get-manifest

# Get manifest ID for a specific branch
./DepotDownloader -app 730 -depot 731 -branch beta -get-manifest

# Check required disk space before downloading
./DepotDownloader -app 730 -check-space

# Preview what would be downloaded (dry run)
./DepotDownloader -app 730 -dry-run

# Verbose dry run with file list
./DepotDownloader -app 730 -dry-run -verbose
```

**Example output for `-get-manifest`:**

```
Latest manifest for app 730, depot 731, branch 'public':
  Manifest ID: 7617088375292372759

To download this specific manifest:
  depotdownloader -app 730 -depot 731 -manifest 7617088375292372759
```

**Example output for `-check-space`:**

```
Disk Space Check for app 730:
  Target:     C:\Games\CS2
  Drive:      C:\
  Required:   15.1 GB
  Available:  50.3 GB

? Sufficient disk space available.
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

### JSON Output Mode

For scripting and automation, use `-json` to get structured JSON output:

```powershell
# Get depot list as JSON
./DepotDownloader -app 730 -list-depots -json

# Get branches as JSON
./DepotDownloader -app 730 -list-branches -json

# Get latest manifest ID as JSON
./DepotDownloader -app 730 -depot 731 -get-manifest -json

# Check disk space as JSON
./DepotDownloader -app 730 -check-space -json

# Get download plan as JSON (with file details using -verbose)
./DepotDownloader -app 730 -dry-run -json -verbose

# Download with JSON result
./DepotDownloader -app 730 -json
```

**Example JSON output for `-get-manifest -json`:**

```json
{
  "success": true,
  "appId": 730,
  "depotId": 731,
  "branch": "public",
  "manifestId": 7617088375292372759,
  "found": true
}
```

**Example JSON output for `-check-space -json`:**

```json
{
  "success": true,
  "appId": 730,
  "requiredBytes": 16234567890,
  "requiredSize": "15.1 GB",
  "availableBytes": 53687091200,
  "availableSize": "50.0 GB",
  "targetDrive": "C:\\",
  "hasSufficientSpace": true
}
```

**Example JSON output for `-list-depots -json`:**

```json
{
  "success": true,
  "appId": 730,
  "appName": "Counter-Strike 2",
  "appType": "game",
  "depots": [
    {
      "depotId": 731,
      "name": "Counter-Strike 2 Content",
      "os": "windows",
      "architecture": null,
      "language": null,
      "maxSize": 16234567890,
      "isSharedInstall": false
    }
  ]
}
```

**Example JSON output for `-dry-run -json`:**

```json
{
  "success": true,
  "appId": 730,
  "appName": "Counter-Strike 2",
  "totalDepots": 1,
  "totalFiles": 1432,
  "totalBytes": 16234567890,
  "totalSize": "15.1 GB",
  "depots": [
    {
      "depotId": 731,
      "manifestId": 7617088375292372759,
      "fileCount": 1432,
      "totalBytes": 16234567890,
      "totalSize": "15.1 GB",
      "files": null
    }
  ]
}
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
| `-max-speed <#>` | Maximum download speed in MB/s |
| `-retries <#>` | Maximum retry attempts per chunk (default: 5, use 0 to disable) |
| `-fail-fast` | Stop immediately on first depot failure |
| `-cellid <#>` | Override content server CellID |
| `-use-lancache` | Route downloads through Lancache |
| `-skip-disk-check` | Skip disk space verification before downloading |
| `-resume` | Resume a previously interrupted download |

#### Discovery & Planning

| Parameter | Description |
|-----------|-------------|
| `-list-depots` | List all depots for the app and exit |
| `-list-branches` | List all branches for the app and exit |
| `-get-manifest` | Get the latest manifest ID for a depot (requires `-depot`) |
| `-check-space` | Check required disk space without downloading |
| `-dry-run` | Show download plan without downloading |
| `-verbose`, `-v` | Show detailed output (e.g., file list in dry-run) |

#### Output Options

| Parameter | Description |
|-----------|-------------|
| `-json` | Output results in JSON format for scripting/automation |
| `-no-progress` | Disable the progress bar during downloads |

#### Configuration

| Parameter | Description |
|-----------|-------------|
| `-config <file>` | Load settings from JSON configuration file (CLI args override) |

#### Other

| Parameter | Description |
|-----------|-------------|
| `-debug` | Enable verbose debug logging |
| `-V`, `--version` | Print version information |

---

### Exit Codes

The CLI returns different exit codes to indicate the result of the operation:

| Exit Code | Meaning | Description |
|-----------|---------|-------------|
| `0` | Success | All operations completed successfully |
| `1` | Failure | Operation failed (authentication, no depots downloaded, etc.) |
| `2` | Partial Success | Some depots downloaded successfully, but others failed |

**Examples:**

```powershell
# Check exit code in PowerShell
./DepotDownloader -app 730 -depot 731 732
if ($LASTEXITCODE -eq 0) {
    Write-Host "All depots downloaded successfully"
} elseif ($LASTEXITCODE -eq 2) {
    Write-Host "Some depots failed - check output for details"
} else {
    Write-Host "Download failed completely"
}
```

```bash
# Check exit code in Bash
./DepotDownloader -app 730 -depot 731 732
case $? in
    0) echo "All depots downloaded successfully" ;;
    2) echo "Some depots failed - check output for details" ;;
    *) echo "Download failed completely" ;;
esac
```

---

### Configuration File

You can use a JSON configuration file to store commonly used settings. CLI arguments always override config file values.

**Example `config.json`:**

```json
{
  "app": 730,
  "username": "myaccount",
  "rememberPassword": true,
  "branch": "public",
  "dir": "C:\\Games\\CS2",
  "os": "windows",
  "osarch": "64",
  "language": "english",
  "maxDownloads": 8,
  "validate": true,
  "depots": [731, 732],
  "manifests": [7617088375292372759, 7617088375292372760]
}
```

**Usage:**

```powershell
# Use config file
./DepotDownloader -config config.json

# Override specific settings from config
./DepotDownloader -config config.json -branch beta -dir "C:\Games\CS2-Beta"
```

**Available config options:**

| Property | Type | Description |
|----------|------|-------------|
| `app` | number | Steam AppID |
| `username` | string | Steam username |
| `rememberPassword` | boolean | Save credentials |
| `qr` | boolean | Use QR code login |
| `noMobile` | boolean | Prefer 2FA code |
| `branch` | string | Branch name |
| `branchPassword` | string | Branch password |
| `depots` | number[] | Depot IDs |
| `manifests` | number[] | Manifest IDs |
| `os` | string | Target OS |
| `osarch` | string | Target architecture |
| `language` | string | Target language |
| `allPlatforms` | boolean | Download all platforms |
| `allArchs` | boolean | Download all architectures |
| `allLanguages` | boolean | Download all languages |
| `lowViolence` | boolean | Include low-violence |
| `dir` | string | Install directory |
| `filelist` | string | Path to filelist |
| `validate` | boolean | Verify existing files |
| `manifestOnly` | boolean | Download manifest only |
| `maxDownloads` | number | Concurrent downloads |
| `cellId` | number | Cell ID override |
| `loginId` | number | Login ID |
| `useLancache` | boolean | Use Lancache |
| `skipDiskCheck` | boolean | Skip disk check |
| `debug` | boolean | Enable debug logging |

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

Check what will be downloaded before starting:

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

// Get required disk space
var requiredSpace = await client.GetRequiredDiskSpaceAsync(options);
Console.WriteLine($"Required space: {requiredSpace / 1024.0 / 1024 / 1024:F2} GB");

// Proceed with download (disk space is checked automatically)
await client.DownloadAppAsync(options);
```

### Cancellation Support

Cancel downloads gracefully using a `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource();

var options = new DepotDownloadOptions
{
    AppId = 730,
    InstallDirectory = @"C:\Games\CS2",
    CancellationToken = cts.Token
};

// Cancel after 5 minutes or when user requests
cts.CancelAfter(TimeSpan.FromMinutes(5));

// Or cancel from another thread/task
_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    cts.Cancel(); // Cancel the download
});

try
{
    await client.DownloadAppAsync(options);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Download was cancelled");
}
```

### Progress Reporting

Subscribe to download progress events for real-time updates:

```csharp
using var client = new DepotDownloaderClient();

// Subscribe to progress events
client.DownloadProgress += (sender, e) =>
{
    Console.WriteLine($"Progress: {e.ProgressPercent:F1}%");
    Console.WriteLine($"Downloaded: {e.BytesDownloaded / 1024.0 / 1024:F1} MB / {e.TotalBytes / 1024.0 / 1024:F1} MB");
    Console.WriteLine($"Speed: {e.SpeedBytesPerSecond / 1024.0 / 1024:F1} MB/s");
    Console.WriteLine($"ETA: {e.EstimatedTimeRemaining:hh\\:mm\\:ss}");
    Console.WriteLine($"Files: {e.FilesCompleted} / {e.TotalFiles}");
    Console.WriteLine($"Current: {e.CurrentFile}");
};

client.LoginAnonymous();

var options = new DepotDownloadOptions
{
    AppId = 730,
    InstallDirectory = @"C:\Games\CS2"
};

await client.DownloadAppAsync(options);
```

The `DownloadProgressEventArgs` provides:
- `BytesDownloaded` - Total bytes downloaded so far
- `TotalBytes` - Total bytes to download
- `ProgressPercent` - Progress percentage (0-100)
- `CurrentFile` - Current file being downloaded
- `FilesCompleted` - Number of files completed
- `TotalFiles` - Total number of files
- `SpeedBytesPerSecond` - Current download speed
- `EstimatedTimeRemaining` - Estimated time remaining

### Download Options

You can create download options directly or use the fluent builder pattern:

**Direct instantiation:**

```csharp
var options = new DepotDownloadOptions
{
    AppId = 730,
    Branch = "public",
    InstallDirectory = @"C:\Games\CS2"
};
```

**Fluent builder (recommended):**

```csharp
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .FromBranch("public")
    .ToDirectory(@"C:\Games\CS2")
    .WithDepot(731)
    .ForOs("windows")
    .ForArchitecture("64")
    .ForLanguage("english")
    .WithMaxConcurrency(8)
    .WithVerification()
    .IncludeFilesMatching(@"\.dll$")
    .WithCancellation(cancellationToken)
    .Build();

await client.DownloadAppAsync(options);
```

**Builder methods:**

| Method | Description |
|--------|-------------|
| `ForApp(appId)` | Set the Steam AppID (required) |
| `WithDepot(depotId, manifestId)` | Add a specific depot to download |
| `FromBranch(branch, password)` | Set branch and optional password |
| `ToDirectory(path)` | Set installation directory |
| `ForOs(os)` | Target specific OS (windows/linux/macos) |
| `ForArchitecture(arch)` | Target specific architecture (32/64) |
| `ForLanguage(lang)` | Target specific language |
| `ForAllPlatforms()` | Download all platform depots |
| `ForAllArchitectures()` | Download all architecture depots |
| `ForAllLanguages()` | Download all language depots |
| `IncludeLowViolence()` | Include low-violence depots |
| `IncludeFile(path)` | Add specific file to download |
| `IncludeFiles(paths)` | Add multiple files to download |
| `IncludeFilesMatching(regex)` | Add regex pattern for file matching |
| `WithMaxConcurrency(n)` | Set concurrent downloads (1-64) |
| `WithVerification()` | Verify existing files |
| `ManifestOnly()` | Download manifest metadata only |
| `WithCellId(id)` | Override content server CellID |
| `WithLoginId(id)` | Set login ID for concurrent instances |
| `VerifyDiskSpace(bool)` | Enable/disable disk space check |
| `WithCancellation(token)` | Set cancellation token |
| `WithRetryPolicy(policy)` | Set retry policy for failed downloads |
| `WithRetry(maxRetries, initialDelay, maxDelay)` | Configure custom retry behavior |
| `WithNoRetry()` | Disable retries |
| `WithMaxSpeed(bytesPerSecond)` | Set max download speed in MB/s |
| `WithMaxSpeedMbps(mbPerSecond)` | Set max download speed in MB/s |
| `WithResume(enable)` | Enable resume support for interrupted downloads |
| `WithFailFast(enable)` | Stop on first depot failure (default: continue) |
| `Build()` | Create the options (throws if AppId not set) |

**Retry Policy:**
```csharp
// Use predefined policies
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithRetryPolicy(RetryPolicy.Aggressive) // 10 retries with longer delays
    .Build();

// Or custom retry settings
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithRetry(maxRetries: 10, 
               initialDelay: TimeSpan.FromSeconds(2),
               maxDelay: TimeSpan.FromMinutes(1))
    .Build();

// Disable retries
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithNoRetry()
    .Build();
```

**Resume Support:**
```csharp
// Enable resume for large downloads that may be interrupted
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .ToDirectory(@"C:\Games\CS2")
    .WithResume()
    .Build();

// If download is interrupted (cancelled, crashed, network error),
// run again with the same options to continue where it left off
await client.DownloadAppAsync(options);
```

**Fail-Fast Mode:**
```csharp
// Stop immediately on first depot failure
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithFailFast()
    .Build();

// Default behavior: continue downloading other depots even if one fails
// The result will indicate which depots succeeded/failed
var result = await client.DownloadAppAsync(options);
if (result.FailedDepots > 0)
{
    Console.WriteLine($"{result.FailedDepots} depot(s) failed:");
    foreach (var failure in result.Failures)
    {
        Console.WriteLine($"  Depot {failure.DepotId}: {failure.ErrorMessage}");
    }
}
```

**Speed Limiting:**
```csharp
// Limit download speed to 10 MB/s
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithMaxSpeedMbps(10)
    .Build();

// Or in bytes per second
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithMaxSpeed(10 * 1024 * 1024) // 10 MB/s
    .Build();
```

**Options Validation:**

The builder validates options when `Build()` is called. You can also validate options directly:

```csharp
// Builder validates automatically
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .ForOs("windows")
    .ForAllPlatforms()  // Throws: Cannot specify both ForOs and ForAllPlatforms
    .Build();

// Direct validation for manually created options
var options = new DepotDownloadOptions
{
    AppId = 730,
    DownloadAllPlatforms = true,
    Os = "windows"  // Invalid combination
};

// Throws ArgumentException with details
options.Validate();

// Or check without throwing
if (!options.TryValidate(out var errorMessage))
{
    Console.WriteLine($"Invalid options: {errorMessage}");
}
```

**Full options reference:**

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
    
    // Retry and throttling
    RetryPolicy = RetryPolicy.Default,    // exponential backoff with 5 retries
    MaxBytesPerSecond = null,             // null = unlimited
    
    // Resume support
    Resume = false,                       // set to true to enable resume
    
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
    var result = await client.DownloadAppAsync(options);
    
    // Check for partial failures
    if (result.PartialSuccess)
    {
        Console.WriteLine($"Downloaded {result.SuccessfulDepots}/{result.DepotResults.Count} depots");
        foreach (var failure in result.Failures)
        {
            Console.WriteLine($"Failed: Depot {failure.DepotId} - {failure.ErrorMessage}");
        }
        
        Console.WriteLine($"Successfully downloaded {result.SuccessfulDepots} depot(s)");
        Console.WriteLine($"Total downloaded: {result.TotalBytesDownloaded} bytes");
    }
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

**Download Result Details:**

The `DownloadResult` object returned by `DownloadAppAsync` provides detailed information about the download:

```csharp
public class DownloadResult
{
    public uint AppId { get; set; }
    public int SuccessfulDepots { get; }      // Number of depots that downloaded successfully
    public int FailedDepots { get; }          // Number of depots that failed
    public bool AllSucceeded { get; }         // True if all depots succeeded
    public bool AllFailed { get; }            // True if all depots failed
    public bool PartialSuccess { get; }       // True if some succeeded and some failed
    
    public ulong TotalBytesDownloaded { get; set; }
    public ulong TotalBytesCompressed { get; set; }
    public int TotalFilesDownloaded { get; set; }
    
    public List<DepotDownloadResult> Successes { get; }  // Details of successful depots
    public List<DepotDownloadResult> Failures { get; }   // Details of failed depots
}
```

Each `DepotDownloadResult` contains:
- `DepotId` - The depot ID
- `ManifestId` - The manifest ID downloaded
- `Success` - Whether the download succeeded
- `ErrorMessage` - Error message if failed
- `BytesDownloaded` - Bytes downloaded for this depot
- `FilesDownloaded` - Files downloaded for this depot

### FAQ

### How do I check if I have enough disk space?

```powershell
# CLI - use check-space command
./DepotDownloader -app 730 -check-space

# Or use dry-run to see total size
./DepotDownloader -app 730 -dry-run
```

```csharp
// Library - disk space is verified automatically before download
// To get required space without downloading:
var requiredSpace = await client.GetRequiredDiskSpaceAsync(options);
Console.WriteLine($"Need {requiredSpace} bytes");

```

### What happens if a depot download fails?

By default, DepotDownloader continues downloading other depots even if one fails. You'll get a summary at the end:

```powershell
# CLI - download multiple depots with failure handling
./DepotDownloader -app 730

# If depot 731 fails, depot 732 will still be attempted
# Exit code: 0 = all succeeded, 1 = all failed, 2 = partial success
```

```csharp
// Library - check the result for failures
var result = await client.DownloadAppAsync(options);

if (result.PartialSuccess)
{
    Console.WriteLine($"Downloaded {result.SuccessfulDepots}/{result.DepotResults.Count} depots");
    foreach (var failure in result.Failures)
    {
        Console.WriteLine($"Failed: Depot {failure.DepotId} - {failure.ErrorMessage}");
    }
}

// Use FailFast = true to stop on first failure
options.FailFast = true;
```

### How do I limit download speed?

```powershell
# CLI - limit to 10 MB/s
./DepotDownloader -app 730 -max-speed 10
```

```csharp
// Library - limit to 10 MB/s
var options = DepotDownloadOptionsBuilder.Create()
    .ForApp(730)
    .WithMaxSpeedMbps(10)
    .Build();
```

### How do I get a specific manifest ID?

```powershell
# CLI - get latest manifest for a depot
./DepotDownloader -app 730 -depot 731 -get-manifest

# Get manifest from a specific branch
./DepotDownloader -app 730 -depot 731 -branch beta -get-manifest
```

```csharp
// Library
var manifestId = await client.GetLatestManifestIdAsync(
    appId: 730,
    depotId: 731,
    branch: "public"
);

if (manifestId.HasValue)
{
    Console.WriteLine($"Latest manifest: {manifestId.Value}");
}
```

---

## License

# Feature Coverage Summary

This document provides a complete overview of all features in DepotDownloader and their documentation coverage.

## ? Complete Feature Coverage

### CLI Commands

| Command | Documentation | Examples | JSON Output |
|---------|--------------|----------|-------------|
| `-app` | ? | ? | ? |
| `-depot` | ? | ? | ? |
| `-manifest` | ? | ? | ? |
| `-branch` | ? | ? | ? |
| `-list-depots` | ? | ? | ? |
| `-list-branches` | ? | ? | ? |
| `-get-manifest` | ? | ? | ? |
| `-check-space` | ? | ? | ? |
| `-dry-run` | ? | ? | ? |
| `-pubfile` | ? | ? | N/A |
| `-ugc` | ? | ? | N/A |

### CLI Options

| Option | CLI Docs | Library Docs | Examples |
|--------|----------|--------------|----------|
| Authentication (`-username`, `-password`, `-qr`) | ? | ? | ? |
| Platform filtering (`-os`, `-osarch`, `-language`, `-all-*`) | ? | ? | ? |
| Download control (`-max-downloads`, `-max-speed`, `-retries`) | ? | ? | ? |
| Error handling (`-fail-fast`) | ? | ? | ? |
| Resume support (`-resume`) | ? | ? | ? |
| Disk space checking (`-check-space`, `-skip-disk-check`) | ? | ? | ? |
| File filtering (`-filelist`, regex support) | ? | ? | ? |
| Validation (`-validate`) | ? | ? | ? |
| Output modes (`-json`, `-verbose`, `-no-progress`) | ? | N/A | ? |
| Configuration (`-config`) | ? | N/A | ? |
| Debugging (`-debug`) | ? | ? | ? |

### Library APIs

| API Method | Docs | CLI Usage | Examples |
|------------|------|-----------|----------|
| `Login` | ? | `-username` | ? |
| `LoginAnonymous` | ? | (default) | ? |
| `LoginWithQrCode` | ? | `-qr` | ? |
| `Logout` | ? | (implicit) | ? |
| `GetAppInfoAsync` | ? | `-list-depots/branches` | ? |
| `GetDepotsAsync` | ? | `-list-depots` | ? |
| `GetBranchesAsync` | ? | `-list-branches` | ? |
| `GetLatestManifestIdAsync` | ? | `-get-manifest` | ? |
| `GetDownloadPlanAsync` | ? | `-dry-run` | ? |
| `GetRequiredDiskSpaceAsync` | ? | `-check-space` | ? |
| `DownloadAppAsync` | ? | (default) | ? |
| `DownloadPublishedFileAsync` | ? | `-pubfile` | ? |
| `DownloadUgcAsync` | ? | `-ugc` | ? |
| `EnableDebugLogging` | ? | `-debug` | ? |

### Builder Pattern

| Method | Documentation | Examples |
|--------|--------------|----------|
| `ForApp` | ? | ? |
| `WithDepot` | ? | ? |
| `FromBranch` | ? | ? |
| `ToDirectory` | ? | ? |
| `ForOs/Architecture/Language` | ? | ? |
| `ForAllPlatforms/Archs/Languages` | ? | ? |
| `IncludeLowViolence` | ? | ? |
| `IncludeFile/Files/FilesMatching` | ? | ? |
| `WithMaxConcurrency` | ? | ? |
| `WithVerification` | ? | ? |
| `ManifestOnly` | ? | ? |
| `WithCellId/LoginId` | ? | ? |
| `VerifyDiskSpace` | ? | ? |
| `WithRetryPolicy/Retry/NoRetry` | ? | ? |
| `WithMaxSpeed/MaxSpeedMbps` | ? | ? |
| `WithResume` | ? | ? |
| `WithFailFast` | ? | ? |
| `WithCancellation` | ? | ? |
| `Build` | ? | ? |

### Return Types

| Type | Documentation | Usage Examples |
|------|--------------|----------------|
| `AppInfo` | ? | ? |
| `DepotInfo` | ? | ? |
| `BranchInfo` | ? | ? |
| `DownloadPlan` | ? | ? |
| `DownloadResult` | ? | ? |
| `DepotDownloadResult` | ? | ? |
| `DownloadProgressEventArgs` | ? | ? |
| `InsufficientDiskSpaceException` | ? | ? |
| `ContentDownloaderException` | ? | ? |

### Advanced Features

| Feature | CLI Support | Library Support | Documented |
|---------|------------|----------------|------------|
| Multi-depot downloads | ? | ? | ? |
| Per-depot success/failure tracking | ? | ? | ? |
| Fail-fast vs continue-on-error | ? | ? | ? |
| Resume interrupted downloads | ? | ? | ? |
| Speed limiting | ? | ? | ? |
| Retry configuration | ? | ? | ? |
| Disk space checking | ? | ? | ? |
| Progress reporting | ? (progress bar) | ? (events) | ? |
| Cancellation support | N/A | ? | ? |
| Custom user interface | N/A | ? | ? |
| Regex file filtering | ? | ? | ? |
| Lancache support | ? | ? | ? |
| QR code authentication | ? | ? | ? |
| Credential persistence | ? | ? | ? |
| JSON output mode | ? | N/A | ? |
| Config file support | ? | N/A | ? |
| Exit codes | ? | N/A | ? |

## Documentation Locations

### README.md Sections

1. **Installation** - CLI and Library installation instructions
2. **CLI Usage** - Command-line examples and parameter reference
3. **Library Usage** - Programmatic API usage with code examples
4. **API Reference** - Complete API surface documentation
5. **FAQ** - Common questions and troubleshooting

### README Coverage by Feature

- ? Authentication (anonymous, username/password, QR code, credential persistence)
- ? Content selection (apps, depots, manifests, branches, workshop items)
- ? Platform filtering (OS, architecture, language)
- ? Download options (concurrency, speed, retries, fail-fast, resume)
- ? Discovery commands (list depots/branches, get manifest, check space, dry-run)
- ? File filtering (specific files, regex patterns)
- ? Progress reporting (CLI progress bar, library events)
- ? Error handling (exceptions, per-depot failures, exit codes)
- ? JSON output mode (all commands support JSON)
- ? Configuration files (JSON config with CLI overrides)
- ? Custom interfaces (IUserInterface implementation)
- ? Debug logging (SteamKit2 diagnostics)

## Examples Coverage

### CLI Examples

- ? Basic download
- ? Authenticated download
- ? Specific depot/manifest
- ? Branch download
- ? Custom directory
- ? Workshop items
- ? List depots/branches
- ? Get manifest ID
- ? Check disk space
- ? Dry run
- ? JSON output
- ? Config file usage
- ? Speed limiting
- ? Retry configuration
- ? Fail-fast mode
- ? Resume downloads
- ? Exit code checking

### Library Examples

- ? Quick start
- ? All authentication methods
- ? Query APIs (app info, depots, branches, manifest)
- ? Download planning
- ? Disk space checking
- ? Progress reporting
- ? Cancellation
- ? Error handling (all exception types)
- ? Builder pattern (all methods)
- ? Direct options instantiation
- ? Retry policies
- ? Speed limiting
- ? Resume support
- ? Fail-fast mode
- ? Per-depot results
- ? Custom user interface
- ? Workshop content

## CLI as Library Usage Example

The CLI (`Program.cs`) serves as a complete usage example demonstrating:

- ? Client creation and disposal
- ? All authentication methods
- ? All query APIs
- ? Download with all options
- ? Progress reporting
- ? Error handling
- ? Result processing
- ? Custom user interface (ConsoleUserInterface)
- ? JSON serialization
- ? Exit code handling

## Summary

**100% Feature Coverage** - All library APIs and CLI commands are:
- ? Fully documented in README
- ? Demonstrated with examples
- ? Used by the CLI (where applicable)
- ? Covered in FAQ entries
- ? Included in JSON output (where applicable)

The documentation provides complete coverage for:
- All public APIs
- All CLI commands and options
- All error scenarios
- All advanced features
- All usage patterns (direct, builder, query-only)
- All output formats (text, JSON)

#!/usr/bin/env pwsh
# Build script for GitHubArchiver.Daemon
# Produces self-contained single-file executables for Windows and Linux

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish/GitHubArchiver.Daemon"
)

$ErrorActionPreference = "Stop"
$ProjectPath = "GitHubArchiver.Daemon/GitHubArchiver.Daemon.csproj"

# Clean output directory
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}

Write-Host "Building GitHubArchiver.Daemon..." -ForegroundColor Cyan

# Common publish arguments for minimal output
$commonArgs = @(
    "--configuration", $Configuration,
    "-p:PublishSingleFile=true",
    "-p:SelfContained=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false"
)

# Build for Linux x64
Write-Host "`nPublishing for Linux x64..." -ForegroundColor Yellow
dotnet publish $ProjectPath `
    --runtime linux-x64 `
    --output "$OutputDir/linux-x64" `
    @commonArgs

# Build for Windows x64
Write-Host "`nPublishing for Windows x64..." -ForegroundColor Yellow
dotnet publish $ProjectPath `
    --runtime win-x64 `
    --output "$OutputDir/win-x64" `
    @commonArgs

Write-Host "`nBuild complete!" -ForegroundColor Green
Write-Host "Output locations:"
Write-Host "  Linux:   $OutputDir/linux-x64/"
Write-Host "  Windows: $OutputDir/win-x64/"

# Show file sizes
Write-Host "`nOutput files:" -ForegroundColor Cyan
Get-ChildItem -Path $OutputDir -Recurse -File | 
    Select-Object @{N='Path';E={$_.FullName.Replace((Get-Location).Path + '\', '')}}, @{N='Size (MB)';E={[math]::Round($_.Length/1MB, 2)}} |
    Format-Table -AutoSize

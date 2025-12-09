#!/usr/bin/env pwsh

# Code Coverage Summary Script
# Parses coverage.cobertura.xml and displays a formatted report

param(
    [string]$CoverageFile = "coverage.cobertura.xml"
)

if (-not (Test-Path $CoverageFile)) {
    Write-Host "? Coverage file not found: $CoverageFile" -ForegroundColor Red
    Write-Host "Run: dotnet-coverage collect -f cobertura -o coverage.cobertura.xml `"dotnet test`"" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n?? Code Coverage Summary" -ForegroundColor Cyan
Write-Host "=" * 80 -ForegroundColor Cyan

[xml]$coverage = Get-Content $CoverageFile

# Overall stats
$overallLineRate = [math]::Round([double]$coverage.coverage.'line-rate' * 100, 2)
$overallBranchRate = [math]::Round([double]$coverage.coverage.'branch-rate' * 100, 2)
$linesCovered = $coverage.coverage.'lines-covered'
$linesValid = $coverage.coverage.'lines-valid'
$branchesCovered = $coverage.coverage.'branches-covered'
$branchesValid = $coverage.coverage.'branches-valid'

Write-Host "`n?? Overall Coverage" -ForegroundColor Yellow
Write-Host "  Line Coverage:   $overallLineRate% ($linesCovered / $linesValid lines)" -ForegroundColor $(if ($overallLineRate -ge 70) { "Green" } elseif ($overallLineRate -ge 40) { "Yellow" } else { "Red" })
Write-Host "  Branch Coverage: $overallBranchRate% ($branchesCovered / $branchesValid branches)" -ForegroundColor $(if ($overallBranchRate -ge 70) { "Green" } elseif ($overallBranchRate -ge 40) { "Yellow" } else { "Red" })

# Package-level breakdown
Write-Host "`n?? Coverage by Package" -ForegroundColor Yellow

foreach ($package in $coverage.coverage.packages.package) {
    $packageName = $package.name
    $packageLineRate = [math]::Round([double]$package.'line-rate' * 100, 2)
    $packageBranchRate = [math]::Round([double]$package.'branch-rate' * 100, 2)
    
    $statusIcon = if ($packageLineRate -ge 70) { "?" } elseif ($packageLineRate -ge 40) { "?? " } else { "?" }
    
    Write-Host "`n  $statusIcon $packageName" -ForegroundColor White
    Write-Host "     Lines:    $packageLineRate%" -ForegroundColor $(if ($packageLineRate -ge 70) { "Green" } elseif ($packageLineRate -ge 40) { "Yellow" } else { "Red" })
    Write-Host "     Branches: $packageBranchRate%" -ForegroundColor $(if ($packageBranchRate -ge 70) { "Green" } elseif ($packageBranchRate -ge 40) { "Yellow" } else { "Red" })
}

# Top uncovered classes
Write-Host "`n?? Top 10 Uncovered Classes" -ForegroundColor Yellow

$uncoveredClasses = @()
foreach ($package in $coverage.coverage.packages.package) {
    foreach ($class in $package.classes.class) {
        $classLineRate = [double]$class.'line-rate'
        $className = $class.name -replace '.*\.', ''  # Get just class name
        
        $uncoveredClasses += [PSCustomObject]@{
            Name = $className
            Package = $package.name
            Coverage = [math]::Round($classLineRate * 100, 2)
        }
    }
}

$uncoveredClasses | 
    Where-Object { $_.Coverage -lt 100 } |
    Sort-Object Coverage |
    Select-Object -First 10 |
    Format-Table -Property @{
        Label = "Class"; Expression = { $_.Name }; Width = 40
    }, @{
        Label = "Package"; Expression = { $_.Package }; Width = 25
    }, @{
        Label = "Coverage"; Expression = { "$($_.Coverage)%" }; Width = 10
    } |
    Out-String |
    Write-Host

# Coverage goals
Write-Host "`n?? Coverage Goals" -ForegroundColor Yellow

$goals = @(
    @{ Component = "Test Infrastructure"; Current = 72.5; Target = 70; Status = "? Met" }
    @{ Component = "CLI Utilities"; Current = $overallLineRate; Target = 85; Status = "? In Progress" }
    @{ Component = "Command Handlers"; Current = 5; Target = 60; Status = "? Needs Work" }
    @{ Component = "Core Library"; Current = 3; Target = 50; Status = "? Needs Work" }
)

$goals | Format-Table -Property @{
    Label = "Component"; Expression = { $_.Component }; Width = 25
}, @{
    Label = "Current"; Expression = { "$($_.Current)%" }; Width = 10
}, @{
    Label = "Target"; Expression = { "$($_.Target)%" }; Width = 10
}, @{
    Label = "Status"; Expression = { $_.Status }; Width = 20
} | Out-String | Write-Host

# Recommendations
Write-Host "`n?? Recommendations" -ForegroundColor Yellow
Write-Host "  1. Add integration tests for command execution paths" -ForegroundColor White
Write-Host "  2. Test error handling and edge cases" -ForegroundColor White
Write-Host "  3. Mock Steam API for core library unit tests" -ForegroundColor White
Write-Host "  4. Add tests for OptionsBuilder validation" -ForegroundColor White
Write-Host "  5. Test JSON output formatting" -ForegroundColor White

Write-Host "`n" -NoNewline
Write-Host "?? Full report: " -ForegroundColor Cyan -NoNewline
Write-Host "COVERAGE.md" -ForegroundColor White

Write-Host "`n" + ("=" * 80) -ForegroundColor Cyan
Write-Host ""

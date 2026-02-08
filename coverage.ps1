#!/usr/bin/env pwsh
<#
.SYNOPSIS
    テストカバレッジレポートを生成するスクリプト
.DESCRIPTION
    dotnet test でカバレッジを収集し、ReportGenerator で HTML レポートを生成します。
.PARAMETER Open
    レポート生成後にブラウザで開く
.PARAMETER Threshold
    カバレッジしきい値（%）。下回るとエラー終了
#>
param(
    [switch]$Open,
    [int]$Threshold = 0
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$resultsDir = Join-Path $repoRoot "TestResults"
$reportDir = Join-Path $resultsDir "CoverageReport"
$solution = Join-Path $repoRoot "SqlSchemaDef.sln"
$runsettings = Join-Path $repoRoot "coverlet.runsettings"

# Clean previous results
if (Test-Path $resultsDir) {
    Remove-Item -Recurse -Force $resultsDir
}

Write-Host "Running tests with coverage collection..." -ForegroundColor Cyan

dotnet test $solution `
    --collect "XPlat Code Coverage" `
    --settings $runsettings `
    --results-directory $resultsDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Find coverage file
$coverageFile = Get-ChildItem -Path $resultsDir -Recurse -Filter "coverage.cobertura.xml" | Select-Object -First 1

if (-not $coverageFile) {
    Write-Host "No coverage file found." -ForegroundColor Red
    exit 1
}

Write-Host "Coverage file: $($coverageFile.FullName)" -ForegroundColor Green

# Install ReportGenerator if not already installed
$toolList = dotnet tool list --global 2>&1
if ($toolList -notmatch "dotnet-reportgenerator-globaltool") {
    Write-Host "Installing ReportGenerator..." -ForegroundColor Cyan
    dotnet tool install --global dotnet-reportgenerator-globaltool
}

# Generate report
Write-Host "Generating HTML coverage report..." -ForegroundColor Cyan

reportgenerator `
    "-reports:$($coverageFile.FullName)" `
    "-targetdir:$reportDir" `
    "-reporttypes:Html;TextSummary;MarkdownSummaryGithub"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Report generation failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Display summary
$summaryFile = Join-Path $reportDir "Summary.txt"
if (Test-Path $summaryFile) {
    Write-Host ""
    Write-Host "=== Coverage Summary ===" -ForegroundColor Cyan
    Get-Content $summaryFile
    Write-Host ""
}

# Check threshold
if ($Threshold -gt 0) {
    $summaryContent = Get-Content $summaryFile -Raw
    if ($summaryContent -match "Line coverage:\s*([\d.]+)%") {
        $lineCoverage = [double]$Matches[1]
        if ($lineCoverage -lt $Threshold) {
            Write-Host "Line coverage ($lineCoverage%) is below threshold ($Threshold%)." -ForegroundColor Red
            exit 1
        }
        Write-Host "Line coverage ($lineCoverage%) meets threshold ($Threshold%)." -ForegroundColor Green
    }
}

Write-Host "Report generated at: $reportDir" -ForegroundColor Green

# Open in browser
if ($Open) {
    $indexFile = Join-Path $reportDir "index.html"
    if (Test-Path $indexFile) {
        Start-Process $indexFile
    }
}

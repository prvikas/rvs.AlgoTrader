<#
.SYNOPSIS
    Runs all rvs.AlgoTrader test suites locally on Windows.

.DESCRIPTION
    Executes unit tests, architecture tests, integration tests, and Playwright E2E tests.
    Requires: .NET 9 SDK, Docker Desktop (for integration tests), Node.js 22+

.PARAMETER Suite
    Which test suite to run: "unit", "arch", "integration", "e2e", "all" (default: "all")

.PARAMETER NoBuild
    Skip the dotnet build step (faster if already built)

.EXAMPLE
    .\run-tests.ps1
    .\run-tests.ps1 -Suite unit
    .\run-tests.ps1 -Suite e2e
#>
param(
    [ValidateSet("unit", "arch", "integration", "e2e", "all")]
    [string]$Suite = "all",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Failed = @()

function Write-Header($text) {
    Write-Host "`n$("="*60)" -ForegroundColor Cyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host "$("="*60)`n" -ForegroundColor Cyan
}

function Invoke-Test($name, $command, $workDir = $Root) {
    Write-Header $name
    Push-Location $workDir
    try {
        Invoke-Expression $command
        if ($LASTEXITCODE -ne 0) {
            $script:Failed += $name
            Write-Host "FAILED: $name" -ForegroundColor Red
        } else {
            Write-Host "PASSED: $name" -ForegroundColor Green
        }
    } catch {
        $script:Failed += $name
        Write-Host "ERROR in $name`: $_" -ForegroundColor Red
    } finally {
        Pop-Location
    }
}

# ─── Build ────────────────────────────────────────────────────────────────────
if (-not $NoBuild -and $Suite -ne "e2e") {
    Write-Header "Building solution"
    dotnet build "$Root\rvs.AlgoTrader.sln" --configuration Release --no-incremental
    if ($LASTEXITCODE -ne 0) { Write-Host "Build FAILED — aborting" -ForegroundColor Red; exit 1 }
}

# ─── Unit Tests ───────────────────────────────────────────────────────────────
if ($Suite -in @("unit", "all")) {
    Invoke-Test "Unit Tests (xUnit)" `
        "dotnet test `"$Root\tests\rvs.AlgoTrader.Tests.Unit\rvs.AlgoTrader.Tests.Unit.csproj`" --configuration Release --no-build --logger `"console;verbosity=normal`" --results-directory `"$Root\test-results\unit`""
}

# ─── Architecture Tests ───────────────────────────────────────────────────────
if ($Suite -in @("arch", "all")) {
    Invoke-Test "Architecture Tests (NetArchTest)" `
        "dotnet test `"$Root\tests\rvs.AlgoTrader.Tests.Architecture\rvs.AlgoTrader.Tests.Architecture.csproj`" --configuration Release --no-build --logger `"console;verbosity=normal`""
}

# ─── Integration Tests (needs Docker) ─────────────────────────────────────────
if ($Suite -in @("integration", "all")) {
    Write-Header "Checking Docker for integration tests"
    $dockerRunning = docker ps 2>&1 | Select-String "CONTAINER"
    if ($dockerRunning) {
        Invoke-Test "Integration Tests (Testcontainers)" `
            "dotnet test `"$Root\tests\rvs.AlgoTrader.IntegrationTests\rvs.AlgoTrader.IntegrationTests.csproj`" --configuration Release --no-build --logger `"console;verbosity=normal`" --results-directory `"$Root\test-results\integration`""
    } else {
        Write-Host "Docker not running — skipping integration tests" -ForegroundColor Yellow
    }
}

# ─── Frontend Vitest ──────────────────────────────────────────────────────────
if ($Suite -in @("unit", "all")) {
    Invoke-Test "Frontend Unit Tests (Vitest)" `
        "npm run test" `
        "$Root\frontend"
}

# ─── E2E Playwright ───────────────────────────────────────────────────────────
if ($Suite -in @("e2e", "all")) {
    Write-Header "Playwright E2E Tests"

    # Check if API is running
    $apiRunning = $false
    try {
        $null = Invoke-WebRequest -Uri "http://localhost:5000/health" -TimeoutSec 3 -ErrorAction Stop
        $apiRunning = $true
    } catch { }

    if (-not $apiRunning) {
        Write-Host "Starting API with docker-compose..." -ForegroundColor Yellow
        Start-Process -FilePath "docker" -ArgumentList "compose up -d algotrader-api" -WorkingDirectory $Root -Wait
        Start-Sleep 10
    }

    # Start frontend dev server in background
    $frontendJob = Start-Job -ScriptBlock {
        Set-Location $using:Root\frontend
        npm run dev -- --port 5173
    }
    Start-Sleep 5

    try {
        # Install Playwright browsers if not already done
        Push-Location "$Root\tests\rvs.AlgoTrader.Tests.UI"
        dotnet build --configuration Release
        $env:PLAYWRIGHT_BROWSERS_PATH = "$Root\.playwright"
        pwsh bin/Release/net9.0/playwright.ps1 install chromium --with-deps 2>$null

        Invoke-Test "E2E Tests - Dashboard (Playwright)" `
            "dotnet test `"$Root\tests\rvs.AlgoTrader.Tests.UI\rvs.AlgoTrader.Tests.UI.csproj`" --configuration Release --no-build --logger `"console;verbosity=normal`" --results-directory `"$Root\test-results\e2e`""
        Pop-Location
    } finally {
        Stop-Job $frontendJob -ErrorAction SilentlyContinue
        Remove-Job $frontendJob -ErrorAction SilentlyContinue
    }
}

# ─── Summary ──────────────────────────────────────────────────────────────────
Write-Header "TEST SUMMARY"
if ($Failed.Count -eq 0) {
    Write-Host "All test suites PASSED!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "FAILED suites ($($Failed.Count)):" -ForegroundColor Red
    $Failed | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

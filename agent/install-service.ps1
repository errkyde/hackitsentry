#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs or uninstalls the HackIT Sentry Agent as a Windows Service.

.DESCRIPTION
    Installs the agent binary as a Windows Service that starts automatically on boot.
    Run with -Uninstall to remove the service.

.PARAMETER ServerUrl
    The URL of the HackIT Sentry server (e.g. https://sentry.example.com).

.PARAMETER CheckinIntervalMinutes
    How often the agent checks in with the server. Default: 30

.PARAMETER Uninstall
    Remove the service instead of installing it.

.EXAMPLE
    .\install-service.ps1 -ServerUrl https://sentry.example.com

.EXAMPLE
    .\install-service.ps1 -Uninstall
#>
param(
    [string]$ServerUrl = "",
    [int]$CheckinIntervalMinutes = 30,
    [switch]$Uninstall
)

$ServiceName = "HackITSentryAgent"
$DisplayName = "HackIT Sentry Agent"
$Description = "Monitors system information and reports to the HackIT Sentry server."
$InstallDir  = "C:\Program Files\HackIT Sentry\Agent"
$ExeName     = "HackITSentry.Agent.exe"
$ExePath     = Join-Path $InstallDir $ExeName

# ── Uninstall ──────────────────────────────────────────────────────────────────
if ($Uninstall) {
    Write-Host "Stopping and removing service '$ServiceName'..." -ForegroundColor Yellow

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -ne "Stopped") {
            Stop-Service -Name $ServiceName -Force
            Start-Sleep -Seconds 2
        }
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Service removed." -ForegroundColor Green
    } else {
        Write-Host "Service '$ServiceName' not found." -ForegroundColor Gray
    }

    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force
        Write-Host "Installation directory removed: $InstallDir" -ForegroundColor Green
    }

    Write-Host "Uninstall complete." -ForegroundColor Green
    exit 0
}

# ── Install ────────────────────────────────────────────────────────────────────
if (-not $ServerUrl) {
    $ServerUrl = Read-Host "Enter the HackIT Sentry server URL (e.g. https://sentry.example.com)"
}
if (-not $ServerUrl) {
    Write-Error "ServerUrl is required."
    exit 1
}

# Locate agent binary next to this script
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceExe  = Join-Path $ScriptDir $ExeName

if (-not (Test-Path $SourceExe)) {
    Write-Error "Agent binary not found at '$SourceExe'. Run publish.ps1 first or place the script next to the published binary."
    exit 1
}

# Stop existing service if present
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "Stopping existing service..." -ForegroundColor Yellow
    if ($svc.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# Copy files to install directory
Write-Host "Copying files to '$InstallDir'..." -ForegroundColor Cyan
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}
Copy-Item -Path "$ScriptDir\*" -Destination $InstallDir -Recurse -Force

# Write appsettings.json with provided configuration
$AppSettings = @{
    SentryAgent = @{
        ServerUrl               = $ServerUrl
        ApiKey                  = ""
        CheckinIntervalMinutes  = $CheckinIntervalMinutes
    }
    Logging = @{
        LogLevel = @{
            Default = "Information"
        }
    }
} | ConvertTo-Json -Depth 5

$AppSettingsPath = Join-Path $InstallDir "appsettings.json"
Set-Content -Path $AppSettingsPath -Value $AppSettings -Encoding UTF8
Write-Host "Configuration written to '$AppSettingsPath'." -ForegroundColor Cyan

# Create service
Write-Host "Creating Windows Service '$ServiceName'..." -ForegroundColor Cyan
$result = sc.exe create $ServiceName `
    binPath= "`"$ExePath`"" `
    start= auto `
    DisplayName= "$DisplayName"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service: $result"
    exit 1
}

sc.exe description $ServiceName "$Description" | Out-Null

# Configure recovery: restart on failure (3 attempts, 1 min cooldown)
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# Create state directory
$StateDir = "C:\ProgramData\HackITSentry"
if (-not (Test-Path $StateDir)) {
    New-Item -ItemType Directory -Path $StateDir -Force | Out-Null
}

# Start service
Write-Host "Starting service..." -ForegroundColor Cyan
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2

$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "  Service : $ServiceName"
Write-Host "  Status  : $($svc.Status)"
Write-Host "  Server  : $ServerUrl"
Write-Host "  Interval: $CheckinIntervalMinutes min"
Write-Host ""
Write-Host "The agent will register with the server automatically." -ForegroundColor Cyan
Write-Host "Approve the pending device request in the HackIT Sentry web interface." -ForegroundColor Cyan

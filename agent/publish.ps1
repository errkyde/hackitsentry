#Requires -PSEdition Core
<#
.SYNOPSIS
    Publishes the HackIT Sentry Agent as a self-contained Windows executable.

.DESCRIPTION
    Builds and publishes the agent for Windows x64 into the 'publish/' directory.
    Copy the contents of 'publish/' to the target machine and run install-service.ps1.

.PARAMETER Runtime
    Target runtime identifier. Default: win-x64

.PARAMETER Output
    Output directory. Default: .\publish

.EXAMPLE
    .\publish.ps1

.EXAMPLE
    .\publish.ps1 -Runtime win-x86 -Output C:\Temp\agent-build
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Output  = ".\publish"
)

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ScriptDir "HackITSentry.Agent.csproj"

if (-not (Test-Path $ProjectFile)) {
    Write-Error "Project file not found: $ProjectFile"
    exit 1
}

Write-Host "Publishing HackIT Sentry Agent..." -ForegroundColor Cyan
Write-Host "  Runtime : $Runtime"
Write-Host "  Output  : $Output"
Write-Host ""

dotnet publish $ProjectFile `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $Output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

# Copy installer script next to the binary
Copy-Item -Path (Join-Path $ScriptDir "install-service.ps1") -Destination $Output -Force

Write-Host ""
Write-Host "Build successful!" -ForegroundColor Green
Write-Host "Output: $Output"
Write-Host ""
Write-Host "To install on a target machine:" -ForegroundColor Yellow
Write-Host "  1. Copy the '$Output' directory to the target machine"
Write-Host "  2. Open PowerShell as Administrator"
Write-Host "  3. Run: .\install-service.ps1 -ServerUrl https://your-server"

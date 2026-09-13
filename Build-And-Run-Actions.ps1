Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$importScript = Join-Path $projectRoot "Import-Actions.ps1"
$projectFile = Join-Path $projectRoot "MarmaladeDesktopPet.csproj"
$applicationPath = Join-Path $projectRoot "bin/Debug/net8.0-windows/MarmaladeDesktopPet.exe"

Write-Host "[1/4] Preparing ClippyCat action workflow..."
Set-Location -LiteralPath $projectRoot

foreach ($scriptPath in @($PSCommandPath, $importScript)) {
    try { Unblock-File -LiteralPath $scriptPath -ErrorAction Stop }
    catch { Write-Host "  Could not unblock $scriptPath; continuing with the current policy." -ForegroundColor Yellow }
}

Write-Host "[2/4] Importing and validating action artwork..."
& $importScript

Write-Host "[3/4] Building ClippyCat..."
dotnet build $projectFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED. ClippyCat was not launched." -ForegroundColor Red
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw "Build succeeded but application was not found: $applicationPath"
}

Write-Host "[4/4] Launching ClippyCat..."
Start-Process -FilePath $applicationPath -WorkingDirectory $projectRoot
Write-Host "Action import, build, and launch completed successfully." -ForegroundColor Green

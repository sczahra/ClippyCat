[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$publishScript = Join-Path $projectRoot "Publish-ClippyCat.ps1"
$installerScript = Join-Path $projectRoot "installer\ClippyCat.iss"
$publishRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\publish\win-x64"))
$installerRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\installer"))
$expectedInstaller = Join-Path $installerRoot "ClippyCatSetup-2.5.0.exe"

Write-Host "[1/6] Locating ClippyCat packaging files..."
foreach ($requiredPath in @($publishScript, $installerScript)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required packaging file not found: $requiredPath"
    }
}

Write-Host "[2/6] Checking for the Inno Setup compiler..."
$compilerCandidates = [Collections.Generic.List[string]]::new()
$pathCommand = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($null -ne $pathCommand) {
    $compilerCandidates.Add($pathCommand.Source)
}
foreach ($candidate in @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)) {
    if (-not $compilerCandidates.Contains($candidate)) {
        $compilerCandidates.Add($candidate)
    }
}
$isccPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($isccPath)) {
    Write-Host "  Inno Setup compiler was not found; publish validation will still run." -ForegroundColor Yellow
}
else {
    Write-Host "  Inno Setup compiler: $isccPath"
}

Write-Host "[3/6] Building the standalone publish folder..."
& $publishScript

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    Write-Host ""
    Write-Host "Standalone publish succeeded: $publishRoot" -ForegroundColor Green
    Write-Host "Installer source is ready: $installerScript" -ForegroundColor Green
    Write-Host "ClippyCatSetup.exe was not created because ISCC.exe is not installed." -ForegroundColor Yellow
    Write-Host "Install Inno Setup 6 separately, then rerun .\Build-Installer.ps1."
    Write-Host "Checked compiler locations:"
    $compilerCandidates | ForEach-Object { Write-Host "  $_" }
    exit 2
}

Write-Host "[4/6] Compiling the Inno Setup installer..."
New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null
& $isccPath "/DSourceDir=$publishRoot" "/DOutputDir=$installerRoot" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

Write-Host "[5/6] Validating installer output..."
if (-not (Test-Path -LiteralPath $expectedInstaller -PathType Leaf)) {
    throw "Installer compiler completed but expected output was not found: $expectedInstaller"
}

$installerFile = Get-Item -LiteralPath $expectedInstaller
$installerMiB = [Math]::Round($installerFile.Length / 1MB, 2)

Write-Host "[6/6] Installer build complete." -ForegroundColor Green
Write-Host "Installer: $expectedInstaller"
Write-Host "Size: $installerMiB MiB ($($installerFile.Length) bytes)"

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectFile = Join-Path $projectRoot "MarmaladeDesktopPet.csproj"
$publishScript = Join-Path $projectRoot "Publish-ClippyCat.ps1"
$installerScript = Join-Path $projectRoot "installer\ClippyCat.iss"
$publishRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\publish\win-x64"))
$installerRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\installer"))

Write-Host "[1/7] Locating ClippyCat packaging files and version..."
foreach ($requiredPath in @($projectFile, $publishScript, $installerScript)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required packaging file not found: $requiredPath"
    }
}

[xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
$versionNode = $projectXml.SelectSingleNode("/Project/PropertyGroup/Version")
$appVersion = if ($null -eq $versionNode) { $null } else { $versionNode.InnerText }

[Version]$parsedVersion = $null
if ([string]::IsNullOrWhiteSpace($appVersion) -or
    -not [Version]::TryParse($appVersion, [ref]$parsedVersion) -or
    $parsedVersion.Build -lt 0 -or
    $parsedVersion.Revision -ge 0) {
    throw "The project Version must be a valid three-part version: $appVersion"
}

$appVersion = "$($parsedVersion.Major).$($parsedVersion.Minor).$($parsedVersion.Build)"
$installerFileName = "ClippyCatSetup-$appVersion.exe"
$expectedInstaller = Join-Path $installerRoot $installerFileName
$checksumPath = "$expectedInstaller.sha256"
Write-Host "  Version: $appVersion"

Write-Host "[2/7] Checking for the Inno Setup compiler..."
$compilerCandidates = [Collections.Generic.List[string]]::new()
$pathCommand = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($null -ne $pathCommand) {
    $compilerCandidates.Add($pathCommand.Source)
}
foreach ($candidate in @(
    (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Programs\Inno Setup 6\ISCC.exe"),
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

Write-Host "[3/7] Building the standalone publish folder..."
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

Write-Host "[4/7] Compiling the Inno Setup installer..."
New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null
& $isccPath "/DMyAppVersion=$appVersion" "/DSourceDir=$publishRoot" "/DOutputDir=$installerRoot" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

Write-Host "[5/7] Validating installer output..."
if (-not (Test-Path -LiteralPath $expectedInstaller -PathType Leaf)) {
    throw "Installer compiler completed but expected output was not found: $expectedInstaller"
}

$installerFile = Get-Item -LiteralPath $expectedInstaller
$installerMiB = [Math]::Round($installerFile.Length / 1MB, 2)
$installerSha256 = (Get-FileHash -LiteralPath $expectedInstaller -Algorithm SHA256).Hash.ToUpperInvariant()

Write-Host "[6/7] Writing SHA-256 release checksum..."
[IO.File]::WriteAllText(
    $checksumPath,
    "$installerSha256  $installerFileName`r`n",
    [Text.Encoding]::ASCII)

if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw "Checksum file was not created: $checksumPath"
}

$checksumLine = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
if ($checksumLine -ne "$installerSha256  $installerFileName") {
    throw "Checksum file validation failed: $checksumPath"
}

Write-Host "[7/7] Installer build complete." -ForegroundColor Green
Write-Host "Installer: $expectedInstaller"
Write-Host "Size: $installerMiB MiB ($($installerFile.Length) bytes)"
Write-Host "SHA256: $installerSha256"
Write-Host "Checksum file: $checksumPath"

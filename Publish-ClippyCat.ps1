[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectFile = Join-Path $projectRoot "MarmaladeDesktopPet.csproj"
$importScript = Join-Path $projectRoot "Import-Actions.ps1"
$publishParent = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\publish"))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $publishParent "win-x64"))
$expectedExecutable = Join-Path $publishRoot "MarmaladeDesktopPet.exe"

Write-Host "[1/6] Locating ClippyCat project..."
if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "Project file not found: $projectFile"
}
if (-not (Test-Path -LiteralPath $importScript -PathType Leaf)) {
    throw "Action importer not found: $importScript"
}

$safePublishPrefix = $publishParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $publishRoot.StartsWith($safePublishPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Resolved publish path is outside the expected artifacts directory: $publishRoot"
}

Write-Host "[2/6] Cleaning generated win-x64 publish output..."
if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

Write-Host "[3/6] Importing and validating production action artwork..."
Push-Location -LiteralPath $projectRoot
try {
    & $importScript

    Write-Host "[4/6] Publishing self-contained Release application for win-x64..."
    dotnet publish $projectFile `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishRoot `
        --source https://api.nuget.org/v3/index.json `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Host "[5/6] Validating standalone runtime layout..."
$requiredPaths = @(
    $expectedExecutable,
    (Join-Path $publishRoot "Assets"),
    (Join-Path $publishRoot "Assets\actions\actions.json"),
    (Join-Path $publishRoot "Assets\Marmalade\spritesheet.png"),
    (Join-Path $publishRoot "Assets\Merry\spritesheet.png")
)

foreach ($requiredPath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Publish validation failed; required path is missing: $requiredPath"
    }
}

$developmentFrames = @(
    Get-ChildItem -LiteralPath (Join-Path $publishRoot "Assets\actions") -Recurse -File -Filter "*.png"
)
if ($developmentFrames.Count -gt 0) {
    throw "Publish validation failed; development action-source PNGs were included."
}

$packagedScripts = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File -Filter "*.ps1")
if ($packagedScripts.Count -gt 0) {
    throw "Publish validation failed; developer PowerShell scripts were included."
}

$publishFiles = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File)
$publishBytes = ($publishFiles | Measure-Object -Property Length -Sum).Sum
$publishMiB = [Math]::Round($publishBytes / 1MB, 2)

Write-Host "[6/6] Standalone publish complete." -ForegroundColor Green
Write-Host "Publish folder: $publishRoot"
Write-Host "Executable: $expectedExecutable"
Write-Host "Payload: $($publishFiles.Count) files, $publishMiB MiB"

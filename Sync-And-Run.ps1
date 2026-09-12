$ErrorActionPreference = "Stop"

Write-Host "[1/7] Opening ClippyCat..."
$Project = "$HOME\Documents\Projects\MarmaladeDesktopPet"
Set-Location $Project

Write-Host "[2/7] Checking for local source changes..."
$Changes = git status --porcelain
if ($Changes) {
    Write-Host ""
    Write-Host "STOP: Local changes detected. Git will not pull over them." -ForegroundColor Yellow
    Write-Host ""
    $Changes | ForEach-Object { Write-Host $_ }
    Write-Host ""
    Write-Host "Resolve, commit, or restore these files before syncing." -ForegroundColor Yellow
    exit 2
}

Write-Host "[3/7] Fetching GitHub main..."
git fetch origin main

Write-Host "[4/7] Pulling latest changes..."
git pull --ff-only origin main

Write-Host "[5/7] Showing exact revision..."
$Commit = git rev-parse --short HEAD
Write-Host "Git commit: $Commit"

$VersionLine = Select-String -Path ".\Program.cs" -Pattern "Version [0-9]+\.[0-9]+" | Select-Object -First 1
if ($VersionLine) {
    Write-Host ("App version line: " + $VersionLine.Line.Trim())
}

Write-Host "[6/7] Building..."
dotnet build
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[7/7] Starting ClippyCat..."
dotnet run

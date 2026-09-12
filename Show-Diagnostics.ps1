$ErrorActionPreference = "Stop"

$Log = Join-Path $env:LOCALAPPDATA "MarmaladeDesktopPet\diagnostics.log"

Write-Host "[1/3] Finding ClippyCat diagnostics..."

if (-not (Test-Path $Log)) {
    Write-Host ""
    Write-Host "No diagnostics log exists yet."
    Write-Host "Run ClippyCat for a minute first, then try again."
    return
}

Write-Host "[2/3] Reading the newest diagnostic lines..."
$Text = Get-Content $Log -Tail 180

Write-Host "[3/3] Copying diagnostics to the clipboard..."
$Text | Set-Clipboard

Write-Host ""
Write-Host "=== CLIPPYCAT DIAGNOSTICS COPIED ==="
Write-Host "Paste them into ChatGPT."
Write-Host ""
$Text

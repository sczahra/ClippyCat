# ClippyCat

A lightweight Windows desktop pet featuring Marmalade and Merry.

## Current baseline

- .NET 8 WinForms
- Transparent always-on-top desktop pet window
- System tray controls and pet switching
- Autonomous idle / walking / curiosity behaviors
- Click / pet reactions
- Pickup dragging with airborne pose and landing reaction
- Marmalade and Merry sprite atlases
- v1.3 diagnostic layer for behavior and animation analysis

## Development workflow

This repository is the source of truth for ClippyCat going forward. Changes can be made from ChatGPT through the connected GitHub repository, then pulled to the Windows development machine for testing.

Typical local update cycle:

```powershell
Write-Host "[1/3] Opening ClippyCat..."
Set-Location "$HOME\Documents\Projects\MarmaladeDesktopPet"

Write-Host "[2/3] Pulling latest changes..."
git pull

Write-Host "[3/3] Building and running..."
dotnet build
dotnet run
```

## Diagnostics

ClippyCat v1.3 writes structured diagnostic lines to:

```text
%LOCALAPPDATA%\MarmaladeDesktopPet\diagnostics.log
```

The diagnostic layer records pet/state changes, energy bands, pickup start/end, click activity, periodic summaries, and sprite-atlas frame-size variation.

To copy the most recent diagnostics for pasting into ChatGPT:

```powershell
.\Show-Diagnostics.ps1
```

The helper prints visible progress, copies the latest diagnostic block to the clipboard, and also displays it in PowerShell.

## Requirements

- Windows 10/11
- .NET 8 SDK for development
- No external NuGet packages required

## Run

```powershell
dotnet run
```

## Build

```powershell
dotnet build
```

Current instrumented baseline: **v1.3**.

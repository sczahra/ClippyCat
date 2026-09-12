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
- Marmalade and Merry share identical behavior logic; only artwork and active pet identity differ
- Runtime diagnostic logging
- GitHub Actions Windows build check

## Development workflow

This repository is the source of truth for ClippyCat going forward. Changes can be made from ChatGPT through the connected GitHub repository, then pulled to the Windows development machine for testing.

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

## Diagnostics

ClippyCat writes runtime diagnostics to:

```text
%LOCALAPPDATA%\MarmaladeDesktopPet\diagnostics.log
```

To copy the recent diagnostic output to the clipboard:

```powershell
.\Show-Diagnostics.ps1
```

The v1.4 diagnostic layer records:

- pet/state transitions and whether the previous state was interrupted early
- planned state duration versus observed duration
- energy checkpoints
- pickup/drag events
- click events
- atlas row variation
- Merry-to-Marmalade frame-by-frame size, center, and bottom alignment differences

This lets animation cleanup target actual outlier frames instead of treating normal pose changes as errors.

Current app behavior baseline: **v1.4**.
Current diagnostic layer: **v1.4**.

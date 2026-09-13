# ClippyCat

A lightweight Windows desktop pet featuring Marmalade and Merry.

## Current baseline

- .NET 8 WinForms
- Transparent layered desktop pet window with an optional always-on-top setting
- System tray controls and pet switching
- Native Settings window with persistent local preferences
- Controlled pet sizes and Low / Normal / High wandering frequency
- Optional per-user Windows startup entry
- Autonomous idle / walking / curiosity behaviors
- Click / pet reactions
- Manifest-driven dedicated action rows; Marmalade Stretch is available while Merry Stretch and Scratch for both pets remain pending artwork
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

## Adding a new animation

1. Create `Assets/actions/<Pet>/<action>/` and add sequential frame files named `0.png`, `1.png`, and so on. Frames must be transparent PNGs no larger than 192x208; smaller frames are centered without cropping.
2. If the action name is new, add matching names to `PetAction` and `PetState`; then add or update its timing, frame count, atlas row, eligibility, and per-pet availability in `Assets/actions/actions.json`.
3. Run `./Import-Actions.ps1 -WhatIf` to validate the manifest, source frames, and intended atlas changes without writing files.
4. Run `./Import-Actions.ps1`, then `dotnet build` and test the action. `./Build-And-Run-Actions.ps1` performs those three steps together.

If one pet has no approved artwork, leave that pet disabled in the manifest. Its direct tray command can remain visible and report that the animation is coming soon, while random and autonomous selection exclude it automatically.

## Settings

Open **Settings...** from the tray menu to choose the preferred pet, always-on-top behavior, pet size (75%, 100%, 125%, or 150%), wandering frequency, and whether ClippyCat starts with Windows.

Preferences are stored locally in `%LOCALAPPDATA%\MarmaladeDesktopPet\settings.json`. The Windows startup option uses only the current user's startup registry entry and does not require administrator rights.

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

Current app behavior baseline: **v1.9**.
Current diagnostic layer: **v1.4**.

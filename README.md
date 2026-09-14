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
- Fully manifest-driven visual actions; Ear Twitch, Sleepy Blink, Look Behind, Loaf, Knead, Head Shake, Scratch, Yawn, Sit/Settle, Tail Flick, and Roll/Flop work for both pets through the source-frame pipeline. Stretch works for Marmalade, while Merry Stretch remains pending artwork.
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

## Building a standalone release

The development workflow imports source action frames, builds the app, and launches the development executable:

```powershell
.\Build-And-Run-Actions.ps1
```

To create a self-contained Windows x64 folder release:

```powershell
.\Publish-ClippyCat.ps1
```

This runs the action importer and publishes to `artifacts\publish\win-x64`. The published folder includes the generated production atlases and action manifest, but excludes development source frames and scripts.

To compile the normal Windows installer after installing Inno Setup 6:

```powershell
.\Build-Installer.ps1
```

The installer is written to `artifacts\installer\ClippyCatSetup-2.5.0.exe`. Generated publish and installer payloads are intentionally excluded from Git.

## Installing ClippyCat

Run `ClippyCatSetup-2.5.0.exe`, then launch **ClippyCat** from the Start Menu or the optional Desktop shortcut. The installed copy is self-contained: end users do not need the .NET runtime, Git, PowerShell, the source repository, or development tools.

ClippyCat installs per user under `%LOCALAPPDATA%\Programs\ClippyCat`, appears in Windows Installed Apps / Add or Remove Programs, and includes an uninstaller. Uninstall removes application files but preserves preferences and diagnostics under `%LOCALAPPDATA%\MarmaladeDesktopPet`. To remove user data manually, exit ClippyCat and delete that folder.

The release stages are deliberately separate:

- Development: source frames → importer → development build
- Release: source tree → importer → self-contained publish → installer
- Runtime: installed executable launches directly with no repository or importer dependency

## Adding a visual action without code changes

1. Create `Assets/actions/<Pet>/<action-id>/` for each supported pet.
2. Add sequential transparent RGBA PNG frames named `0.png`, `1.png`, and so on. Frames must be no larger than 192x208; smaller frames are centered without cropping.
3. Add one entry to `Assets/actions/actions.json` with a stable lowercase kebab-case `id`, user-facing `displayName`, atlas row, frame/timing metadata, tray and Do Something eligibility, autonomous weight, and both pets' availability.
4. Run `./Build-And-Run-Actions.ps1` to import the frames, build ClippyCat, and launch it.
5. Test the tray command, Do Something eligibility, autonomous frequency, and both pets' availability behavior.

Ordinary stationary sprite actions require no `Program.cs` edit or enum addition. The tray, playback, Do Something pool, and autonomous pool discover them from the manifest. If one pet has no approved artwork, leave that pet disabled; its tray command remains visible and reports that the animation is coming soon, while random and autonomous selection exclude it automatically.

Actions with custom movement or mechanics—such as mouse-directed travel, jumping physics, dragging, sounds, desktop interaction, or scripted paths—may still require code.

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

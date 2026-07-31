# 3x3 Centar Scoreboard

Branded offline Windows scoreboard software for 3x3 Centar, built with .NET 10, WPF, and MVVM.

Published by **3X3 Centar** — [3x3centar.com](https://3x3centar.com) · [3x3centar@gmail.com](mailto:3x3centar@gmail.com)

The application opens two native windows:

- `ControllerWindow` stays on the operator laptop and owns every score, foul, clock, game, display, and audio command.
- `ScoreboardWindow` is read-only and automatically uses a secondary display in borderless fullscreen mode, with a safe preview fallback on a single monitor.

## What is implemented

- FIBA-style defaults: 10-minute game, 12-second shot clock, regular-time
  21-point alert, first-to-2 overtime with no win-by-two requirement, and team
  foul penalty thresholds.
- Coin-toss winner/choice determines the opening possession and reserves the
  opposite choice for a potential overtime.
- Persistent tournament catalogs with team rosters and optional team/player
  images; new games select both teams from the tournament.
- Group, qualifier, quarterfinal, semifinal, and final classifications, with
  groups 1–20 or A–Z shown beneath the public-display logo.
- One authoritative event-sourced match session shared by both windows.
- Drift-resistant monotonic clocks with tenths display and one-shot expiration events.
- Synchronized regulation clocks: starting or pausing either applies to both;
  in overtime, the same controls run only the shot clock while the game clock
  displays `OT`.
- Button and keyboard control through the same MVVM commands.
- Undo by appending `EventRevertedEvent`; history is never deleted.
- Automatic five-second shot-clock warning and recorded expiry buzzer,
  manual/game-clock buzzers, mute/volume settings, and a shot-clock sound test.
- Responsive 720p/1080p/4K public display, blackout, fullscreen, monitor selection, hot-plug fallback, and DPI-aware placement.
- Every committed game action is queued into an ordered background JSON journal;
  atomic replacement and one-second running-clock snapshots keep crash recovery current.
- A loopback-only OBS browser overlay at `http://127.0.0.1:5050/overlay` receives
  coalesced scoreboard state through background SSE publishing; slow clients,
  serialization, and server failures never block match operation.
- A newest-first Saved Games library can reopen finished matches on their final
  screen or recover unfinished matches paused so play can continue.
- Manual JSON export and full event history.
- Windows sleep/display suppression while a game is active.
- Local settings and diagnostic logs that are outside the install directory and survive upgrades.
- 3x3 Centar logo and branding on both the operator controller and public display.
- Self-contained x64 publish, portable ZIP workflow, and WiX MSI packaging.

## Requirements

Development requires the .NET SDK version selected by [global.json](global.json): .NET 10.0.302 or a compatible later 10.0 patch. The published app is self-contained and does not require a separately installed .NET runtime.

The runtime target is 64-bit Windows 10 version 1607 (build 14393) or later, including Windows 11. The application manifest declares Windows 10/11 compatibility, and both the portable app and MSI reject older Windows releases with a clear message.

Microsoft currently provides official .NET 10 support on Windows 10 only for supported LTSC/Enterprise releases. See [Install .NET on Windows](https://learn.microsoft.com/dotnet/core/install/windows#supported-versions) for the current operating-system support matrix.

## Build and test

```powershell
dotnet restore ThreeByThree.Centar.Scoreboard.slnx --locked-mode
dotnet build ThreeByThree.Centar.Scoreboard.slnx --configuration Release --no-restore
dotnet test ThreeByThree.Centar.Scoreboard.slnx --configuration Release --no-build
```

Run the development build:

```powershell
dotnet run --project src\ThreeByThree.Centar.Scoreboard.Wpf\ThreeByThree.Centar.Scoreboard.Wpf.csproj
```

Create release artifacts:

```powershell
.\scripts\build-release.ps1 -Version 1.0.0
```

Outputs are written to `artifacts\`:

- `3x3Centar.Scoreboard-1.0.0-win-x64.zip`
- `installer\3x3Centar.Scoreboard.Setup.msi`
- `publish\win-x64\`

## Publish a GitHub Release

Push a semantic-version tag to build, test, and publish the MSI and portable ZIP:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The same release workflow can be run manually from the repository's **Actions**
page by entering a version such as `1.0.0`. Each release also includes
`SHA256SUMS.txt` for verifying the downloaded files.

## User-data locations

```text
Documents\3x3 Centar Scoreboard\Games\YYYY-MM-DD\
└── (legacy completed games)

Documents\3x3 Centar Scoreboard\Games\
└── YYYY-MM-DD_match-id.json

%LOCALAPPDATA%\3x3 Centar Scoreboard\
├── Logs\scoreboard-YYYYMMDD.log
├── Tournaments\<tournament-id>.json
├── Tournaments\assets\<tournament-id>\<team-or-player-id>.<extension>
└── settings.json
```

The installer owns only application binaries and shortcuts. It does not remove
settings, logs, tournament rosters/images, or saved-game JSON during uninstall.

New installations use the `3x3 Centar Scoreboard` folders. Existing data from
the previous product name is detected automatically so settings, recovery data,
logs, and completed games are not orphaned by the rename.

## Documentation

- [Implementation plan](docs/IMPLEMENTATION_PLAN.md)
- [Architecture and persistence](docs/ARCHITECTURE.md)
- [Operator guide and keyboard map](docs/OPERATOR_GUIDE.md)
- [Acceptance checklist](docs/ACCEPTANCE_CHECKLIST.md)

## License

3x3 Centar Scoreboard is completely free to download, use, copy, modify,
and distribute for any purpose, including commercial use. It is released
under the [Zero-Clause BSD license](LICENSE).

The bundled Roboto Condensed typeface is distributed under the SIL Open Font
License 1.1. Its license notice is included with the
[font assets](src/ThreeByThree.Centar.Scoreboard.Wpf/Assets/Fonts).

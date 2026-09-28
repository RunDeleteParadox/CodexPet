# CodexPetHub

[English](README.md) | [Français](README.fr.md)

Windows tray application for a USB CodexPet. The Hub turns Codex lifecycle states into expressions, manages device preferences and owns the serial connection.

## Components

~~~text
Codex → CodexPetPlugin → local Named Pipe → CodexPetHub → USB / serial → CodexPetFirmware
~~~

This component lives beside `CodexPetPlugin` and `CodexPetFirmware` in the common CodexPet repository. Run this guide's commands from the `CodexPetHub` directory. The firmware runs on an M5Stack StopWatch. The plugin interprets lifecycle events; the Hub controls presentation, sleep and reconnection; the firmware renders the avatar. Conversation content is not sent to the Pet.

**Current Hub version: 1.2.0.** IPC generation 2; serial protocol 1; firmware 1.1.0 or later. Completion sounds require firmware 1.1.7 with audio capability.

## Build and start

See the complete [English user guide](../docs/USER-GUIDE.md) or [French user guide](../docs/USER-GUIDE.fr.md) for installation, everyday use and troubleshooting.

Requirements: Windows and the .NET 10 SDK. Run from this repository:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 -Publish
.\artifacts\publish\CodexPetHub.exe
~~~

The script builds the solution, runs software tests and creates a self-contained Windows x64 application. Keep all published files together, including the `fr` resource directory. A user running this distribution does not need the .NET SDK.

Double-click the tray icon to open Settings. Settings, Diagnostics and License and credits share one window with navigation on the left. Each tray-menu entry selects the corresponding page; Settings is the bold default action. Closing the window keeps the Hub in the tray. Right-click for state tests, brightness, sleep, wake, reconnect, settings and Quit. The Hub discovers compatible USB devices and verifies their firmware identity. If discovery finds several candidates, select a COM port in Settings.

## Settings and languages

**Settings → Language** offers **English** and **French**. Selecting a language previews the whole window. Switching pages preserves pending edits. Save persists them without closing the window and updates the tray within one second. Cancel reloads saved preferences; closing the window discards pending edits. No restart is needed.

The first language follows the Windows installation UI language: French for a French installation, English otherwise. Existing configurations without a language keep their other preferences and use this default.

Preferences are stored in `%USERPROFILE%\.codex-pet-hub\config.json`:

- Language, device name and paired hardware identity.
- USB port, reconnection interval and brightness.
- Completion sound: None, Fanfare · two notes, or Voice · TA-DA!
- Launch at sign-in and sleep on Windows lock/display off.

To hear a sound, save the preference and choose **Test a state → Response complete**. The sound plays on the Pet. Older compatible firmware keeps working without audio commands.

## Presentation

The displayed Codex states are Idle, Thinking, Working and Waiting for you. Response complete, Error, Stopped and Waking up are short reactions. Response complete means that the response finished; it does not certify answer correctness or completion of an entire project.

A completion reaction lasts five seconds. During that time the Hub remembers the latest Codex base state and discards other reactions. It then displays the latest base. Sleep and manual tests can take priority. Error, Stop and Wake can be interrupted by later activity.

A base-state test normally lasts ten seconds. Enable **Test a state → Keep test active** to inspect it for longer. Codex activity continues to be tracked. Disable this mode to return to the latest Codex state.

Sleep can be requested manually, by Windows lock, display off or system suspend. The latest Codex state remains in memory. Wake restores it when all sleep causes have cleared. USB remains active during Pet sleep; this is not a deep-sleep or battery-life feature.

## Diagnostics

Diagnostics shows processing and IPC health, firmware and device identity, requested/reported states, audio status and optional power telemetry. Reported values do not replace observing the real screen and speaker.

Local files in `%USERPROFILE%\.codex-pet-hub`:

| File | Purpose |
| --- | --- |
| `config.json` | Persistent preferences |
| `hub.jsonl`, `hub.log` | Structured and readable rotating logs |
| `status.json` | Health snapshot, refreshed independently of the serial loop |
| `session.json` | Process heartbeat and clean-exit information |

Each log format keeps its active file and up to 14 archives, rotating at 1 MB or a date change. High activity can shorten the number of days retained. **Export diagnostics** writes a ZIP containing local logs, configuration and status. It includes local paths and device identifiers; inspect it before sharing. Prompts, transcripts, raw tool results and memory dumps are excluded.

USB/protocol failures trigger reconnect attempts. A stopped or stalled processing loop is reported explicitly; quit and restart the Hub if that happens. The app does not restart itself after a requested exit.

## Development and checks

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1
dotnet run --project tests/CodexPetHub.UiTests -c Release -- --render artifacts/ui-previews
~~~

The tests use private data directories, synthetic events and fake/disabled serial. The sibling `CodexPetPlugin/tests/Test-Integration.ps1` verifies plugin integration; `-HubExe` accepts another layout.

The physical test scripts are separate and require an explicit device ID and port. Quit the Hub and any serial monitor before using them. No build or software test flashes the Pet.

Developer options include `--data <directory>`, `--pipe <name>`, `--no-serial`, `--headless --seconds <n>` and `--render-preview <directory>`. Use both private data and pipe arguments for isolated runs.

## Scope and documentation

One Pet, USB/serial and the conversation selected by the plugin. No Wi-Fi or multi-Pet support. A new non-compact session start or user prompt selects the displayed conversation; another conversation can work in the background.

See [the documentation index](docs/README.md), [protocols](docs/PROTOCOLS.md), [localization](docs/LOCALIZATION.md) and [contribution guide](CONTRIBUTING.md). Current user guides are available in both languages. Private working notes and historical delivery reports are kept locally under ignored `.local/notes`.

## License

Copyright (C) 2026 **RunDeleteParadox and contributors**.

CodexPetHub is licensed under **GNU AGPL version 3 or later** (`AGPL-3.0-or-later`),
without warranty. See [LICENSE](LICENSE), [NOTICE](NOTICE), [third-party notices](THIRD_PARTY_NOTICES.md)
and [corresponding source](SOURCE.md). The Hub, plugin and firmware use the same
project license; upstream and dependency notices remain in force.

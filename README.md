# CodexPet

[English](README.md) | [Français](README.fr.md)

Codex activity on a M5Stack StopWatch: a Windows Hub, a Codex lifecycle plugin
and firmware for a small animated companion. Maintained by **RunDeleteParadox**.

The firmware is based on **KK / M5Stack StopWatch Avatar by trentct and
contributors**: [Trentct/m5stack-stopwatch-avatar](https://github.com/Trentct/m5stack-stopwatch-avatar).
Its original AGPL license, credits and Git history are preserved.

## Components

| Directory | Purpose |
| --- | --- |
| [CodexPetHub](CodexPetHub/README.md) | Windows tray application, English/French settings, device connection and presentation |
| [CodexPetPlugin](CodexPetPlugin/README.md) | Native Codex hooks and semantic lifecycle events over a local Named Pipe |
| [CodexPetFirmware](CodexPetFirmware/README.md) | ESP32-S3 firmware, avatar animation, power controls and completion audio |

```text
Codex → Plugin → local Named Pipe → Hub → USB/serial → Pet
```

All three components live in this single Git repository. Clone it once; no
submodule initialization is needed. The current scope is one USB Pet on Windows.
Prompts, conversation text and raw tool output are not sent to the device.

## Build and use

Start with the complete [user guide](docs/USER-GUIDE.md): requirements, firmware,
Hub, plugin installation, daily use, updates and troubleshooting.

The Hub requires Windows and the .NET 10 SDK to build. Normal plugin operation
uses Windows PowerShell 5.1. Development tests also need Python 3.11 or later.

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File CodexPetHub/scripts/Build.ps1 -Publish
.\CodexPetHub\artifacts\publish\CodexPetHub.exe
```

Use the Hub's Settings to select English or French, name the Pet and configure
brightness, sound and power preferences. Configuration is saved outside the
checkout under the user's profile.

For firmware prerequisites and flashing, follow the
[firmware guide](CodexPetFirmware/README.md). Quit the Hub before another program
opens the serial port. The [plugin guide](CodexPetPlugin/README.md) explains
hooks, setup helpers and completion detection. Its legacy migration helper is
specific to an existing personal marketplace; a public plugin distribution
route is included as a local marketplace in `CodexPetPlugin/.agents/plugins`.
The user guide gives the install commands from a downloaded checkout.

## Checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test.ps1
pio run --project-dir CodexPetFirmware
python -m unittest discover -s CodexPetFirmware/tests -p "test_*.py"
```

The first command runs the Hub and plugin software tests with fake or disabled
serial. GitHub Actions runs these Windows checks and a separate firmware build.
Physical appearance, audio and hardware interactions require separate device
verification.

## Documentation

- [User guide](docs/USER-GUIDE.md) / [Guide utilisateur](docs/USER-GUIDE.fr.md)
- [Documentation index](docs/README.md)
- [Credits and provenance](CREDITS.md)
- [Contributing](CONTRIBUTING.md)
- [Licensing and distribution](CodexPetHub/docs/LICENSING.md)
- [Hub guides](CodexPetHub/docs/README.md)
- [Firmware behavior](CodexPetFirmware/docs/CODEXPET.md)

User guides are maintained in English and French. Upstream technical documents
are preserved. Private audits and delivery notes live under ignored `.local/notes`,
separate from public documentation.

## License

**AGPL-3.0-or-later**. See [LICENSE](LICENSE), [NOTICE](NOTICE),
[CREDITS.md](CREDITS.md) and component third-party notices. Third-party material
retains its own terms. Corresponding-source and dependency materials must
accompany binary distributions as explained in the component SOURCE.md files.

Community project; not affiliated with or endorsed by OpenAI or M5Stack.

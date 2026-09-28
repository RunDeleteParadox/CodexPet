# CodexPetFirmware

[English](README.md) | [Français](README.fr.md) | [Upstream 简体中文](README.zh-CN.md)

M5Stack StopWatch firmware for CodexPet, derived from [Trentct/m5stack-stopwatch-avatar](https://github.com/Trentct/m5stack-stopwatch-avatar), baseline `2049632`. The upstream procedural avatar engine, attribution and **AGPL-3.0-or-later** license are retained.

CodexPetHub controls the device over USB/serial. The firmware knows graphical states and device commands; it receives no Codex prompts, transcripts or tool identifiers.

## Features

- Procedural eyes and expression timelines for the 466 × 466 circular AMOLED.
- Persistent Idle, Thinking, Working and Waiting presentations.
- Success, Error, Stop and Wake reactions, with return to the requested base.
- Brightness control and panel sleep/wake while USB remains active.
- Factory eFuse device identity and optional power/reset diagnostics.
- Two embedded completion cues: a mascot “TA-DA!” voice and a two-note fanfare, plus silent mode.
- Preserved upstream touch, button and IMU interaction.

**Current firmware: 1.1.7; serial protocol: 1.** CodexPetHub and CodexPetPlugin are sibling components in the common CodexPet Git repository. Run this guide's commands from the CodexPetFirmware directory. One USB Pet is the current scope.

## Build

For the complete installation sequence, see the [English user guide](../docs/USER-GUIDE.md) or [French user guide](../docs/USER-GUIDE.fr.md).

Use PlatformIO Core 6.1.18. The ESP32 platform and M5 libraries are pinned in [platformio.ini](platformio.ini).

~~~sh
pio run
~~~

The configuration uses `espressif32 @ 6.12.0`, Arduino and the `esp32s3box` board definition for the StopWatch hardware. See the preserved [hardware baseline](docs/HARDWARE_BASELINE.md) for pin/address details and the original verification record.

## Flash

Quit CodexPetHub and other serial owners before flashing. Use a USB data cable and identify the intended device/port.

~~~sh
pio run --target upload --upload-port COM_PORT
pio device monitor --port COM_PORT --baud 115200
~~~

Replace `COM_PORT` with the actual port. Uploading replaces the device firmware. Keep any needed backup and verify the target first. Upstream recovery guidance is to hold reset for about two seconds and release when the green LED turns on if automatic upload does not start.

## Software tests

The Windows native test harness needs Visual Studio C++ Build Tools and uses `vswhere` to locate them:

~~~powershell
cmd /c tests\Run-Native.cmd
python -m unittest discover -s tests -p "test_*.py"
~~~

The native harness runs the real avatar engine against a software framebuffer. These images are simulations, not photos of the StopWatch.

`tests/device_validation.py` is a separate device test requiring `--port`, `--device-id` and `--output`, with pyserial installed. It takes exclusive serial access. A passing build or protocol test does not establish visual quality, speaker audibility or battery life.

## Audio and source assets

The WAV source files and generation procedure are documented in [assets/audio/README.md](assets/audio/README.md). Run `python scripts/embed_success_audio.py` to regenerate the embedded C++ data. The source WAVs must accompany a corresponding-source release.

`SOUND none|fanfare|voice` selects a cue without playing it. Only `REACT success` plays the selected cue once. Startup is silent until the Hub configures a preference. See [CodexPet behavior](docs/CODEXPET.md).

## Documentation and validation history

The current fork guide is [CODEXPET.md](docs/CODEXPET.md), with a [French version](docs/CODEXPET.fr.md). Original upstream engineering notes remain public technical references. Private animation/audio delivery reports are archived locally under ignored `.local/notes`; users do not need them to build or operate the firmware.

## License and attribution

**GNU Affero General Public License, version 3 or later.** CodexPet changes:
Copyright (C) 2026 **RunDeleteParadox and contributors**. See [LICENSE](LICENSE),
[NOTICE](NOTICE) and [third-party notices](THIRD_PARTY_NOTICES.md).
The upstream engine is KK / M5Stack StopWatch Avatar. Its original project and
repository history are preserved.

Hub, plugin and firmware all use AGPL-3.0-or-later. When distributing a binary,
provide the matching corresponding source, build materials and applicable
notices. See [source distribution](SOURCE.md) for the firmware release materials.

Community project; not affiliated with or endorsed by M5Stack or OpenAI.

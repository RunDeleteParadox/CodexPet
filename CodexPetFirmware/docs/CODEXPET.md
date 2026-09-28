# CodexPet firmware behavior

[English](CODEXPET.md) | [FranÃ§ais](CODEXPET.fr.md)

This guide describes the 1.1.7 fork. The fork preserves the upstream timeline/interpolation engine, blink system, eye projection, dirty-rectangle renderer and local touch/IMU interaction. Idle and the original Sleepy presentation remain part of the engine.

## Responsibilities

CodexPetHub owns Codex presentation policy, reaction timing, preferences, sleep causes and the USB connection. The firmware renders a requested base or reaction, handles local input and reports device state. It does not interpret Codex tool events or conversation content.

The graphical catalog contains the 12 upstream expressions plus working, waiting, success, error, stop and wake. A local touch or IMU reaction may differ from the requested base; STATUS exposes both.

Thinking was refined in 1.1.5, with asymmetric eyes, pauses and a 9.2-second loop. Working was refined in 1.1.6, with converging eyes, two writing passes, clearing between passes and a 14.72-second loop. These describe the current animation loops; physical appearance must be checked on the device.

## Serial commands

Protocol version 1, ASCII lines at 115200 baud. Responses begin with `CP ` followed by JSON. The host must complete INFO and verify protocol, firmware version and device identity before control.

| Command | Behavior |
| --- | --- |
| `INFO` | Device type, stable factory eFuse identity, firmware/protocol versions and capabilities |
| `STATUS` | Active/requested state, sleep, brightness and optional diagnostics |
| `STATE <expression>` | Install the base; cancel any active reaction |
| `REACT success/error/stop/wake` | One reaction, followed by the installed base |
| `BRIGHTNESS 0..100` | Set display brightness |
| `SLEEP` | Sleepy transition, fade, panel off after 1.5 seconds; USB stays active |
| `WAKE` | Panel-on fade over 0.4 seconds, restore the base |
| `SOUND none/fanfare/voice` | Select the Success cue without playing it |

The Hub requests the Wake reaction separately after WAKE. STATE can update the stored base during sleep without turning on the panel. Repeating REACT can replay a reaction; the Hub owns event deduplication. The full shared contract is in `CodexPetHub/docs/PROTOCOLS.md`.

## Completion audio

Firmware 1.1.7 advertises `INFO.successAudio=true`. Two embedded mono PCM16 WAV files remain in flash during asynchronous M5Unified playback. Playback is local and requires no network, filesystem or speech model.

Only `REACT success` plays a cue. Boot defaults to silent. STATE, SLEEP, another reaction or a changed sound preference stops audio. Restoring a base never replays it. STATUS exposes the selected cue, software readiness/playback state and play/error counters. The counters do not prove audibility.

See [audio assets and provenance](../assets/audio/README.md).

## Power and health diagnostics

Optional fields added in 1.1.4 include boot ID, 64-bit uptime, reset reason, panel state, last power command, battery/USB voltages and USB presence. Missing or failed readings remain null. Charging state is unavailable in the current integration.

Voltage registers are read on STATUS requests. No periodic flash writes, charging policy changes or deep-sleep feature were introduced. A changed boot ID lets the Hub resynchronize after a reset even when the COM port stays the same.

## Validation

Run `pio run` for firmware compilation, the native harness for engine behavior and the Python tests for audio generation. Physical tests require exclusive USB ownership and an explicit device identity/port. Preserve the distinction between compilation, protocol replies, simulated frames and human acceptance on the real screen/speaker.

Builds and generated evidence belong under ignored `.pio` / `artifacts` directories. Never publish device identifiers, private device logs or credentials. Keep the upstream license and dependency notices.

Original upstream engineering notes remain public references. Private delivery reports
are kept locally under ignored `.local/notes`. Upstream roadmaps do not expand
the current one-Pet USB scope.

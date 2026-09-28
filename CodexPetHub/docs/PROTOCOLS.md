# CodexPet protocols

[English](PROTOCOLS.md) | [Français — preserved version](PROTOCOLS.fr.md)

Protocol identifiers remain in English regardless of the Hub interface language.

## Plugin → Hub: IPC generation 2

Local Named Pipe: `CodexPetHub.v2.<Windows SID>`, restricted to the current account; remote network clients are rejected. UTF-8 without BOM, one JSON object per line, at most 1,024 characters and depth 8. Duplicate fields, unsupported versions/states/reactions and timestamps without a timezone are rejected without logging their content. Extra fields are ignored.

~~~json
{"version":2,"producerId":"0123456789abcdef0123456789abcdef","sequence":42,"baseState":"working","reaction":"error","timestamp":"2026-09-25T10:00:00Z"}
{"version":2,"type":"heartbeat","timestamp":"2026-09-25T10:00:05Z"}
~~~

`type` defaults to `state`. Exact base values: `idle`, `thinking`, `working`, `waitingForUser`. Optional reaction: `success`, `error`, `stop`. Sleeping and Wake belong to the Hub. `producerId` identifies a worker; `sequence` increases within that producer. Already accepted revisions are ignored.

Session, turn and tool-call identifiers stay inside the plugin. Prompts, raw results, hardware identifiers and animation names are not transmitted over this IPC. Elapsed time never establishes task completion.

Each connection starts with a base-only snapshot. Reconnect every 2 seconds, heartbeat every 5 seconds, disconnect after 15 seconds of silence. These timings concern transport health. Offline reactions are not replayed. Source loss displays Idle. An idle worker can exit after 60 seconds without reclassifying ongoing activity.

Success keeps its five-second presentation while later base states are remembered and incoming reactions are discarded. At its end, the latest base appears. Sleep and explicit manual tests can override it. Other reactions can be interrupted by later activity, including a revision with the same base.

## Hub → firmware: serial protocol 1

Firmware 1.1.0 or later. 115200 baud, 8N1, no hardware handshake, DTR/RTS disabled. ASCII with LF; CRLF accepted. Commands are limited to 63 characters; overlong lines are rejected in full. Replies start with `CP ` followed by JSON.

The Hub waits for each reply before sending another command, with a 1.8-second timeout. Only a timed-out STATUS query is retried once. A reaction is not retried as a read operation.

| Command | Effect |
| --- | --- |
| `INFO`, `VERSION`, `PING` | Identity, versions and connectivity |
| `STATUS` | Active expression, requested base, sleep, brightness and diagnostics |
| `STATE <expression>` | Install a graphical base; cancel an active reaction even if the base is unchanged |
| `REACT success/error/stop/wake` | Play once and return to the base; no visible reaction during sleep |
| `SOUND none/fanfare/voice` | Select the completion cue without playing it; requires `INFO.successAudio=true` |
| `BRIGHTNESS 0..100` | Set brightness |
| `SLEEP` | Play sleepy, fade, turn off the panel after 1.5 seconds; keep USB active |
| `WAKE` | Fade the panel on over 0.4 seconds and restore the base; the Hub requests `REACT wake` separately |

Example with a synthetic device identity:

~~~text
CP {"type":"info","protocolVersion":1,"deviceType":"CodexPet","deviceId":"1234-5678-9ABC","firmwareVersion":"1.1.0"}
CP {"type":"ok","command":"REACT"}
CP {"type":"status","state":"error","requestedState":"working","sleeping":false,"brightness":50,"diagnostic":false,"freeHeap":339352,"minFreeHeap":334008}
~~~

Before controlling the device, the Hub validates device type, protocol, minimum firmware version and a nonzero/non-FF `DeviceId`. The factory eFuse MAC, formatted `XXXX-XXXX-XXXX`, identifies the Pet. A COM port is an endpoint. USB VID/PID only filters discovery candidates.

Malformed replies, invalid identity/status or firmware errors invalidate the connection and schedule a retry. Logs retain the awaited command and known `unknown_command` / `line_too_long` codes. Firmware errors have no command identity, so an error received while waiting for INFO is not proof it was caused by INFO. Unknown codes and arbitrary serial text are excluded from diagnostics.

Expressions: `idle`, `listening`, `thinking`, `happy`, `excited`, `curious`, `confused`, `angry`, `surprised`, `sad`, `sleepy`, `dizzy`, `working`, `waiting`, `success`, `error`, `stop`, `wake`. Use REACT for automatic reaction return.

The Hub uses reaction durations of 5000/3000/900/2000 ms for Success/Error/Stop/Wake. It evaluates presentation every 100 ms, excluding transport time. During protected Success it retains the applied base and revision, avoiding an intermediate STATE that would cancel or replay the reaction.

Synchronization order: changed brightness, capability-gated sound preference, changed base/revision, changed sleep state, requested reaction, STATUS. STATE can change the stored base while asleep. A transport error clears reported presentation but keeps the logical base.

## Audio and optional diagnostics

Firmware 1.1.7 adds capability-gated sound selection. Only `REACT success` plays the selected cue. Other reactions, STATE, SLEEP or a changed sound preference stop playback. Boot is silent until configured. Audio fields are `successSound`, `audioReady`, `audioPlaying`, `audioPlayCount`, `audioErrorCount`. Readiness/playback counters do not establish speaker audibility.

Firmware 1.1.4 adds optional STATUS fields; absent or null means unavailable:

| Field | Meaning |
| --- | --- |
| `bootId` | 16 random hexadecimal characters, new on boot; no flash write |
| `uptimeMs` | Milliseconds from the ESP32 64-bit uptime clock |
| `resetReason` | ESP32/ROM reason code; not a complete power-loss history |
| `panelAsleep` | Panel state, distinct from the request during fading |
| `lastPowerCommand`, `lastPowerCommandAtMs` | boot/sleep/wake and uptime of that request |
| `batteryMillivolts`, `usbMillivolts` | M5PM1 readings; invalid/I²C failures return null |
| `usbPresent` | true at ≥4 V, false at ≤0.8 V, otherwise null |
| `charging` | null on the current StopWatch integration |
| `freeHeap`, `minFreeHeap` | Firmware heap diagnostics |

Voltage registers are read only on STATUS, with a bounded second read if needed. Charging/power policy is not changed by diagnostics.

An observed reboot invalidates the applied presentation even if the COM port is unchanged, causing base, brightness, sound and sleep to resynchronize without replaying old reactions. Pending IPC base updates are coalesced, and a separate pending Success is retained. A reaction pending for more than two seconds is discarded without changing the logical base.

ACK confirms acceptance and STATUS reports firmware state. Physical visual and audio acceptance remain separate.

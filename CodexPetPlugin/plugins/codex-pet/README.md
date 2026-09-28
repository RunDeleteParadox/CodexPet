# Codex Pet plugin

[English](README.md) | [Français](README.fr.md)

This Windows PowerShell 5.1 plugin publishes Codex lifecycle states to CodexPetHub. It tracks the selected session, current turn and parallel tool calls without knowing the device port, animations, brightness or power state.

## States

Bases: `Idle`, `Thinking`, `Working`, `WaitingForUser`. Reactions: `Success`, `Error`, `Stop`. The Hub owns Sleeping and Wake.

Success means that a response finished. A successful tool, Stop, inactivity or an interrupted turn never proves completion. After Stop, the optional local completion reader requires the same turn, `completed`, a positive `completedAt` and no error. It uses read-only stdio requests and does not load messages or resume a conversation. New activity cancels that check.

Only strict boolean `is_error`/`isError` results produce a tool Error. The last non-compact session start or user prompt selects the displayed session. Duplicate and retired activity is ignored.

## IPC

The per-user pipe is `CodexPetHub.v2.<Windows SID>`. UTF-8 JSON lines carry semantic state and a producer revision:

~~~json
{"version":2,"producerId":"0123456789abcdef0123456789abcdef","sequence":42,"baseState":"working","reaction":"error","timestamp":"2026-09-25T10:00:00Z"}
~~~

`reaction` is optional. Reconnect every 2 seconds with a base-only snapshot; do not replay offline reactions. Heartbeat every 5 seconds. An idle worker may exit after 60 seconds. These timings never establish that an active tool or turn has completed.

## Local commands

Run from this plugin directory:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/CodexPet.ps1 -Action status
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/CodexPet.ps1 -Action send -State working -Reaction error
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/CodexPet.ps1 -Action disable
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/CodexPet.ps1 -Action enable
~~~

The `send` example changes the Pet presentation if a Hub is connected. Use the Hub's **Keep test active** option for visual inspection.

Runtime: `%USERPROFILE%\.codex-pet-plugin\v1.1`. `-DataDirectory` or `CODEX_PET_PLUGIN_DATA` isolates tests. State includes local session/turn/tool-call identifiers and normalized results; prompts, arguments, raw responses and transcripts are excluded. Tool identifiers stay local and are not sent to the Hub.

Configure `completionCodexPath` with the repository's `Configure-CompletionApi.py` helper. Check that path after a Codex update. `completionStatus` explains availability; `last-completion.json` records only identifiers, time and source of a confirmed signal. The experimental API can become unavailable; no fallback infers success from time.

Open a new Codex chat after reinstalling so its hooks use the installed version. Use the app's native hook trust flow when requested.

Copyright (C) 2026 RunDeleteParadox and contributors. Licensed under
**AGPL-3.0-or-later**, without warranty. See [LICENSE](LICENSE), [NOTICE](NOTICE)
and [third-party notices](THIRD_PARTY_NOTICES.md).

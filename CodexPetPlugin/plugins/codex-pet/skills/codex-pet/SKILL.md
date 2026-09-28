---
name: codex-pet
description: Diagnose or test the Codex Pet plugin that publishes semantic lifecycle states to CodexPetHub through a local Windows Named Pipe.
---

Native hooks publish lifecycle metadata automatically. Never simulate routine updates with assistant tool calls or global prompts.

Resolve `../../scripts/CodexPet.ps1` relative to this skill. Use Windows PowerShell 5.1 with `-NoProfile -ExecutionPolicy Bypass -File <absolute-path> -Action <action>`.

- `status`: read configuration, worker connection and latest base state.
- `enable` / `disable`: allow hooks or stop the relay (bounded five-second shutdown).
- `send -State working`: explicit diagnostic base state; accepts idle, thinking, working, waitingForUser.
- Optional `-Reaction success|error|stop` is for an explicitly requested diagnostic, never a substitute for actual lifecycle evidence.

The plugin owns Codex semantics. Session/turn/tool identifiers correlate parallel calls. Each completion retires only its own call. Duplicate/late events cannot resurrect completed tools or retired turns. Latest prompted session is selected; other sessions never overwrite it through tool completions. The Hub owns visual priority, timing and Windows sleep/wake. Firmware knows no tool identifiers.

Data lives in `%USERPROFILE%/.codex-pet-plugin/v1.1`, outside MSIX AppData. This separate queue prevents a V1 hook still loaded in an older conversation from competing with the V1.1 worker. `CODEX_PET_PLUGIN_DATA` or `-DataDirectory` isolates tests. IPC uses `CodexPetHub.v2.<Windows-user-SID>` (wire generation 2, product V1.1). Reconnect restores only a base snapshot, never queued celebrations. Heartbeats have no semantic meaning. The sanitized checkpoint preserves active/completed/retired call identities across worker restarts.

Success celebrates an explicitly completed response, never correctness or completion of a larger project. After a selected Stop, the worker reads Codex's experimental `thread/turns/list` API over local stdio (`itemsView: notLoaded`, limit 1). It requires the same turn id, status `completed`, a non-null positive integer `completedAt`, and no error. A new prompt, activity, interruption, or session selection cancels the reader. Never infer completion from a delay, absence of activity, Stop alone, or the read-only server's `interrupted`/`notLoaded` view. Configure `completionCodexPath` with Configure-CompletionApi.py; the helper restores the pre-existing notify consumer. `notify` can exceed the Windows command-line limit before any adapter starts. The legacy adapter remains compatible but is not needed for the new reader. API unavailability is exposed through `completionStatus`; it never fabricates Success. Reconnection never replays old celebrations.

Successful tool results never celebrate. Boolean `isError: true` or `is_error: true` still means an operation error, never a terminal turn failure. Stop means Idle plus a neutral Stop reaction while actual completion is checked. Compact preserves active calls. Question tools indicate attention only; async attention clears at tool return. The completion reader starts no thread or model turn, never resumes a thread, loads no message items, reads no transcript itself, and opens no listening network port.

Never persist prompts, tool arguments/results or transcripts. Only allowlisted event names, ids, normalized outcome, source and parent-process liveness are stored. IPC carries a base, optional reaction and transport identity/sequence, with no raw hook payload.

Installing a changed plugin may require the user's `/hooks` review. Never bypass trust checks or edit trust hashes. Open a new Codex task after reinstall. Do not replace an installation during a read-only diagnosis. For aesthetic checks use the Hub systray recipe mode; successful ACKs do not validate the appearance on the real screen.

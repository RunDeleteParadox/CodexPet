# CodexPetPlugin

[English](README.md) | [Français](README.fr.md)

A hardware-independent Codex lifecycle producer for CodexPetHub. It runs on Windows PowerShell 5.1 and sends semantic states through a local Named Pipe. Missing Hub/device connectivity does not block Codex.

The distributable plugin lives in [plugins/codex-pet](plugins/codex-pet). See its [runtime guide](plugins/codex-pet/README.md). CodexPetHub and CodexPetFirmware are sibling directories in the same Git repository, providing the Windows interface and M5Stack firmware. Run this guide's commands from the CodexPetPlugin directory.

## Installation

Follow the complete [English user guide](../docs/USER-GUIDE.md) or [French user guide](../docs/USER-GUIDE.fr.md). From the common repository root:

```powershell
codex plugin marketplace add .\CodexPetPlugin
codex plugin add codex-pet@codexpet
```

Then enable the runtime, configure the completion reader, approve the hooks and open a new Codex chat as described in that guide. Use only one installed copy of Codex Pet. The included local marketplace works without a published repository URL.

## Lifecycle model

| Event | Meaning |
| --- | --- |
| SessionStart: startup/resume/clear | Select this session and display Idle |
| SessionStart: compact | Preserve ongoing activity |
| UserPromptSubmit | Select session/turn; Thinking without active tools |
| PreToolUse | Add the correlated call; Working, or WaitingForUser for user-input tools |
| PostToolUse | Remove only that call; Working while others remain, otherwise Thinking |
| PermissionRequest | Request user attention |
| Stop | Neutral Stop reaction and a check of actual turn status |
| Interrupt | Idle without an invented error |
| SessionEnd | Idle for the selected session |
| PreCompact/PostCompact | Preserve tools/attention; otherwise Thinking |
| Confirmed completion | Idle plus Success, meaning response finished |

Bases: Idle, Thinking, Working and WaitingForUser. Reactions: Success, Error and Stop. The Hub owns Sleeping and Wake.

The last non-compact SessionStart or UserPromptSubmit selects the displayed session. Background sessions do not replace it. Duplicates, closed calls and retired turns cannot reopen activity. A Post arriving before its Pre is remembered as completed. A new Pre can resume the same turn after Stop.

Only strict boolean error fields in a tool response produce Error. A successful tool call does not produce Success, and a tool error does not automatically mean a terminal turn failure. Missing fields, arbitrary success flags, text and elapsed time remain neutral.

User-input tools request attention only for their observed lifetime. An asynchronous tool can return before the user answers; later answers are not inferred. Hosted tools do not all emit these hooks. A prose question alone does not create a signal. Permission events lack a universally correlated tool-call ID, so concurrent permission prompts for the same tool cannot be distinguished reliably.

## Completion reader

After Stop, the worker requests `thread/turns/list` through the local Codex stdio API, with `itemsView: notLoaded` and limit 1. Success requires the matching turn, `status: completed`, a positive integer `completedAt`, and no error. It loads no messages, resumes no conversation and starts no turn.

New activity, Interrupt, a new turn or a session change cancels the check. The experimental API may change. Missing executable, incompatible responses, timeouts or missing completion timestamps cannot manufacture a celebration. A prolonged failure can make a celebration go missing.

`Configure-CompletionApi.py --codex <absolute-path-to-codex.exe>` previews the setup; add `--apply` to write it. It preserves other settings with backups and restores any previously saved native notifier. Python 3.11 or later is needed for setup helpers and Python tests, but not for normal plugin operation. Initialize the plugin runtime before using the helper.

The old `Configure-CompletionNotify.py` relay remains for compatibility. Windows can reject very long JSON command-line arguments before that adapter starts. The completion reader avoids depending on that path.

## Local data and privacy

Runtime: `%USERPROFILE%\.codex-pet-plugin\v1.1`, outside virtualized AppData. Filtered events, state checkpoints and diagnostic receipts can contain session/turn/tool-call IDs and tool names. These stay local to the plugin. Prompts, tool arguments, raw responses and transcripts are not persisted or sent to the Hub.

Use `CODEX_PET_PLUGIN_DATA` or `-DataDirectory` for isolated tests. The prototype's earlier queue and relay are separate. After reinstalling, open a new Codex chat so new hooks are loaded. Review hook trust through the native application when requested.

## Tests

Build the sibling Hub first, then run from this repository:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-Core.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-CompletionApi.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-Integration.ps1
python -m unittest discover -s tests -p "test_*.py"
~~~

Integration uses a private Named Pipe and a Hub with serial disabled. Its default Hub path is relative to the repository; pass `-HubExe` for another layout. It does not install a plugin or launch the production tray.

`tests/Test-DeviceChain.ps1` is a separate physical-device test requiring explicit `-DeviceId` and `-Port`. Quit the production Hub and other serial owners first. Test payloads are synthetic. Output defaults to this repository's ignored `artifacts` directory.

`Switch-Plugin.ps1` is a migration helper for an existing personal marketplace junction and an earlier prototype checkout. Pass `-PrototypeSource` with that prototype plugin directory; it is not part of the common repository. This helper is not a general-purpose public installer. Use the included `codexpet` marketplace for a fresh installation from a downloaded checkout.

## Documentation and licensing

User setup and operating instructions are available in English and French. Earlier French guides and private delivery reports are preserved locally under ignored `.local/notes`.

Copyright (C) 2026 **RunDeleteParadox and contributors**. Licensed under
**AGPL-3.0-or-later**, without warranty. See [LICENSE](LICENSE), [NOTICE](NOTICE),
[third-party notices](THIRD_PARTY_NOTICES.md), [source distribution](SOURCE.md)
and [contribution guidance](CONTRIBUTING.md).
Hub and firmware use the same project license.

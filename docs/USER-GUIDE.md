# CodexPet user guide

[English](USER-GUIDE.md) | [Français](USER-GUIDE.fr.md)

CodexPet shows Codex activity on an M5Stack StopWatch. The Windows Hub connects
the Codex plugin to the device. This guide covers one Pet connected by USB.

## 1. What you need

- A Windows x64 computer running the Codex desktop app or CLI with plugin and hook support.
- An M5Stack **StopWatch** and a USB cable that carries data.
- This repository, downloaded as a ZIP and extracted, or cloned with Git.
- To build the Hub: the **.NET 10 SDK**. A published self-contained Hub needs no SDK.
- To set up the plugin: Windows PowerShell 5.1 (included with Windows), Python 3.11+
  and a native `codex.exe` available to the CLI.
- To flash firmware: Python and **PlatformIO Core 6.1.18**.

Open PowerShell in the extracted repository root, the folder containing all three
component directories. All commands below start there. No administrator session
or serial monitor is needed for normal use.

## 2. Prepare the Pet

Skip flashing if your StopWatch already has compatible CodexPet firmware.
The original trentct firmware must first be replaced by the CodexPet fork.
Current firmware is 1.1.7; Hub control needs 1.1.0 or newer, and sound needs 1.1.7.

Quit the Hub from its tray menu and close any serial monitor before flashing.
Connect the StopWatch and identify its COM port in Windows Device Manager.

```powershell
python -m pip install platformio==6.1.18
python -m platformio run --project-dir CodexPetFirmware
$petPort = Read-Host 'COM port of your StopWatch, for example COM3'
python -m platformio run --project-dir CodexPetFirmware --target upload --upload-port $petPort
```

Flashing replaces the device firmware. Use the port of the intended StopWatch.
See the [firmware guide](../CodexPetFirmware/README.md) for build details and recovery.

## 3. Start the Hub

If you have a complete published Windows bundle, extract it into a stable folder
and start `CodexPetHub.exe`. Keep its DLLs, `fr` folder, license files and source
archive together. Otherwise build it from the repository:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File CodexPetHub/scripts/Build.ps1 -Publish
.\CodexPetHub\artifacts\publish\CodexPetHub.exe
```

The tray icon may be inside Windows' hidden-icons area. Double-click it to open
**Settings**. Choose English or French and save. Leave the port on **Automatic**
for one device; select a COM port if several candidates are present. The Hub
remembers the Pet's hardware identity even if Windows later changes its port.

## 4. Install the Codex plugin

Start and sign into Codex once before setup. Confirm `codex --version` works in
PowerShell. Run these commands from the repository root:

```powershell
codex plugin marketplace add .\CodexPetPlugin
codex plugin add codex-pet@codexpet
powershell.exe -NoProfile -ExecutionPolicy Bypass -File CodexPetPlugin/plugins/codex-pet/scripts/CodexPet.ps1 -Action enable
$codexExe = (Get-Command codex.exe -CommandType Application).Source
python CodexPetPlugin/Configure-CompletionApi.py --codex $codexExe
python CodexPetPlugin/Configure-CompletionApi.py --codex $codexExe --apply
```

The first Python command previews setup. The second saves the local completion
reader path and makes backups, preserving existing Codex settings and notifier.
It expects `%USERPROFILE%\.codex\config.toml` to exist; if you use a custom Codex
home, pass `--config` with that home's config file. A first installation without
the file can create an empty `config.toml` there before running the helper.
Use a native `codex.exe`, not an npm `.cmd` or `.ps1` shim; locate the native
executable if your CLI launcher is a wrapper.

Restart Codex if the plugin does not appear. Review and approve its hooks in
Codex's hook settings (`/hooks` in the CLI), then open a **new chat**. Installing
a plugin alone does not approve its hooks. See [OpenAI's plugin documentation](https://developers.openai.com/plugins/build/plugins).

Only enable one copy of Codex Pet. If you previously installed `codex-pet@personal`
or the old direct-serial prototype, disable that copy before enabling this one.
The old `Switch-Plugin.ps1` migration helper is for a particular development
installation and is not needed here. Keep the source checkout available for
updates to its local marketplace.

## 5. Everyday use

The Hub has one window with **Settings**, **Diagnostics**, and **License and credits**
in the left navigation. The three tray-menu entries open that same window on the
requested page. **Settings** is bold because it is the double-click action.

- Changing pages keeps pending edits. **Save** applies them without closing the
  window; **Cancel** reloads saved preferences. Closing the window discards pending edits.
- Language previews apply throughout the window. Save updates the tray language too.
- Configure brightness, the Pet's name, completion sound, Windows sleep behavior
  and launch at sign-in on the Settings page.
- Closing the window leaves the Hub running in the tray. **Quit** stops it.
- **Test a state → Response complete** plays the selected completion cue.
  **Keep test active** holds manual tests; turn it off to return to Codex activity.

| Display | Meaning |
| --- | --- |
| Idle | No selected activity |
| Thinking | The selected Codex turn is active, without an active tool |
| Working | One or more tools are active |
| Waiting for you | An observed input/permission request needs attention |
| Response complete | A matching turn explicitly finished; this does not certify correctness |
| Error / Stopped | A tool error / neutral stop or interruption |

After Stop, the plugin checks the matching turn's completion status through a
local read-only Codex API. It does not infer completion from silence or time.
The latest prompted chat controls the Pet; background chats do not replace it.

## 6. Connection and troubleshooting

The current route is **Codex hooks → plugin → local Named Pipe → Hub → USB → Pet**.
Only the Hub opens the serial port. The plugin has no direct-Pet fallback.
The original prototype used direct serial, but that is a different installation.

- **Pet disconnected:** check the USB data cable and port, close serial monitors,
  use **Reconnect**, then inspect Diagnostics. Unplug/replug may recover USB state.
- **Pet connected but no Codex activity:** check the separate **Plugin** status,
  confirm the plugin is enabled and hooks trusted, then use a new Codex chat.
- **No completion sound:** save a sound other than None, check firmware 1.1.7+
  and test Response complete. A missing completion signal also prevents celebration.
- **No completion reaction after a Codex update:** check `completionCodexPath`
  still names an existing native executable, then rerun the setup helper.
- **Black screen:** the Pet may be sleeping. Use **Wake** before assuming a power fault.
- **Processing fault or stalled loop:** export diagnostics, then quit and restart the Hub.

Plugin status, from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File CodexPetPlugin/plugins/codex-pet/scripts/CodexPet.ps1 -Action status
```

Hub preferences/logs live in `%USERPROFILE%\.codex-pet-hub`; plugin state lives in
`%USERPROFILE%\.codex-pet-plugin\v1.1`. Prompts, raw tool results and transcripts
are not sent to the Pet. **Export diagnostics** includes local paths and device
identifiers; review the ZIP before sharing it.

## 7. Update or remove

Quit the Hub before replacing its entire published folder. Preferences remain
under your profile. If you move the executable, save the launch-at-sign-in setting
from the new location. Update firmware separately with the Hub stopped.

After updating the plugin source, reinstall `codex-pet@codexpet` and use a new
Codex chat. Review changed hooks when Codex asks, and recheck the completion path.
Do not enable a second marketplace copy as an update to an existing installation.

To remove the plugin, first run `CodexPet.ps1 -Action disable` using the full
relative script path above, then `codex plugin remove codex-pet@codexpet`.
For another marketplace, use its installed identifier. Disable Hub startup in
Settings, save and quit before deleting the Hub folder. Local preferences and
logs can be retained; deleting them is a separate reset of your saved state.

## License and credits

All three components use **AGPL-3.0-or-later**. Firmware derives from
[trentct's M5Stack StopWatch Avatar](https://github.com/Trentct/m5stack-stopwatch-avatar).
RunDeleteParadox maintains the CodexPet adaptations, Hub and plugin.
See [credits](../CREDITS.md) and [licensing](../CodexPetHub/docs/LICENSING.md), including
matching source and notices when redistributing binaries.

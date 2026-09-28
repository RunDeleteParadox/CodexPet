# Contributing

Use Windows and the .NET 10 SDK. Build and run the software checks from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1
```

The checks use synthetic data, private Named Pipes and fake or disabled serial transports. The UI checks create temporary off-screen windows and a temporary tray icon. They do not use the physical Pet or change the installed Hub configuration.

Keep these boundaries intact:

- The plugin owns Codex lifecycle interpretation. A successful tool call or elapsed time must not manufacture a completed response.
- The Hub owns presentation, preferences and the single USB/serial connection.
- The firmware owns rendering and hardware behavior.
- Device identity comes from the firmware; a COM port is only an endpoint.
- Prompts, transcripts and raw tool payloads must stay out of IPC, diagnostics and test evidence.

Read [the protocols](docs/PROTOCOLS.md) before changing a contract and [localization](docs/LOCALIZATION.md) before adding interface text. Maintain user guides in English and French. Contributor references may be English. Keep dated local engineering reports in ignored `.local/notes`; preserve originals when replacing old guides.

Describe the behavior change, relevant checks and any remaining physical-device validation in a contribution. A successful build, protocol ACK or simulated renderer does not establish what a real screen or speaker looks or sounds like.

## Publishing

Use `scripts/Build.ps1 -Publish` to create an independent Windows x64 build under `artifacts/publish`. Stop a Hub running from that directory before replacing it. Keep generated files, local configurations, device identities and diagnostic exports out of source control.

Contributions use **AGPL-3.0-or-later**, the project license chosen by RunDeleteParadox.
Submit only work you are entitled to contribute, retain upstream notices and
identify third-party material and its license. Contributors retain their copyright;
no copyright assignment or separate contributor agreement is required.
See [licensing](docs/LICENSING.md) and [NOTICE](NOTICE).

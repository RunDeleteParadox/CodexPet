# Contributing

CodexPetPlugin is maintained by RunDeleteParadox under **AGPL-3.0-or-later**.
Contributions use the same license. Contributors retain their copyright;
no copyright assignment or separate contributor agreement is required.

Submit only work you are entitled to contribute. Identify third-party material,
preserve its notices and confirm compatible terms. Keep the root LICENSE/NOTICE
and the copies in `plugins/codex-pet` together when packaging the plugin.

Write current guides in English; preserve existing French documents and dated
engineering records. Run the checks documented in README.md for behavioral changes.
Keep prompts, transcripts, raw tool payloads and private configuration out of source
control. Test with a private data directory and Named Pipe.

Completion requires an explicit matching completed turn and timestamp. Never infer
Success from Stop, a successful tool, inactivity or a timeout. The plugin owns Codex
semantics; the Hub alone owns device presentation and serial access.

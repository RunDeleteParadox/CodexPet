# Contributing

Describe the behavior you want to change and identify the affected component.
Keep changes small enough to review and test. The component guides contain
setup and validation details:

- [Hub](CodexPetHub/CONTRIBUTING.md)
- [Plugin](CodexPetPlugin/CONTRIBUTING.md)
- [Firmware](CodexPetFirmware/CONTRIBUTING.md)

Run `scripts/Test.ps1` on Windows for Hub/plugin work. Firmware work requires
`pio run --project-dir CodexPetFirmware`; report physical-device observations
separately from successful builds or protocol replies. Never commit local logs,
device identifiers or credentials.

Maintain public user guides in English and French. Technical contributor guides may be English. Preserve upstream documentation and keep private working notes in ignored `.local/notes`.
Update both Hub resource catalogs when changing user-facing text.

Contributions are accepted under AGPL-3.0-or-later. Contributors retain their
copyright; no separate assignment or contributor agreement is required. Preserve
the original trentct firmware notices and any third-party terms.

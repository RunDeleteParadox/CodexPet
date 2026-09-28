# Corresponding source and firmware releases

CodexPetFirmware is licensed under AGPL-3.0-or-later. CodexPet additions and
modifications are Copyright (C) 2026 RunDeleteParadox and contributors. Original
upstream rights remain intact; see LICENSE, NOTICE and THIRD_PARTY_NOTICES.md.

For every firmware binary, provide the matching complete source tree and clear
download directions alongside it. Preserve the exact revision and include:

- `src`, `include` and any local libraries used by the build;
- `platformio.ini` and its pinned platform/library references;
- the source WAV files in `assets/audio`, their provenance and usage terms;
- the audio generation script and the generated embedded data used in the build;
- build, flash and recovery instructions from README and the hardware guides;
- tests, license copies, third-party notices and change records.

Build with PlatformIO Core 6.1.18 using `pio run`. Regenerate embedded audio with
`python scripts/embed_success_audio.py` when changing its WAV sources. The
README gives the flash command and USB ownership requirements. No project
signing key is needed by the current development flashing process.

Retain the source and notices required by dependency licenses when bundling
their compiled code. The Arduino/ESP-IDF framework contains components with
different licenses; a firmware binary release needs the applicable notices and
corresponding-source materials for that actual build. The M5 library copies in
LICENSES are part of that inventory, not a replacement for the framework terms.

When distributing a consumer device, include any installation information
required by AGPL section 6 so its recipient can install a modified version.
Do not include private device identifiers, local logs, credentials or build
caches in a public source archive.

Public RunDeleteParadox repository addresses will be added once chosen.

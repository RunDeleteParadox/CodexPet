# Contributor and agent guidance

This repository contains CodexPetHub, CodexPetPlugin and CodexPetFirmware.
Read the relevant component README before editing. Firmware changes also follow
CodexPetFirmware/AGENTS.md and its required hardware/engineering guides.

- Keep source, protocol evidence and physical-device observations distinct.
- The plugin emits semantic lifecycle events; the Hub owns serial access and
  presentation. Firmware renders the avatar. Preserve these responsibilities.
- Do not infer successful completion from inactivity, a Stop or an interrupted turn.
- Maintain user guides in both English and French; technical contributor guides may be English.
- Keep personal audits, delivery reports and local evidence under ignored .local/notes or artifacts.
- Preserve earlier guide versions locally when replacing them with current user documentation.
- Retain trentct's firmware attribution, upstream history and third-party notices.
- Run the relevant component checks. scripts/Test.ps1 covers Hub and plugin;
  firmware builds use pio run --project-dir CodexPetFirmware.
- Keep runtime data, device identifiers, credentials, binaries and test artifacts
  out of commits. Runtime preferences belong under the user's profile.

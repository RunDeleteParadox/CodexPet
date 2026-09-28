# Source distribution

CodexPetPlugin is Copyright (C) 2026 RunDeleteParadox and contributors, under
AGPL-3.0-or-later. See LICENSE, NOTICE and THIRD_PARTY_NOTICES.md.

The runtime package in `plugins/codex-pet` is readable PowerShell source; no
compiled plugin binary or bundled third-party runtime is needed. Retain its
license and notice files when distributing that directory. A corresponding
repository archive must include the setup helpers, tests, source documentation
and license files for the same revision. Exclude local runtime data, logs,
credentials and ignored artifacts.

The README documents prerequisites and tests. Normal operation uses Windows
PowerShell 5.1; development helpers additionally use Python and the .NET SDK.
No private signing key is needed to modify the plugin. Codex may ask the user to
review and trust a newly installed plugin's hooks through its normal trust flow.

RunDeleteParadox will add public source and installation addresses once the
repositories and distribution route are chosen. Include a matching source
archive with any package published before those addresses are documented.

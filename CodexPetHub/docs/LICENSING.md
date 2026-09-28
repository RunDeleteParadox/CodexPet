# Licensing and distribution

On September 28, 2026, RunDeleteParadox selected **AGPL-3.0-or-later** for CodexPetHub, CodexPetPlugin and the CodexPet changes to CodexPetFirmware. The firmware already used this license. Its upstream license, authorship and history are retained. This guide explains the release process; the [LICENSE](../LICENSE) contains the actual terms.

## Attribution and scope

Original Hub and plugin work is Copyright (C) 2026 RunDeleteParadox and contributors. In the firmware, that attribution covers the CodexPet additions and modifications. The original firmware comes from [Trentct/m5stack-stopwatch-avatar](https://github.com/Trentct/m5stack-stopwatch-avatar), baseline `2049632`.

The common repository and each component contain license and attribution files. Each component keeps its own THIRD_PARTY_NOTICES.md, and the packaged plugin carries its own license and notice copies. Third-party code and assets keep their own terms. The Hub bundles applicable .NET runtime and package notices in LICENSES; the firmware preserves its upstream and dependency notices.

Contributions are accepted under AGPL-3.0-or-later. Contributors retain their copyright; there is no separate copyright assignment or contributor agreement. Preserve existing third-party notices.

## What recipients may do

Recipients may use, study, modify and redistribute the software, including commercially. Distributed derivatives must preserve applicable AGPL terms, notices and corresponding-source rights. The project is supplied without warranty, as set out in the license.

Private use does not by itself require publishing a GitHub repository, and the license does not require sending private changes upstream. Section 13 additionally requires a modified program that supports remote network interaction to offer its corresponding source to those remote users. If network access is introduced later, implement that source offer as part of the feature. See [AGPL sections 2, 4–6 and 13](https://www.gnu.org/licenses/agpl-3.0.en.html).

## Preparing a release

1. Identify the exact source revision used to build each binary. Preserve a tag or immutable source archive and document the build prerequisites.
2. Include LICENSE, NOTICE, THIRD_PARTY_NOTICES.md and applicable LICENSES files in the distribution. Keep modification records and upstream attribution.
3. Offer complete matching corresponding source alongside each binary, with clear download directions. Include source resources, build scripts and installation materials.
4. Retain dependency version pins and any dependency source/materials required by their licenses. Update bundled notices when dependencies change.
5. When distributing a consumer device, also assess and supply the installation information required by AGPL section 6 for installing modified software.

### CodexPetHub

`scripts/Build.ps1 -Publish` builds and tests the application, publishes the Windows x64 bundle, and creates `CodexPetHub-source.zip` in `artifacts/publish`. Ship that archive with the bundle. It excludes runtime preferences, device data, logs, generated build directories and private artifacts. [SOURCE.md](../SOURCE.md) explains how to rebuild. The tray's License and credits window exposes copyright, warranty, license and source information in both interface languages.

### CodexPetPlugin

The plugin is distributed as readable source. Include its LICENSE, NOTICE and THIRD_PARTY_NOTICES.md when packaging `plugins/codex-pet`, and offer the matching repository source archive for the setup helpers and tests. The manifest declares AGPL-3.0-or-later and RunDeleteParadox. Public repository and marketplace addresses must be chosen before publishing. Changing repository metadata does not change an already installed Codex plugin cache.

### CodexPetFirmware

Keep the original LICENSE and source notices. Supply the complete matching tree, including `platformio.ini`, source WAV files, audio generation script, embedded data and flashing instructions. Preserve dependency and audio notices in THIRD_PARTY_NOTICES.md and `assets/audio/README.md`. Check the actual dependency bundle and audio provenance before publishing a binary or selling a device.

## Public addresses

The organization name is **RunDeleteParadox**. No public repository URLs are assumed by these documents. Add confirmed addresses to the READMEs, plugin distribution instructions and source download directions when repositories are created. Repository publication is a separate step from preparing these files.

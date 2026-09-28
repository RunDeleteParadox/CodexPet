# Third-party notices

CodexPetHub's original code and documentation are licensed under AGPL-3.0-or-later.
Copyright (C) 2026 RunDeleteParadox and contributors. See [NOTICE](NOTICE).

## Original firmware: trentct

CodexPetFirmware is a fork of **KK / M5Stack StopWatch Avatar**, created by
**trentct and contributors**:
[Trentct/m5stack-stopwatch-avatar](https://github.com/Trentct/m5stack-stopwatch-avatar).
The fork started from upstream commit `204963257cb4dc2f3d7501eff900897bac55ef82`.
Its AGPL-3.0-or-later license, original notices and Git history are preserved.
Credit for the original avatar engine and firmware belongs to that project.

RunDeleteParadox and contributors maintain the CodexPet firmware adaptations,
Windows Hub and Codex plugin. Firmware dependency and inspiration credits are
recorded in CodexPetFirmware/THIRD_PARTY_NOTICES.md, including M5Stack libraries
and Bible Strong Avatar Lab. The Hub credits acknowledge this shared foundation;
firmware implementation and audio assets are distributed with the firmware.

## Windows distribution dependencies

Dependencies retain their own licenses. The current Windows x64 distribution uses:

| Component | Version | License and attribution |
| --- | --- | --- |
| Microsoft.NETCore.App runtime | 10.0.12 | MIT, .NET Foundation and contributors; additional component notices |
| Microsoft.WindowsDesktop.App runtime | 10.0.12 | MIT, .NET Foundation and contributors |
| System.IO.Ports | 10.0.0 | MIT, Microsoft Corporation; package notices |
| System.Management | 10.0.0 | MIT, Microsoft Corporation; package notices |

The [LICENSES](LICENSES) directory contains the runtime license texts, Microsoft
package MIT notice and the third-party notice files supplied with those packages.
They are copied into the build and publish output automatically. Runtime notice
files cover additional upstream material under the terms specified in each notice.

This inventory was checked against the published `.deps.json` and
`.runtimeconfig.json`. Update it and the license copies when dependency or runtime
versions change. The .NET SDK and Windows operating system are not bundled.

CodexPetFirmware is a separate AGPL-3.0-or-later program. Its implementation and
audio assets are not embedded in CodexPetHub. OpenAI and M5Stack names identify
interoperability; this community project is not affiliated with either organization.

# Corresponding source

CodexPetHub is Copyright (C) 2026 RunDeleteParadox and contributors and licensed
under AGPL-3.0-or-later. See LICENSE and NOTICE.

The release process places `CodexPetHub-source.zip` beside the Windows executable.
It contains the source, resources, tests, build scripts, documentation and license
notices used for the distribution. Extract it into an empty directory on Windows
and run the following with the .NET 10 SDK installed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 -Publish
```

NuGet restores the dependencies declared in the project files. Keep the `fr`
resource directory and legal files with the executable. No device keys, proprietary
compiler extension or signing key is required to build or run a modified Hub.

The publisher must offer the matching source archive with each binary release,
with clear download directions alongside the binary. A public repository address
will be added once RunDeleteParadox chooses it. Do not substitute an unrelated
upstream archive or a different revision for the source of a distributed binary.

# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    dotnet build CodexPetHub.slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/CodexPetHub.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet run --project tests/CodexPetHub.ReliabilityTests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Reliability tests failed.' }
    dotnet run --project tests/CodexPetHub.UiTests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'UI localization tests failed.' }
    if ($Publish) {
        dotnet publish src/CodexPetHub.Windows -c Release -r win-x64 --self-contained true -o artifacts/publish --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        & (Join-Path $PSScriptRoot 'Export-Source.ps1') -OutputDirectory (Join-Path $root 'artifacts\publish')
    }
} finally { Pop-Location }

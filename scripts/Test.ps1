# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$checks = @(
    'CodexPetHub\scripts\Build.ps1',
    'CodexPetPlugin\tests\Test-Core.ps1',
    'CodexPetPlugin\tests\Test-CompletionApi.ps1',
    'CodexPetPlugin\tests\Test-Integration.ps1'
)
foreach ($check in $checks) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root $check)
    if ($LASTEXITCODE -ne 0) { throw "Failed: $check" }
}
& python -m unittest discover -s (Join-Path $root 'CodexPetPlugin\tests') -p 'test_*.py'
if ($LASTEXITCODE -ne 0) { throw 'Plugin Python tests failed.' }

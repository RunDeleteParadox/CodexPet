# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param(
    [Parameter(Mandatory=$true)][ValidateSet('V1','Prototype')][string]$Target,
    [Parameter(Mandatory=$true)][string]$PrototypeSource
)
$ErrorActionPreference = 'Stop'
# Deployment helper for the existing personal marketplace junction. Not run by tests.
$profileRoot = [Environment]::GetFolderPath('UserProfile')
$junction = Join-Path $profileRoot 'plugins\codex-pet'
$newSource = Join-Path $PSScriptRoot 'plugins\codex-pet'
$oldSource = [IO.Path]::GetFullPath($PrototypeSource)
$desired = if($Target -eq 'V1') { $newSource } else { $oldSource }
$creator = Join-Path $profileRoot '.codex\skills\.system\plugin-creator\scripts'
$item = Get-Item -LiteralPath $junction -Force
if ($item.LinkType -ne 'Junction') { throw "Expected existing personal marketplace junction at $junction. No files changed." }
$previous = [IO.Path]::GetFullPath([string]$item.Target)
$permitted = @([IO.Path]::GetFullPath($oldSource), [IO.Path]::GetFullPath($newSource))
if ($previous -notin $permitted) { throw 'Unexpected junction target. No files changed.' }
if (-not (Test-Path -LiteralPath (Join-Path $desired '.codex-plugin\plugin.json'))) { throw 'Target plugin manifest missing.' }
if ($Target -eq 'Prototype' -and (Get-Process CodexPetHub -ErrorAction SilentlyContinue)) {
    throw 'Quit CodexPetHub from its tray menu before restoring the prototype serial owner.'
}
$marketplace = & python (Join-Path $creator 'read_marketplace_name.py')
if ($LASTEXITCODE -ne 0 -or $marketplace -ne 'personal') { throw 'Expected personal marketplace. No files changed.' }
& python (Join-Path $creator 'validate_plugin.py') $desired
if ($LASTEXITCODE -ne 0) { throw 'Plugin validation failed.' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $oldSource 'scripts\CodexPet.ps1') -Action disable
if ($LASTEXITCODE -ne 0) { throw 'Prototype did not release ownership.' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $newSource 'scripts\CodexPet.ps1') -Action disable
if ($LASTEXITCODE -ne 0) { throw 'New relay did not stop.' }
try {
    if ($previous -ne [IO.Path]::GetFullPath($desired)) {
        # Nonrecursive deletion of the verified junction, never its target sources.
        [IO.Directory]::Delete($junction)
        New-Item -ItemType Junction -Path $junction -Target $desired | Out-Null
    }
    & python (Join-Path $creator 'update_plugin_cachebuster.py') $desired
    if ($LASTEXITCODE -ne 0) { throw 'Cachebuster update failed.' }
    & codex plugin add 'codex-pet@personal' --json
    if ($LASTEXITCODE -ne 0) { throw 'Plugin installation failed.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $desired 'scripts\CodexPet.ps1') -Action enable
    if ($LASTEXITCODE -ne 0) { throw 'Installed plugin could not be enabled.' }
    Write-Output 'Installation complete. Open a new Codex task and review /hooks if requested. Trust records were not modified.'
} catch {
    # Restore the source pointer. Leave relays disabled after an uncertain installation.
    $current = Get-Item -LiteralPath $junction -Force -ErrorAction SilentlyContinue
    if ($current -and $current.LinkType -eq 'Junction' -and [IO.Path]::GetFullPath([string]$current.Target) -eq [IO.Path]::GetFullPath($desired)) {
        [IO.Directory]::Delete($junction)
    }
    if (-not (Test-Path -LiteralPath $junction)) { New-Item -ItemType Junction -Path $junction -Target $previous | Out-Null }
    throw
}

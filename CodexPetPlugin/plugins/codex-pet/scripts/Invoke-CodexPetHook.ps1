# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param([string]$DataDirectory)
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'CodexPet.Core.ps1')
    $root = Get-CodexPetDataRoot $DataDirectory
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $event = ConvertTo-CodexPetEvent $payload
    if ($null -eq $event -or -not (Get-CodexPetConfig $root).enabled) { exit 0 }
    Add-CodexPetHookEvent $root $event (Get-CodexPetOwner)
    Start-CodexPetWorker $root
} catch {
    # Never surface display errors to Codex; never persist a JSON exception or raw payload.
    try {
        if ($root) {
            [IO.Directory]::CreateDirectory($root) | Out-Null
            Write-CodexPetJson (Join-Path $root 'hook-error.json') ([pscustomobject]@{ timestamp = [datetime]::UtcNow.ToString('o'); error = 'Lifecycle event could not be processed.' })
        }
    } catch { }
}
exit 0

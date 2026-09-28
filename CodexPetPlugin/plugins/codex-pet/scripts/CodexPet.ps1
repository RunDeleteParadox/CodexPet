# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param(
    [ValidateSet('status','enable','disable','send')][string]$Action = 'status',
    [ValidateSet('idle','thinking','working','waitingForUser')][string]$State = 'idle',
    [ValidateSet('','success','error','stop')][string]$Reaction = '',
    [string]$DataDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CodexPet.Core.ps1')
$root = Get-CodexPetDataRoot $DataDirectory
[IO.Directory]::CreateDirectory($root) | Out-Null
$config = Get-CodexPetConfig $root
switch ($Action) {
    'status' {
        $status = $null
        $path = Join-Path $root 'status.json'
        if (Test-Path -LiteralPath $path) { $status = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
        [pscustomobject]@{ dataDirectory = $root; config = $config; workerRunning = (Test-CodexPetWorkerRunning $root); status = $status } | ConvertTo-Json -Depth 6
    }
    'enable' { $config.enabled = $true; Write-CodexPetJson (Join-Path $root 'config.json') $config }
    'disable' {
        $config.enabled = $false; Write-CodexPetJson (Join-Path $root 'config.json') $config
        $deadline = [datetime]::UtcNow.AddSeconds(5)
        while ((Test-CodexPetWorkerRunning $root) -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (Test-CodexPetWorkerRunning $root) { throw 'Worker has not stopped within 5 seconds.' }
        $queue = Join-Path $root 'events'
        if (Test-Path -LiteralPath $queue) { Get-ChildItem -LiteralPath $queue -Filter '*.json' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName } }
    }
    'send' {
        if (-not $config.enabled) { throw 'Plugin disabled. Enable it before sending a semantic test state.' }
        Add-CodexPetEvent $root $State -Reaction $Reaction
        Start-CodexPetWorker $root
    }
}

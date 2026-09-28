# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param([Parameter(Position=0)][string]$Notification, [string]$DataDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CodexPet.Core.ps1')
$root = Get-CodexPetDataRoot $DataDirectory

function Quote-NativeArgument([string]$Value) {
    # Windows argv quoting, not PowerShell code evaluation. Preserve the JSON
    # argument exactly for the pre-existing native notify consumer.
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

# Preserve the existing notifier even if our own adapter fails or is disabled.
try {
    $forwardFile = Join-Path $root 'notify-forward.json'
    if (Test-Path -LiteralPath $forwardFile) {
        $forward = Get-Content -LiteralPath $forwardFile -Raw | ConvertFrom-Json
        $command = @($forward.command)
        if ($command.Count -gt 0) {
            $info = New-Object Diagnostics.ProcessStartInfo
            $info.FileName = [string]$command[0]
            $arguments = @(); if ($command.Count -gt 1) { $arguments += $command[1..($command.Count-1)] }
            $arguments += $Notification
            $info.Arguments = ($arguments | ForEach-Object { Quote-NativeArgument ([string]$_) }) -join ' '
            $info.UseShellExecute = $false; $info.CreateNoWindow = $true
            $process = [Diagnostics.Process]::Start($info)
            $process.Dispose()
        }
    }
} catch {
    # No raw arguments or exception text: notification bodies may be private.
    [IO.Directory]::CreateDirectory($root) | Out-Null
    Write-CodexPetJson (Join-Path $root 'notify-forward-error.json') ([pscustomobject]@{timestamp=[datetime]::UtcNow.ToString('o');error='Existing notify consumer could not be started.'})
}

try {
    $event = ConvertTo-CodexPetCompletion ($Notification | ConvertFrom-Json)
    if ($null -eq $event -or -not (Get-CodexPetConfig $root).enabled) { exit 0 }
    Add-CodexPetHookEvent $root $event (Get-CodexPetOwner)
    Start-CodexPetWorker $root
} catch {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    Write-CodexPetJson (Join-Path $root 'notify-error.json') ([pscustomobject]@{timestamp=[datetime]::UtcNow.ToString('o');error='Turn completion could not be processed.'})
}

# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param([string]$DataDirectory, [int]$MaxSeconds = 0)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CodexPet.Core.ps1')
. (Join-Path $PSScriptRoot 'CompletionApi.ps1')
$root = Get-CodexPetDataRoot $DataDirectory
$queue = Join-Path $root 'events'
[IO.Directory]::CreateDirectory($queue) | Out-Null
$lease = New-Object Threading.Mutex($false, (Get-CodexPetMutexName $root))
$owned = $false; $pipe = $null; $model = New-CodexPetModel
$producer = [guid]::NewGuid().ToString('N')
$started = [datetime]::UtcNow; $lastActivity = $started
$timestamp = $started.ToString('o'); $nextAttempt = $started; $nextHeartbeat = $started; $nextStatus = $started
$ownerPid = 0; $ownerStart = ''; $lastOwnerCheck = $started
$lastError = $null; $pipeName = ''; $delivered = 0; $connections = 0
$completionReader = $null; $completionTarget = $null; $completionStatus = 'inactive'
$completionRetry = $started; $lastCompletion = $null
function Send-Snapshot([string]$Reaction = '') {
    $message = [ordered]@{ version=2; producerId=$producer; sequence=$model.revision; baseState=$model.state; timestamp=$timestamp }
    if ($Reaction) { $message.reaction = $Reaction }
    Send-CodexPetPipeLine $pipe ($message | ConvertTo-Json -Compress)
    $script:delivered++; $script:lastError = $null; $script:nextHeartbeat = [datetime]::UtcNow.AddSeconds(5)
}
try {
    try { $owned = $lease.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { exit 0 }
    $checkpoint = Join-Path $root 'semantic-state.json'
    if (Test-Path -LiteralPath $checkpoint) {
        $saved = Get-Content -LiteralPath $checkpoint -Raw | ConvertFrom-Json
        if ($saved.schema -eq 2) { $model = ConvertTo-CodexPetHashtable $saved.model }
    }
    while ($true) {
        $now = [datetime]::UtcNow
        if ($MaxSeconds -gt 0 -and ($now - $started).TotalSeconds -ge $MaxSeconds) { break }
        $config = Get-CodexPetConfig $root
        if (-not $config.enabled) { break }
        if ($pipeName -ne $config.pipeName) {
            if ($null -ne $pipe) { $pipe.Dispose(); $pipe = $null }
            $pipeName = $config.pipeName; $nextAttempt = $now
        }
        # Every completion updates the set, even if multiple hooks queue at once.
        foreach ($file in @(Get-ChildItem -LiteralPath $queue -Filter '*.json' -File | Sort-Object Name)) {
            try {
                $event = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                $changed = Update-CodexPetModel $model $event
                Write-CodexPetJson $checkpoint ([pscustomobject]@{ schema=2; model=$model })
                if ($changed) {
                    $timestamp = $event.timestamp; $lastActivity = $now
                    $ownerPid = [int]$event.ownerPid; $ownerStart = [string]$event.ownerStartUtc
                    if ($event.hook -eq 'Stop') {
                        Stop-CodexPetCompletionReader $completionReader; $completionReader=$null
                        $completionTarget=@{session=$event.session;turn=$event.turn}
                        $completionRetry=$now; $completionStatus='pending'
                    }
                    if ($null -ne $pipe) {
                        try { Send-Snapshot $model.reaction }
                        catch { $pipe.Dispose(); $pipe = $null; $lastError = 'Hub disconnected.'; $nextAttempt = $now.AddSeconds(2) }
                    }
                }
            } catch { $lastError = 'Invalid queued lifecycle metadata.' }
            finally { Remove-Item -LiteralPath $file.FullName -Force }
        }
        if ($null -ne $completionTarget) {
            $session=$model.sessions[$completionTarget.session]
            if ($model.selected -cne $completionTarget.session -or $session.turn -cne $completionTarget.turn -or
                -not $session.stopped) {
                Stop-CodexPetCompletionReader $completionReader; $completionReader=$null; $completionTarget=$null
                $completionStatus='inactive'
            } elseif ($config.completionCodexPath) {
                try {
                    if ($null -eq $completionReader -and $now -ge $completionRetry) {
                        $completionReader=Start-CodexPetCompletionReader $config.completionCodexPath $completionTarget.session $completionTarget.turn
                        $completionStatus='checking'
                    }
                    if ($null -ne $completionReader) {
                        $confirmed=Read-CodexPetCompletion $completionReader
                        if ($null -ne $confirmed) {
                            if (Update-CodexPetModel $model $confirmed) {
                                $timestamp=$confirmed.timestamp; $lastActivity=$now
                                $lastCompletion=@{session=$confirmed.session;turn=$confirmed.turn;at=$timestamp;source='thread/turns/list'}
                                Write-CodexPetJson $checkpoint ([pscustomobject]@{schema=2;model=$model})
                                Write-CodexPetJson (Join-Path $root 'last-completion.json') $lastCompletion
                                if ($null -ne $pipe) {
                                    try { Send-Snapshot $model.reaction }
                                    catch { $pipe.Dispose(); $pipe=$null; $lastError='Hub disconnected.'; $nextAttempt=$now.AddSeconds(2) }
                                }
                            }
                            Stop-CodexPetCompletionReader $completionReader; $completionReader=$null; $completionTarget=$null
                            $completionStatus='confirmed'
                        }
                    }
                } catch {
                    Stop-CodexPetCompletionReader $completionReader; $completionReader=$null
                    $completionStatus='unavailable'; $completionRetry=$now.AddSeconds(5)
                }
            } else { $completionStatus='not-configured' }
        }
        if (($now - $lastOwnerCheck).TotalSeconds -ge 2 -and $ownerPid -gt 0) {
            $lastOwnerCheck = $now
            try { $owner = Get-Process -Id $ownerPid -ErrorAction Stop; if ($owner.StartTime.ToUniversalTime().ToString('o') -ne $ownerStart) { break } }
            catch { break }
        }
        if ($null -eq $pipe -and $now -ge $nextAttempt) {
            try {
                $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', $pipeName, [IO.Pipes.PipeDirection]::Out, [IO.Pipes.PipeOptions]::Asynchronous)
                $pipe.Connect(80); $connections++
                Send-Snapshot # Reconnect restores the base, not old reactions.
            } catch {
                if ($null -ne $pipe) { $pipe.Dispose(); $pipe = $null }
                $lastError = 'CodexPetHub unavailable.'; $nextAttempt = $now.AddSeconds(2)
            }
        }
        if ($null -ne $pipe -and $now -ge $nextHeartbeat) {
            try {
                Send-CodexPetPipeLine $pipe (@{ version=2; type='heartbeat'; timestamp=$now.ToString('o') } | ConvertTo-Json -Compress)
                $nextHeartbeat = $now.AddSeconds(5)
            } catch { $pipe.Dispose(); $pipe = $null; $nextAttempt = $now.AddSeconds(2) }
        }
        if ($now -ge $nextStatus) {
            Write-CodexPetJson (Join-Path $root 'status.json') ([pscustomobject]@{
                running=$true; workerPid=$PID; connected=($null -ne $pipe); state=$model.state;
                deliveredStates=$delivered; connections=$connections; updated=$now.ToString('o'); error=$lastError
                completionStatus=$completionStatus; lastCompletion=$lastCompletion
            })
            $nextStatus = $now.AddSeconds(1)
        }
        # Elapsed time never classifies an active turn.
        if ($model.state -eq 'idle' -and ($now - $lastActivity).TotalSeconds -gt 60) { break }
        Start-Sleep -Milliseconds 50
    }
} catch { $lastError = 'Plugin worker stopped after a local IPC/configuration error.' }
finally {
    Stop-CodexPetCompletionReader $completionReader
    if ($null -ne $pipe) { $pipe.Dispose() }
    if ($owned) {
        try { Write-CodexPetJson (Join-Path $root 'status.json') ([pscustomobject]@{ running=$false; workerPid=$PID; connected=$false; state=$model.state; deliveredStates=$delivered; connections=$connections; updated=[datetime]::UtcNow.ToString('o'); error=$lastError }) } catch { }
        $lease.ReleaseMutex()
    }
    $lease.Dispose()
    if ($owned -and $MaxSeconds -eq 0) {
        try { if ((Get-CodexPetConfig $root).enabled -and @(Get-ChildItem -LiteralPath $queue -Filter '*.json' -File).Count -gt 0) { Start-CodexPetWorker $root } } catch { }
    }
}

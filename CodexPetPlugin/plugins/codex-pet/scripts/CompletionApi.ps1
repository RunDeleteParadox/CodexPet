# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

# Read-only Codex app-server client. No thread/resume, turn/start or message data.
# All transport uses redirected stdio, never a shell or a listening network port.
if (-not ('CodexPetCompletionOutput' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'CompletionProcess.cs')
}
function Start-CodexPetCompletionReader {
    param([string]$Executable, [string]$Session, [string]$Turn)
    if (-not $Executable -or -not [IO.File]::Exists($Executable)) { throw 'Completion executable unavailable.' }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = 'app-server --listen stdio:// -c features.hooks=false -c notify=[]'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    $reader = @{ process=$process; output=(New-Object CodexPetCompletionOutput($process)); session=$Session; turn=$Turn;
        phase='starting'; id=1; next=[datetime]::UtcNow; deadline=[datetime]::UtcNow.AddSeconds(10) }
    return $reader
}

function Stop-CodexPetCompletionReader {
    param($Reader)
    if ($null -eq $Reader) { return }
    $Reader.output.Stop()
}

function ConvertFrom-CodexPetTurnPage {
    param($Result, [string]$Session, [string]$Turn)
    # A stored active turn can appear "interrupted" in a separate reader.
    # Never derive activity or errors from this API. Only an explicit completed
    # status WITH a persisted completion timestamp can confirm the matching turn.
    foreach ($entry in @(Get-CodexPetProperty $Result 'data' @())) {
        if ((Get-CodexPetProperty $entry 'id') -cne $Turn -or
            (Get-CodexPetProperty $entry 'status') -cne 'completed' -or
            $null -ne (Get-CodexPetProperty $entry 'error')) { continue }
        $finished = Get-CodexPetProperty $entry 'completedAt'
        if ($finished -isnot [long] -and $finished -isnot [int]) { continue }
        if ($finished -le 0) { continue }
        return ConvertTo-CodexPetCompletion ([pscustomobject]@{
            type='agent-turn-complete'; 'thread-id'=$Session; 'turn-id'=$Turn })
    }
    return $null
}

function Read-CodexPetCompletion {
    param($Reader)
    $now = [datetime]::UtcNow
    if ($Reader.output.Failed) { throw 'Completion reader could not start.' }
    if (-not $Reader.output.Started) {
        if ($now -ge $Reader.deadline) { throw 'Completion reader start timed out.' }
        return $null
    }
    if ($Reader.process.HasExited) { throw 'Completion reader exited.' }
    if ($Reader.phase -eq 'starting') {
        $message = @{id=1; method='initialize'; params=@{
            clientInfo=@{name='codexpet_completion';version='1.1'}; capabilities=@{experimentalApi=$true} }}
        $Reader.process.StandardInput.WriteLine(($message | ConvertTo-Json -Depth 5 -Compress)); $Reader.process.StandardInput.Flush()
        $Reader.phase='initialize'; $Reader.deadline=$now.AddSeconds(10)
    }
    # Drain only already available responses; never block the IPC/heartbeat loop.
    $line = ''
    for ($i=0; $i -lt 16 -and $Reader.output.TryRead([ref]$line); $i++) {
        $message = $line | ConvertFrom-Json
        if ((Get-CodexPetProperty $message 'id') -ne $Reader.id) { continue }
        if ($null -ne (Get-CodexPetProperty $message 'error')) { throw 'Completion API unavailable.' }
        if ($Reader.phase -eq 'initialize') {
            $Reader.process.StandardInput.WriteLine('{"method":"initialized"}')
        } elseif ($Reader.phase -eq 'query') {
            $event = ConvertFrom-CodexPetTurnPage (Get-CodexPetProperty $message 'result') $Reader.session $Reader.turn
            if ($null -ne $event) { return $event }
        }
        $Reader.phase='ready'; $Reader.next=$now.AddMilliseconds(500)
    }
    if ($Reader.phase -eq 'ready' -and $now -ge $Reader.next) {
        $Reader.id++; $Reader.phase='query'; $Reader.deadline=$now.AddSeconds(10)
        $request=@{ id=$Reader.id; method='thread/turns/list'; params=@{
            threadId=$Reader.session; limit=1; sortDirection='desc'; itemsView='notLoaded' } }
        $Reader.process.StandardInput.WriteLine(($request | ConvertTo-Json -Depth 4 -Compress))
        $Reader.process.StandardInput.Flush()
    } elseif ($Reader.phase -ne 'ready' -and $now -ge $Reader.deadline) {
        # This is a transport timeout, NEVER evidence that the turn completed.
        throw 'Completion API timed out.'
    }
    return $null
}

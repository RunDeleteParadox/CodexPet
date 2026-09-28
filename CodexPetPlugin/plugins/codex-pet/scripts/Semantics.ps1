# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

# Windows PowerShell 5.1. Only allowlisted lifecycle metadata is retained.
function Get-CodexPetOutcome {
    param($Payload)
    $result = Get-CodexPetProperty $Payload 'tool_response'
    foreach ($key in @('is_error','isError')) {
        $flag = Get-CodexPetProperty $result $key
        if ($flag -is [bool] -and $flag) { return 'error' }
    }
    # Successful tools update activity only. Success celebrates a completed
    # assistant turn, received through Codex's native notify contract.
    return ''
}

function ConvertTo-CodexPetCompletion {
    param($Payload)
    if ((Get-CodexPetProperty $Payload 'type') -cne 'agent-turn-complete') { return $null }
    $sid = Get-CodexPetProperty $Payload 'thread-id'
    $tid = Get-CodexPetProperty $Payload 'turn-id'
    foreach ($value in @($sid, $tid)) {
        if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value) -or $value.Length -gt 256 -or $value -match '[\r\n\x00]') { return $null }
    }
    # Do not retain cwd, input-messages or last-assistant-message.
    return [pscustomobject]@{ schema=2; id=[guid]::NewGuid().ToString('N');
        timestamp=[datetime]::UtcNow.ToString('o'); hook='TurnComplete';
        session=$sid; turn=$tid; tool=''; toolName=''; source=''; outcome='' }
}

function ConvertTo-CodexPetEvent {
    param($Payload)
    $name = [string](Get-CodexPetProperty $Payload 'hook_event_name' '')
    if ($name -notin @('SessionStart','SessionEnd','UserPromptSubmit','PreToolUse','PostToolUse','PermissionRequest','Stop','Interrupt','PreCompact','PostCompact')) { return $null }
    $e = [ordered]@{ schema = 2; id = [guid]::NewGuid().ToString('N'); timestamp = [datetime]::UtcNow.ToString('o'); hook = $name }
    foreach ($pair in @(@('session','session_id'),@('turn','turn_id'),@('tool','tool_use_id'),@('toolName','tool_name'))) {
        $value = [string](Get-CodexPetProperty $Payload $pair[1] '')
        if ($value.Length -gt 256 -or $value -match '[\r\n\x00]') { return $null }
        $e[$pair[0]] = $value
    }
    if (-not $e.session -or ($name -notin @('SessionStart','SessionEnd') -and -not $e.turn)) { return $null }
    if ($name -in @('PreToolUse','PostToolUse') -and -not $e.tool) { return $null }
    $source = [string](Get-CodexPetProperty $Payload 'source' '')
    $e.source = if ($source -in @('startup','resume','clear','compact')) { $source } else { '' }
    $e.outcome = if ($name -eq 'PostToolUse') { Get-CodexPetOutcome $Payload } else { '' }
    return [pscustomobject]$e
}

function New-CodexPetModel {
    return @{ sessions = [Collections.Hashtable]::new([StringComparer]::Ordinal); selected = ''; state = 'idle'; reaction = ''; revision = 0L }
}
function New-CodexPetSession {
    return @{ turn = ''; active = [Collections.Hashtable]::new([StringComparer]::Ordinal); completed = [Collections.Hashtable]::new([StringComparer]::Ordinal); retired = [Collections.Hashtable]::new([StringComparer]::Ordinal); ended = $false; stopped = $false; attention = ''; state = 'idle'; seen = [Collections.Hashtable]::new([StringComparer]::Ordinal) }
}
function Update-CodexPetModel {
    param($Model, $Event)
    $Model.reaction = ''
    if ($Event.schema -ne 2) { return $false }
    if ($Event.hook -eq 'Diagnostic') {
        $Model.state = $Event.baseState; $Model.reaction = $Event.reaction; $Model.revision++
        return $true
    }
    $sid = [string]$Event.session; $tid = [string]$Event.turn
    if (-not $Model.sessions.ContainsKey($sid)) { $Model.sessions[$sid] = New-CodexPetSession }
    $s = $Model.sessions[$sid]
    if ($s.seen.ContainsKey($Event.id)) { return $false }
    $s.seen[$Event.id] = $true
    $hook = $Event.hook
    if ($hook -eq 'SessionStart' -and $Event.source -eq 'compact') { return $false }
    if ($hook -eq 'SessionStart') {
        $Model.sessions[$sid] = New-CodexPetSession; $s = $Model.sessions[$sid]
        $Model.selected = $sid
    } elseif ($hook -eq 'SessionEnd') {
        $s.ended = $true; $s.stopped = $false; foreach ($id in @($s.active.Keys)) { $s.completed[$id] = $true }; $s.active.Clear(); $s.attention = ''; $s.state = 'idle'
    } else {
        if ($s.retired.ContainsKey($tid)) { return $false }
        if ($tid -ne $s.turn) {
            # A prompt starts a new turn. Other events may bootstrap an unknown
            # session but cannot resurrect an older/unannounced turn.
            if ($s.turn -and $hook -ne 'UserPromptSubmit') { return $false }
            if ($s.turn) { $s.retired[$s.turn] = $true }
            $s.turn = $tid; $s.active.Clear(); $s.completed.Clear(); $s.seen.Clear()
            $s.ended = $false; $s.stopped = $false; $s.attention = ''; $s.state = 'thinking'
        }
        if ($hook -eq 'UserPromptSubmit') {
            # Stop hooks can cause a continuation within the SAME turn.
            $s.ended = $false; $s.stopped = $false; $s.state = 'thinking'; $s.attention = ''
            $Model.selected = $sid
        } elseif ($s.ended) {
            # Stop is not terminal. An explicitly observed new activity may
            # continue the same turn, without a second UserPromptSubmit.
            $continuation = $s.ContainsKey('stopped') -and $s.stopped -and
                ($hook -in @('PermissionRequest','PreCompact','PostCompact','TurnComplete','Interrupt') -or
                 ($hook -eq 'PreToolUse' -and -not $s.completed.ContainsKey($Event.tool)))
            if (-not $continuation) { return $false }
            $s.ended = $false; $s.stopped = $false
        }
        switch ($hook) {
            'PreToolUse' {
                if ($s.completed.ContainsKey($Event.tool) -or $s.active.ContainsKey($Event.tool)) { return $false }
                $waiting = $Event.toolName -match '(^|[_.:/])request_user_input(_async)?$'
                $s.active[$Event.tool] = @{ waiting = $waiting; name = $Event.toolName }
                # A new non-question operation ends an uncorrelated attention cue.
                $s.attention = ''
            }
            'PostToolUse' {
                if ($s.completed.ContainsKey($Event.tool)) { return $false }
                # A completion arriving before its Pre is a tombstone; a late
                # Pre cannot reopen it. Unknown completions never celebrate.
                $known = $s.active.ContainsKey($Event.tool)
                $s.active.Remove($Event.tool); $s.completed[$Event.tool] = $true
                if ($s.attention -eq $Event.toolName) { $s.attention = '' }
                if ($known -and $Event.outcome -eq 'error') { $Model.reaction = 'error' }
            }
            'TurnComplete' {
                $s.ended = $true; $s.stopped = $false
                foreach ($id in @($s.active.Keys)) { $s.completed[$id] = $true }
                $s.active.Clear(); $s.attention = ''; $s.state = 'idle'
                $Model.reaction = 'success'
            }
            'PermissionRequest' { $s.attention = if ($Event.toolName) { $Event.toolName } else { 'PermissionRequest' } }
            { $_ -in @('Stop','Interrupt') } {
                $s.ended = $true; foreach ($id in @($s.active.Keys)) { $s.completed[$id] = $true }; $s.active.Clear(); $s.attention = ''; $s.state = 'idle'
                $s.stopped = $hook -eq 'Stop'
                if ($hook -eq 'Stop') { $Model.reaction = 'stop' }
            }
        }
        if (-not $s.ended) {
            $waitingTools = @($s.active.Values | Where-Object { $_.waiting }).Count
            $s.state = if ($s.attention -or $waitingTools -gt 0) { 'waitingForUser' } elseif ($s.active.Count -gt 0) { 'working' } else { 'thinking' }
        }
    }
    if (-not $Model.selected) { $Model.selected = $sid }
    if ($Model.selected -ne $sid) { $Model.reaction = ''; return $false }
    $Model.state = $s.state; $Model.revision++
    return $true
}

function ConvertTo-CodexPetHashtable {
    param($Value)
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $h = [Collections.Hashtable]::new([StringComparer]::Ordinal); foreach ($p in $Value.PSObject.Properties) { $h[$p.Name] = ConvertTo-CodexPetHashtable $p.Value }; return $h
    }
    return $Value
}

# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\plugins\codex-pet\scripts\CodexPet.Core.ps1')
$passed=0
function Assert($Condition, [string]$Name) { if (-not $Condition) { throw "FAIL $Name" }; $script:passed++; Write-Output "PASS $Name" }
function Event([string]$Hook,[string]$Tool='', $Result=$null,[string]$Turn='t1',[string]$Name='Bash',[string]$Session='s1',[string]$Source='') {
    ConvertTo-CodexPetEvent ([pscustomobject]@{hook_event_name=$Hook;session_id=$Session;turn_id=$Turn;tool_use_id=$Tool;tool_name=$Name;tool_response=$Result;source=$Source;prompt='PRIVATE_SENTINEL';tool_input='PRIVATE_SENTINEL'})
}
function Step($E) { Update-CodexPetModel $script:m $E | Out-Null }
$m=New-CodexPetModel
Step (Event 'UserPromptSubmit'); Assert ($m.state -eq 'thinking') 'prompt starts activity'
Step (Event 'PreToolUse' 'A'); Step (Event 'PreToolUse' 'B')
Assert ($m.state -eq 'working' -and $m.sessions.s1.active.Count -eq 2) 'two tools active'
Step (Event 'PostToolUse' 'A'); Assert ($m.state -eq 'working' -and $m.reaction -eq '') 'one finishes; other keeps Working, no fabricated success'
Step (Event 'PostToolUse' 'B'); Assert ($m.state -eq 'thinking') 'last tool ends'
Step (Event 'PreToolUse' 'B'); Assert ($m.sessions.s1.active.Count -eq 0) 'late duplicate Pre cannot reopen completion'
Step (Event 'PostToolUse' 'before'); Step (Event 'PreToolUse' 'before'); Assert ($m.sessions.s1.active.Count -eq 0) 'Post before Pre tombstone'
foreach($key in @('is_error','isError')) {
    Step (Event 'PreToolUse' $key)
    $result=New-Object PSObject -Property @{$key=$true}
    Step (Event 'PostToolUse' $key $result)
    Assert ($m.reaction -eq 'error' -and $m.state -eq 'thinking') "explicit $key error returns to base"
}
Step (Event 'PreToolUse' 'long')
Step (Event 'PreToolUse' 'bad'); Step (Event 'PostToolUse' 'bad' ([pscustomobject]@{isError=$true}))
Assert ($m.state -eq 'working' -and $m.reaction -eq 'error') 'error preserves parallel work'
Step (Event 'PreToolUse' 'mcp' -Name 'mcp__test__read'); Step (Event 'PostToolUse' 'mcp' ([pscustomobject]@{isError=$false}) -Name 'mcp__test__read')
Assert ($m.state -eq 'working' -and $m.reaction -eq '') 'successful MCP tool does not celebrate'
foreach($result in @([pscustomobject]@{isError='true'},[pscustomobject]@{is_error=$false},[pscustomobject]@{success=$true},'exit code: 0')) {
    Assert ((Get-CodexPetOutcome ([pscustomobject]@{tool_name='Bash';tool_response=$result})) -eq '') 'unknown/string result is neutral'
}
Assert ((Get-CodexPetOutcome ([pscustomobject]@{tool_name='mcp__s__t';tool_response=[pscustomobject]@{isError=$false;is_error=$true}})) -eq 'error') 'error wins conflicting flags'
foreach($tool in @('request_user_input','functions.request_user_input_async','tools/request_user_input')) {
    Step (Event 'PreToolUse' $tool -Name $tool); Assert ($m.state -eq 'waitingForUser') "attention $tool"
    Step (Event 'PostToolUse' $tool -Name $tool); Assert ($m.state -eq 'working') 'attention ends at tool return, remaining work retained'
}
Step (Event 'PermissionRequest'); Assert ($m.state -eq 'waitingForUser') 'permission attention'
Step (Event 'PostToolUse' 'long'); Assert ($m.state -eq 'thinking') 'permission cleared by relevant result'
Step (Event 'PreToolUse' 'compact-work')
Step (Event 'PreCompact'); Step (Event 'PostCompact'); Step (Event 'SessionStart' -Source 'compact')
Assert ($m.state -eq 'working') 'compaction preserves actual tools'
Step (Event 'Stop'); Assert ($m.state -eq 'idle' -and $m.reaction -eq 'stop') 'Stop is neutral reaction with Idle base'
Assert (-not (Update-CodexPetModel $m (Event 'PostToolUse' 'compact-work'))) 'late completion after Stop ignored'
Step (Event 'PreToolUse' 'after-stop'); Assert ($m.state -eq 'working' -and $m.reaction -eq '') 'new tool directly preempts Stop in same turn'
Step (Event 'Stop')
Step (Event 'UserPromptSubmit'); Step (Event 'PreToolUse' 'continued'); Assert ($m.state -eq 'working') 'same-turn Stop continuation'
Step (Event 'UserPromptSubmit' -Turn 't2'); Step (Event 'PreToolUse' 'new' -Turn 't2')
Assert (-not (Update-CodexPetModel $m (Event 'Stop' -Turn 't1'))) 'old-turn Stop rejected'
Assert ($m.state -eq 'working') 'old turn does not erase new activity'
Step (Event 'Interrupt' -Turn 't2'); Assert ($m.state -eq 'idle' -and $m.reaction -eq '') 'interrupt is not an error'
Step (Event 'UserPromptSubmit' -Session 's2'); Step (Event 'PreToolUse' 'selected' -Session 's2')
Step (Event 'SessionEnd'); Assert ($m.state -eq 'working') 'background session end cannot steal selected source'
$raw=(Event 'PostToolUse' 'private' ([pscustomobject]@{isError=$true;content='PRIVATE_SENTINEL'})) | ConvertTo-Json -Depth 8
Assert (-not $raw.Contains('PRIVATE_SENTINEL')) 'sanitized queue contains no payload text'
$roundtrip=ConvertTo-CodexPetHashtable (($m | ConvertTo-Json -Depth 8) | ConvertFrom-Json)
Assert ($roundtrip.sessions.s2.active.ContainsKey('selected')) 'restart checkpoint retains exact active calls'
Assert ($null -eq (ConvertTo-CodexPetEvent ([pscustomobject]@{hook_event_name='PreToolUse'}))) 'missing correlation ids rejected'
Assert ((Get-CodexPetPipeName) -match '^CodexPetHub\.v2\.S-1-') 'IPC generation isolated from V1'
Assert ((Get-CodexPetDataRoot) -like '*\.codex-pet-plugin\v1.1') 'runtime queue isolated from still-loaded V1 hooks'
foreach($flag in @('false','true',0,1)) {
    Assert ((Get-CodexPetOutcome ([pscustomobject]@{tool_name='mcp__s__t';tool_response=[pscustomobject]@{isError=$flag}})) -eq '') 'non-boolean MCP flags are neutral'
}
$m=New-CodexPetModel; Step (Event 'UserPromptSubmit'); Step (Event 'PreToolUse' 'Case'); Step (Event 'PreToolUse' 'case')
Step (Event 'PostToolUse' 'Case'); Assert ($m.sessions.s1.active.ContainsKey('case') -and $m.state -eq 'working') 'opaque tool ids remain case-sensitive'
Step (Event 'PermissionRequest' -Name ''); Assert ($m.state -eq 'waitingForUser') 'permission without tool name still requests attention'
function Completion([string]$Turn='t1',[string]$Session='s1') {
    ConvertTo-CodexPetCompletion ([pscustomobject]@{type='agent-turn-complete';'thread-id'=$Session;'turn-id'=$Turn;'input-messages'=@('PRIVATE_SENTINEL');'last-assistant-message'='PRIVATE_SENTINEL'})
}
$m=New-CodexPetModel; Step (Event 'UserPromptSubmit'); Step (Event 'Stop')
Step (Completion); Assert ($m.state -eq 'idle' -and $m.reaction -eq 'success') 'native completion after Stop celebrates response completion'
Assert (-not (Update-CodexPetModel $m (Completion))) 'duplicate native completion is ignored'
Assert (-not (Update-CodexPetModel $m (Event 'Stop'))) 'late Stop cannot overwrite completion'
$m=New-CodexPetModel; Step (Event 'UserPromptSubmit'); Step (Event 'PreToolUse' 'A'); Step (Completion)
Assert ($m.state -eq 'idle' -and $m.reaction -eq 'success' -and $m.sessions.s1.active.Count -eq 0) 'completion before Stop closes observed activity'
Assert (-not (Update-CodexPetModel $m (Event 'PostToolUse' 'A'))) 'late tool result cannot replay a completion'
Step (Event 'UserPromptSubmit' -Turn 't2'); Assert (-not (Update-CodexPetModel $m (Completion))) 'old completion cannot end a newer turn'
Assert ($m.state -eq 'thinking') 'newer turn remains active'
Step (Event 'Interrupt' -Turn 't2'); Assert (-not (Update-CodexPetModel $m (Completion -Turn 't2'))) 'completion cannot celebrate an interrupted turn'
$m=New-CodexPetModel; Step (Event 'UserPromptSubmit'); Step (Event 'UserPromptSubmit' -Session 's2');
Assert (-not (Update-CodexPetModel $m (Completion))) 'background completion does not steal selected session'
Assert ($m.state -eq 'thinking' -and $m.reaction -eq '') 'background completion is silent'
Assert (-not ((Completion | ConvertTo-Json -Depth 6).Contains('PRIVATE_SENTINEL'))) 'notify text is never persisted'
foreach($bad in @([pscustomobject]@{type='other';'thread-id'='s';'turn-id'='t'},[pscustomobject]@{type='agent-turn-complete';'thread-id'='s'},[pscustomobject]@{type='agent-turn-complete';'thread-id'=123;'turn-id'='t'})) {
    Assert ($null -eq (ConvertTo-CodexPetCompletion $bad)) 'notify requires exact type and explicit correlation ids'
}
$m=New-CodexPetModel; Step (Event 'UserPromptSubmit'); Step (Event 'Stop'); Step (Event 'Interrupt')
Assert (-not $m.sessions.s1.stopped) 'Interrupt after Stop closes the completion window'
Assert (-not (Update-CodexPetModel $m (Completion))) 'interruption after Stop cannot celebrate'
Write-Output "RESULT: $passed passed"

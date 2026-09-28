# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param(
    [Parameter(Mandatory=$true)][string]$DeviceId,
    [Parameter(Mandatory=$true)][string]$Port,
    [string]$FirmwareVersion='1.1.7',
    [string]$HubExe=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\CodexPetHub\artifacts\publish\CodexPetHub.exe')),
    [string]$Scripts=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\plugins\codex-pet\scripts')),
    [string]$Output=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\device-chain-v1.1'))
)
# Deliberately hardware-bound. Quit the production Hub/serial monitors first.
# These are synthetic hook fixtures through the real entrypoint, IPC and USB.
$ErrorActionPreference='Stop'
. (Join-Path $Scripts 'CodexPet.Core.ps1')
if (Get-Process CodexPetHub -ErrorAction SilentlyContinue) { throw 'Quit the Hub before running the exclusive USB test.' }
$Output=[IO.Path]::GetFullPath($Output)
$pluginData=Join-Path $Output 'plugin'; $hubData=Join-Path $Output 'hub'
[IO.Directory]::CreateDirectory($pluginData) | Out-Null
[IO.Directory]::CreateDirectory($hubData) | Out-Null
$pipeName='CodexPetHub.DeviceTest.'+[guid]::NewGuid().ToString('N')
$session='fixture-'+[guid]::NewGuid().ToString('N')
$script:turn='fixture-turn'
Write-CodexPetJson (Join-Path $pluginData 'config.json') @{enabled=$true;pipeName=$pipeName}
Write-CodexPetJson (Join-Path $hubData 'config.json') @{launchOnStartup=$false;brightness=50;sleepOnWindowsLock=$true;sleepOnDisplayOff=$true;serialPort=$Port;reconnectInterval=1;currentPet=@{deviceId=$DeviceId;friendlyName='Test fixture'}}
$script:checks=@(); $hub=$null
function Read-Status {
    $reader=$null
    try {
        $stream=[IO.File]::Open((Join-Path $hubData 'status.json'),[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $reader=New-Object IO.StreamReader($stream)
        $reader.ReadToEnd() | ConvertFrom-Json
    } catch { $null } finally { if($null -ne $reader){$reader.Dispose()} }
}
function Await([scriptblock]$Condition,[string]$Name) {
    $end=[datetime]::UtcNow.AddSeconds(10)
    do { $s=Read-Status; if ($s -and $null -ne $s.device -and $null -ne $s.displayed -and (& $Condition $s)) { $script:checks+=$Name; Write-Output "PASS $Name"; return }; Start-Sleep -Milliseconds 50 } while([datetime]::UtcNow -lt $end)
    throw "FAIL $Name; last status: $($s | ConvertTo-Json -Depth 5 -Compress)"
}
function Hook([string]$Name,[string]$Id='',[string]$Tool='fixture_tool',$Result=$null) {
    $payload=@{hook_event_name=$Name;session_id=$session;turn_id=$script:turn;tool_use_id=$Id;tool_name=$Tool;tool_response=$Result}
    $pi=New-Object Diagnostics.ProcessStartInfo
    $pi.FileName=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $pi.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+(Join-Path $Scripts 'Invoke-CodexPetHook.ps1')+'" -DataDirectory "'+$pluginData+'"'
    $pi.UseShellExecute=$false; $pi.CreateNoWindow=$true; $pi.RedirectStandardInput=$true; $pi.RedirectStandardOutput=$true
    $p=[Diagnostics.Process]::Start($pi)
    try {
        $p.StandardInput.WriteLine(($payload | ConvertTo-Json -Depth 5 -Compress));$p.StandardInput.Close()
        if (-not $p.WaitForExit(4000) -or $p.ExitCode -ne 0 -or $p.StandardOutput.ReadToEnd() -ne '') { throw 'Hook entrypoint failed or emitted output.' }
    } finally { $p.Dispose() }
}
try {
    $hub=Start-Process $HubExe -ArgumentList ('--headless --write-status --seconds 120 --pipe '+$pipeName+' --data "'+$hubData+'"') -WindowStyle Hidden -PassThru
    Await {param($s) $s.device.deviceId -eq $DeviceId -and $s.device.firmwareVersion -eq $FirmwareVersion} ('real device handshake '+$FirmwareVersion)
    Hook UserPromptSubmit
    Await {param($s) $s.codexState -eq 1 -and $s.displayed.state -eq 'thinking'} 'raw prompt -> installed plugin -> Hub -> Thinking on firmware'
    Hook PreToolUse A
    Hook PreToolUse B mcp__fixture__read
    Await {param($s) $s.codexState -eq 2 -and $s.displayed.state -eq 'working'} 'parallel tools -> Working'
    Hook PostToolUse A fixture_tool @{is_error=$true}
    Await {param($s) $s.codexState -eq 2 -and $s.requested.reaction -eq 'error' -and $s.displayed.state -eq 'error' -and $s.displayed.requestedState -eq 'working'} 'is_error -> physical Error with Working base'
    Await {param($s) $null -eq $s.requested.reaction -and $s.displayed.state -eq 'working'} 'Error returns to remaining parallel work'
    Hook PostToolUse B mcp__fixture__read @{isError=$false}
    Await {param($s) $s.codexState -eq 1 -and $null -eq $s.requested.reaction -and $s.displayed.state -eq 'thinking'} 'successful MCP tool returns to Thinking without celebration'
    Hook Stop
    $notice=@{type='agent-turn-complete';'thread-id'=$session;'turn-id'=$script:turn} | ConvertTo-Json -Compress
    & (Join-Path $Scripts 'Invoke-CodexPetNotify.ps1') -Notification $notice -DataDirectory $pluginData
    Await {param($s) $s.codexState -eq 0 -and $s.requested.reaction -eq 'success' -and $s.displayed.state -eq 'success'} 'native turn completion -> physical Success with Idle base'
    $script:turn='fixture-turn-2'
    Hook UserPromptSubmit
    Hook PreToolUse Q functions.request_user_input_async
    Await {param($s) $s.codexState -eq 3 -and $s.requested.reaction -eq 'success' -and $s.displayed.state -eq 'success'} 'WaitingForUser is remembered while physical Success continues'
    Await {param($s) $s.codexState -eq 3 -and $null -eq $s.requested.reaction -and $s.displayed.state -eq 'waiting'} 'Success finishes on the remembered WaitingForUser'
    Hook PostToolUse Q functions.request_user_input_async
    Await {param($s) $s.codexState -eq 1 -and $s.displayed.state -eq 'thinking'} 'async input tool return ends attention cue'
    Hook Stop
    Await {param($s) $s.codexState -eq 0 -and $s.requested.reaction -eq 'stop' -and $s.displayed.state -eq 'stop'} 'Stop is neutral with Idle base'
    Hook PreToolUse C
    Await {param($s) $s.codexState -eq 2 -and $s.displayed.state -eq 'working'} 'new tool continues same turn after Stop'
    Hook PostToolUse A fixture_tool @{isError=$true}
    Await {param($s) $s.codexState -eq 2 -and $null -eq $s.requested.reaction -and $s.displayed.state -eq 'working'} 'late completed tool cannot reopen an error'
    Hook PostToolUse C fixture_tool @{isError=$true}
    Await {param($s) $s.codexState -eq 1 -and $s.requested.reaction -eq 'error' -and $s.displayed.state -eq 'error'} 'isError -> Error, without terminal classification'
    Await {param($s) $null -eq $s.requested.reaction -and $s.displayed.state -eq 'thinking'} 'Error returns to Thinking'
    Hook Stop
    Await {param($s) $s.codexState -eq 0 -and $null -eq $s.requested.reaction -and $s.displayed.state -eq 'idle'} 'Stop finishes on Idle'
    & (Join-Path $Scripts 'CodexPet.ps1') -Action disable -DataDirectory $pluginData
    Await {param($s) -not $s.pluginConnected -and $s.displayed.state -eq 'idle'} 'source disconnect stays Idle'
    Write-CodexPetJson (Join-Path $Output 'result.json') @{completed=$true;checks=$checks;count=$checks.Count;pluginScripts=$Scripts;timestamp=[datetime]::UtcNow.ToString('o')}
    Write-Output "RESULT: $($checks.Count) device-chain checks passed"
} finally {
    & (Join-Path $Scripts 'CodexPet.ps1') -Action disable -DataDirectory $pluginData
    if ($null -ne $hub) { if (-not $hub.HasExited) { Stop-Process -Id $hub.Id -Force; $hub.WaitForExit() }; $hub.Dispose() }
}

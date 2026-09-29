# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param([string]$HubExe = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\CodexPetHub\src\CodexPetHub.Windows\bin\Release\net10.0-windows\CodexPetHub.exe')), [switch]$KeepArtifacts)
$ErrorActionPreference = 'Stop'
$scripts = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\plugins\codex-pet\scripts'))
. (Join-Path $scripts 'CodexPet.Core.ps1')
if (-not (Test-Path -LiteralPath $HubExe)) { throw 'Build CodexPetHub.Windows first.' }
$artifacts = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts'))
$root = Join-Path $artifacts ('CodexPetIntegration-' + [guid]::NewGuid().ToString('N'))
$pluginData = Join-Path $root 'plugin'
$hubData = Join-Path $root 'hub'
[IO.Directory]::CreateDirectory($pluginData) | Out-Null
[IO.Directory]::CreateDirectory($hubData) | Out-Null
if ($KeepArtifacts) { Write-Output ('Evidence: ' + $root) }
$pipeName = 'CodexPetHub.Test.' + [guid]::NewGuid().ToString('N')
Write-CodexPetJson (Join-Path $pluginData 'config.json') ([pscustomobject]@{enabled=$true;pipeName=$pipeName})
$hub = $null; $script:passed = 0; $succeeded = $false
function Read-Status([string]$Directory) {
    try { return Get-Content -LiteralPath (Join-Path $Directory 'status.json') -Raw | ConvertFrom-Json } catch { return $null }
}
function Await([scriptblock]$Condition, [string]$Name, [int]$Seconds = 12) {
    $deadline = [datetime]::UtcNow.AddSeconds($Seconds)
    do {
        if (& $Condition) { $script:passed++; Write-Output "PASS $Name"; return }
        Start-Sleep -Milliseconds 100
    } while ([datetime]::UtcNow -lt $deadline)
    foreach ($directory in @($pluginData, $hubData)) {
        foreach ($diagnosticName in @('status.json', 'hook-error.json', 'startup-error.txt')) {
            $path = Join-Path $directory $diagnosticName
            if (Test-Path -LiteralPath $path) { Write-Output ("DIAGNOSTIC $path`: " + (Get-Content -LiteralPath $path -Raw)) }
        }
    }
    Write-Output ('DIAGNOSTIC queued events: ' + @(Get-ChildItem -LiteralPath (Join-Path $pluginData 'events') -Filter '*.json' -ErrorAction SilentlyContinue).Count)
    Write-Output ('DIAGNOSTIC worker running: ' + (Test-CodexPetWorkerRunning $pluginData))
    throw "FAIL $Name (timeout)"
}
function Start-TestHub {
    return Start-Process -FilePath $HubExe -ArgumentList ('--headless --no-serial --write-status --seconds 90 --pipe ' + $pipeName + ' --data "' + $hubData + '"') -WindowStyle Hidden -PassThru
}
try {
    # Real hook entrypoint, including private text that must never reach disk or IPC.
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $info.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + (Join-Path $scripts 'Invoke-CodexPetHook.ps1') + '" -DataDirectory "' + $pluginData + '"'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true
    $process = [Diagnostics.Process]::Start($info)
    $process.StandardInput.WriteLine('{"hook_event_name":"PreToolUse","session_id":"test-session","turn_id":"t1","tool_use_id":"A","tool_name":"safe_test","prompt":"PRIVATE_SENTINEL_DO_NOT_STORE","tool_input":{"secret":"PRIVATE_SENTINEL_DO_NOT_STORE"}}')
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(3000) -or $process.ExitCode -ne 0) { throw 'Hook blocked or failed without Hub.' }
    if ($process.StandardOutput.ReadToEnd() -ne '') { throw 'Hook wrote conversation output.' }
    $process.Dispose()
    Await { $s=Read-Status $pluginData; $null -ne $s -and $s.running -and -not $s.connected -and $s.state -eq 'working' } 'Codex starts before Hub; hook succeeds without Hub'
    $hub = Start-TestHub
    Await { $s=Read-Status $hubData; $p=Read-Status $pluginData; $null -ne $s -and $s.pluginConnected -and $s.codexState -eq 2 -and $p.connected -and $p.deliveredStates -ge 1 } 'Hub appears; plugin reconnects and restores Working'
    $before = (Read-Status $pluginData).deliveredStates
    for($i=0;$i -lt 5;$i++) { Add-CodexPetHookEvent $pluginData (ConvertTo-CodexPetEvent ([pscustomobject]@{hook_event_name='PreToolUse';session_id='test-session';turn_id='t1';tool_use_id='A';tool_name='safe_test'})) }
    # Wait until at least one full heartbeat interval has passed; state counter must stay stable.
    $changedAfter = [datetime]::UtcNow.AddSeconds(6)
    Await { [datetime]::UtcNow -ge $changedAfter -and @((Get-ChildItem -LiteralPath (Join-Path $pluginData 'events') -Filter '*.json')).Count -eq 0 } 'duplicate events consumed and heartbeat interval elapsed'
    if ((Read-Status $pluginData).deliveredStates -ne $before) { throw 'Repeated state sent unnecessarily.' }
    $script:passed++; Write-Output 'PASS unchanged state is not resent by heartbeat'
    foreach($id in @('B','C')) { Add-CodexPetHookEvent $pluginData (ConvertTo-CodexPetEvent ([pscustomobject]@{hook_event_name='PreToolUse';session_id='test-session';turn_id='t1';tool_use_id=$id;tool_name='safe_test'})) }
    Add-CodexPetHookEvent $pluginData (ConvertTo-CodexPetEvent ([pscustomobject]@{hook_event_name='PostToolUse';session_id='test-session';turn_id='t1';tool_use_id='B';tool_name='safe_test';tool_response=[pscustomobject]@{isError=$true}}))
    Await { $s=Read-Status $hubData; $s.codexState -eq 2 -and $s.requested.reaction -eq 'error' } 'parallel tool error reaches Hub with Working base'
    Await { $s=Read-Status $hubData; $s.codexState -eq 2 -and $null -eq $s.requested.reaction } 'error expires back to Working'
    Add-CodexPetEvent $pluginData 'waitingForUser'
    Await { $s=Read-Status $hubData; $null -ne $s -and $s.codexState -eq 3 -and $s.requested.expression -eq 'waiting' } 'WaitingForUser reaches Hub mapping'
    # Actual notify entrypoint -> queue -> worker -> Named Pipe -> Hub.
    $notice = '{"type":"agent-turn-complete","thread-id":"test-session","turn-id":"t1","last-assistant-message":"PRIVATE_SENTINEL_DO_NOT_STORE"}'
    & (Join-Path $scripts 'Invoke-CodexPetNotify.ps1') -Notification $notice -DataDirectory $pluginData
    Await { $s=Read-Status $hubData; $s.codexState -eq 0 -and $s.requested.reaction -eq 'success' } 'native completion reaches Hub as Idle plus Success'
    Add-CodexPetEvent $pluginData 'waitingForUser' $null 'error'
    Await { $s=Read-Status $hubData; $s.codexState -eq 3 -and $s.requested.reaction -eq 'success' -and $s.requested.expression -eq 'idle' } 'Success protects display while remembering Waiting and dropping Error'
    Await { $s=Read-Status $hubData; $s.codexState -eq 3 -and $null -eq $s.requested.reaction -and $s.requested.expression -eq 'waiting' } 'Success ends on latest resident state with no queued Error'
    # A diagnostic consumer can deny the atomic file replacement on Windows.
    # That must not terminate the actual presentation/IPC service loop.
    $lockedStatus=[IO.File]::Open((Join-Path $hubData 'status.json'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try { Add-CodexPetEvent $pluginData 'thinking'; Start-Sleep -Milliseconds 1800 } finally { $lockedStatus.Dispose() }
    Await { $s=Read-Status $hubData; $s.codexState -eq 1 -and (Get-Content (Join-Path $hubData 'hub.log') -Raw).Contains('Diagnostic status file busy') } 'locked diagnostics cannot stop presentation; file publication recovers'
    Add-CodexPetEvent $pluginData 'waitingForUser'
    Await { $s=Read-Status $hubData; $s.codexState -eq 3 } 'subsequent event still reaches Hub after diagnostic lock'
    # Only terminate the test process owned by this script.
    Stop-Process -Id $hub.Id -Force
    $hub.WaitForExit(); $hub.Dispose(); $hub = $null
    Await { $s=Read-Status $pluginData; $null -ne $s -and -not $s.connected } 'Hub exit does not kill plugin'
    $hub = Start-TestHub
    Await { $s=Read-Status $hubData; $p=Read-Status $pluginData; $null -ne $s -and $s.pluginConnected -and $s.codexState -eq 3 -and $p.connections -ge 2 } 'Hub restart restores latest WaitingForUser'
    & (Join-Path $scripts 'CodexPet.ps1') -Action disable -DataDirectory $pluginData
    Await { $s=Read-Status $hubData; $null -ne $s -and -not $s.pluginConnected -and $s.codexState -eq 0 } 'Plugin disconnect returns Hub to Idle'
    $privateText = Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
    if (($privateText -join "`n").Contains('PRIVATE_SENTINEL_DO_NOT_STORE')) { throw 'Private hook payload persisted.' }
    $script:passed++; Write-Output 'PASS privacy sentinel absent from all runtime files'
    # Hub-before-plugin scenario.
    & (Join-Path $scripts 'CodexPet.ps1') -Action enable -DataDirectory $pluginData
    & (Join-Path $scripts 'CodexPet.ps1') -Action send -State thinking -DataDirectory $pluginData
    Await { $s=Read-Status $hubData; $null -ne $s -and $s.pluginConnected -and $s.codexState -eq 1 } 'Hub starts before plugin'
    & (Join-Path $scripts 'CodexPet.ps1') -Action disable -DataDirectory $pluginData
    Write-Output "RESULT: $script:passed passed"
    $succeeded = $true
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'CodexPetIntegration-*') { throw 'Unsafe cleanup target.' }
    try { & (Join-Path $scripts 'CodexPet.ps1') -Action disable -DataDirectory $pluginData } catch { }
    # A failed test must not leave its worker alive, including across Windows app isolation.
    $workerScript = Join-Path $scripts 'Start-CodexPetWorker.ps1'
    $dataArgument = '-DataDirectory "' + $pluginData + '"'
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.Contains($workerScript) -and $_.CommandLine.Contains($dataArgument)
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if ($null -ne $hub) { if (-not $hub.HasExited) { Stop-Process -Id $hub.Id -Force; $hub.WaitForExit() }; $hub.Dispose() }
    if ($succeeded -and -not $KeepArtifacts -and -not (Test-CodexPetWorkerRunning $pluginData)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    else { Write-Output ('Evidence retained: ' + $resolved) }
}

# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param([Parameter(Mandatory=$true)][ValidatePattern('^COM[1-9][0-9]*$')][string]$Port)
$ErrorActionPreference = 'Stop'
# Hub-owned bench for a flashed device. Quit the tray Hub and any old relay first.
$serial = New-Object IO.Ports.SerialPort($Port, 115200, [IO.Ports.Parity]::None, 8, [IO.Ports.StopBits]::One)
$serial.ReadTimeout = 100; $serial.WriteTimeout = 700; $serial.NewLine = "`n"
$serial.DtrEnable = $false; $serial.RtsEnable = $false
$script:passed = 0; $baseline = $null
function Exchange([string]$Command, [string]$Type) {
    $serial.WriteLine($Command)
    $deadline = [datetime]::UtcNow.AddSeconds(3)
    while ([datetime]::UtcNow -lt $deadline) {
        try { $line=$serial.ReadLine().Trim() } catch [TimeoutException] { continue }
        if (-not $line.StartsWith('CP ')) { continue }
        $value = $line.Substring(3) | ConvertFrom-Json
        if ($value.type -eq 'error' -and $Type -ne 'error') { throw 'Firmware rejected a test command.' }
        if ($value.type -eq $Type) { return $value }
    }
    throw "No $Type response."
}
function Assert($Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL $Name" }
    $script:passed++; Write-Output "PASS $Name"
}
try {
    $serial.Open(); $serial.DiscardInBuffer()
    $info = Exchange 'INFO' 'info'
    if ($info.deviceType -ne 'CodexPet' -or $info.protocolVersion -ne 1 -or $info.deviceId -notmatch '^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$') { throw 'Device identity rejected; no presentation commands sent.' }
    Assert ($info.deviceId -notin @('0000-0000-0000','FFFF-FFFF-FFFF')) 'INFO with stable hardware identity'
    $baseline = Exchange 'STATUS' 'status'
    Assert ((Exchange 'VERSION' 'version').firmwareVersion -eq $info.firmwareVersion) 'VERSION matches INFO'
    Assert ((Exchange 'PING' 'pong').type -eq 'pong') 'PING while awake'
    Exchange 'WAKE' 'ok' | Out-Null
    foreach ($expression in @('idle','thinking','listening','happy','confused')) {
        Exchange ('STATE '+$expression) 'ok' | Out-Null
        $status = Exchange 'STATUS' 'status'
        Assert ($status.requestedState -eq $expression -and $status.state -eq $expression) "STATE $expression acknowledged and reported"
    }
    Exchange 'BRIGHTNESS 40' 'ok' | Out-Null
    Assert ((Exchange 'STATUS' 'status').brightness -eq 40) 'brightness persisted in firmware state'
    Assert ((Exchange 'BRIGHTNESS 101' 'error').code -eq 'unknown_command') 'out-of-range brightness rejected'
    Assert ((Exchange ('BRIGHTNESS 0'+('x'*70)) 'error').code -eq 'line_too_long') 'overlong line rejected in full'
    Assert ((Exchange 'STATUS' 'status').brightness -eq 40) 'invalid brightness commands did not mutate state'
    Exchange 'SLEEP' 'ok' | Out-Null
    Start-Sleep -Milliseconds 1800
    Assert ((Exchange 'STATUS' 'status').sleeping) 'sleep reported after transition interval'
    Assert ((Exchange 'PING' 'pong').type -eq 'pong') 'USB command processing continues asleep'
    Assert ((Exchange 'INFO' 'info').deviceId -eq $info.deviceId) 'identity available asleep and unchanged'
    Exchange 'STATE listening' 'ok' | Out-Null
    $asleep = Exchange 'STATUS' 'status'
    Assert ($asleep.sleeping -and $asleep.requestedState -eq 'listening') 'STATE while asleep changes base without waking'
    Exchange 'WAKE' 'ok' | Out-Null
    Start-Sleep -Milliseconds 600
    $awake = Exchange 'STATUS' 'status'
    Assert (-not $awake.sleeping -and $awake.state -eq 'listening') 'wake restores the latest requested state'
    $serial.Close(); $serial.Open(); $serial.DiscardInBuffer()
    Assert ((Exchange 'INFO' 'info').deviceId -eq $info.deviceId) 'identity unchanged after serial reopen'
    Write-Output "RESULT: $script:passed passed; protocol responses only, physical appearance not asserted."
} finally {
    if ($serial.IsOpen -and $null -ne $baseline) {
        try {
            Exchange ('BRIGHTNESS '+$baseline.brightness) 'ok' | Out-Null
            Exchange ('STATE '+$baseline.requestedState) 'ok' | Out-Null
            $power = if($baseline.sleeping) { 'SLEEP' } else { 'WAKE' }
            Exchange $power 'ok' | Out-Null
        } catch { Write-Warning 'Could not restore the device baseline. Reconnect it from the Hub.' }
    }
    $serial.Dispose()
}

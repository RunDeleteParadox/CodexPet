param([string]$HubExe=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\CodexPetHub\artifacts\publish\CodexPetHub.exe')),
      [string]$Output=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\hub-duration-test')))
# Exclusive real-device test. Explicit synthetic presentation fixtures only.
$ErrorActionPreference='Stop'
if(Get-Process CodexPetHub -ErrorAction SilentlyContinue){throw 'Quit the production Hub first.'}
[IO.Directory]::CreateDirectory($Output) | Out-Null
Copy-Item -LiteralPath (Join-Path $env:USERPROFILE '.codex-pet-hub\config.json') -Destination (Join-Path $Output 'config.json') -Force
$pipeName='CodexPetHub.ArtTest.'+[guid]::NewGuid().ToString('N')
$producer=[guid]::NewGuid().ToString('N');$script:sequence=0;$script:checks=@();$hub=$null;$client=$null
function Snapshot {
    $reader=$null
    try {
        $stream=[IO.File]::Open((Join-Path $Output 'status.json'),[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $reader=New-Object IO.StreamReader($stream);return ($reader.ReadToEnd() | ConvertFrom-Json)
    } catch {return $null} finally {if($reader){$reader.Dispose()}}
}
function Await([scriptblock]$Condition,[string]$Name) {
    $end=[datetime]::UtcNow.AddSeconds(9)
    do { $s=Snapshot;if($s -and (& $Condition $s)){$script:checks+=$Name;Write-Output "PASS $Name";return};Start-Sleep -Milliseconds 60 } while([datetime]::UtcNow -lt $end)
    throw "FAIL $Name"
}
function Send([string]$Base,[string]$Reaction='') {
    $script:sequence++;$e=@{version=2;producerId=$producer;sequence=$sequence;baseState=$Base;timestamp=[datetime]::UtcNow.ToString('o')}
    if($Reaction){$e.reaction=$Reaction}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($e | ConvertTo-Json -Compress)+"`n")
    $client.Write($bytes,0,$bytes.Length);$client.Flush()
}
try {
    $hub=Start-Process $HubExe -ArgumentList ('--headless --write-status --seconds 90 --pipe '+$pipeName+' --data "'+$Output+'"') -WindowStyle Hidden -PassThru
    Await {param($s) $s.device -and $s.device.firmwareVersion -eq '1.1.6' -and $s.displayed} 'new firmware paired through published Hub'
    $client=New-Object IO.Pipes.NamedPipeClientStream('.', $pipeName, [IO.Pipes.PipeDirection]::Out)
    $client.Connect(3000)
    $timings=@{}
    foreach($reaction in @('success','error')) {
        $duration=if($reaction -eq 'success'){5000}else{3000}
        $clock=[Diagnostics.Stopwatch]::StartNew();Send 'working' $reaction
        Await {param($s) $s.requested.reaction -eq $reaction -and $s.displayed.state -eq $reaction} "$reaction reaches real firmware"
        while($clock.ElapsedMilliseconds -lt ($duration-700)){Start-Sleep -Milliseconds 50}
        $s=Snapshot
        if($s.requested.reaction -ne $reaction){throw 'Hub ended the longer reaction too soon.'}
        $script:checks+="$reaction still visible near end";Write-Output "PASS $reaction still visible near end"
        Await {param($s) $null -eq $s.requested.reaction -and $s.displayed.state -eq 'working'} "$reaction returns to Working"
        $timings[$reaction]=$clock.ElapsedMilliseconds
    }
    Send 'working' 'success'
    Await {param($s) $s.displayed.state -eq 'success'} 'start long celebration for preemption test'
    $clock=[Diagnostics.Stopwatch]::StartNew();Send 'waitingForUser'
    Await {param($s) $null -eq $s.requested.reaction -and $s.displayed.state -eq 'waiting'} 'Waiting preempts five-second Success'
    $timings['preemptionObservedMs']=$clock.ElapsedMilliseconds
    Send 'idle' 'stop'
    Await {param($s) $s.displayed.state -eq 'stop'} 'neutral Stop remains available'
    Await {param($s) $null -eq $s.requested.reaction -and $s.displayed.state -eq 'idle'} 'Stop returns to Idle'
    @{checks=$checks;count=$checks.Count;observedMilliseconds=$timings;statusPublicationPeriodMs=500;completed=$true} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Output 'result.json') -Encoding utf8
    Write-Output "RESULT: $($checks.Count) presentation checks passed"
} finally {
    if($client){$client.Dispose()}
    if($hub){if(-not $hub.HasExited){Stop-Process -Id $hub.Id -Force;$hub.WaitForExit()};$hub.Dispose()}
}

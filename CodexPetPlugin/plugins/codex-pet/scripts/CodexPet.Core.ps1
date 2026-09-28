# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

# Windows PowerShell 5.1. Only semantic lifecycle states leave this component.
Set-StrictMode -Version 2.0

function Get-CodexPetDataRoot {
    param([string]$DataDirectory)
    if ($DataDirectory) { return [IO.Path]::GetFullPath($DataDirectory) }
    if ($env:CODEX_PET_PLUGIN_DATA) { return [IO.Path]::GetFullPath($env:CODEX_PET_PLUGIN_DATA) }
    # Shared between MSIX PowerShell and the native background worker. Isolate
    # the v2 event queue from hooks still loaded by a running V1 conversation.
    return Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-pet-plugin\v1.1'
}

function Get-CodexPetProperty {
    param($Object, [string]$Name, $Default = $null)
    if ($null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]) { return $Object.$Name }
    return $Default
}

function Write-CodexPetJson {
    param([string]$Path, $Value)
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $backup = $temporary + '.bak'
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 6 -Compress), (New-Object Text.UTF8Encoding($false)))
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, $backup) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
        if ([IO.File]::Exists($backup)) { [IO.File]::Delete($backup) }
    }
}

function Get-CodexPetPipeName {
    return 'CodexPetHub.v2.' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}

function Get-CodexPetConfig {
    param([string]$DataDirectory)
    $config = [pscustomobject]@{ enabled = $true; pipeName = (Get-CodexPetPipeName); completionCodexPath = '' }
    $path = Join-Path $DataDirectory 'config.json'
    if (Test-Path -LiteralPath $path) {
        $saved = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        foreach ($key in @('enabled','pipeName','completionCodexPath')) {
            if ($null -ne $saved.PSObject.Properties[$key]) { $config.$key = $saved.$key }
        }
    }
    if ($config.pipeName -like 'CodexPetHub.v1.*') { $config.pipeName = $config.pipeName.Replace('CodexPetHub.v1.', 'CodexPetHub.v2.') }
    if ($config.enabled -isnot [bool]) { throw 'enabled must be a boolean.' }
    if ($config.pipeName -notmatch '^[A-Za-z0-9._-]{1,160}$') { throw 'Invalid local pipe name.' }
    return $config
}

function Get-CodexPetMutexName {
    param([string]$DataDirectory)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($DataDirectory.ToLowerInvariant()))).Replace('-', '').Substring(0, 24) }
    finally { $sha.Dispose() }
    return 'Local\CodexPetPlugin-' + $key
}

function Test-CodexPetWorkerRunning {
    param([string]$DataDirectory)
    $lease = $null; $owned = $false
    try {
        try { $lease = [Threading.Mutex]::OpenExisting((Get-CodexPetMutexName $DataDirectory)) }
        catch [Threading.WaitHandleCannotBeOpenedException] { return $false }
        try { $owned = $lease.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
        return -not $owned
    } finally {
        if ($owned) { $lease.ReleaseMutex() }
        if ($null -ne $lease) { $lease.Dispose() }
    }
}

. (Join-Path $PSScriptRoot 'Semantics.ps1')

function Get-CodexPetOwner {
    # Read only process ancestry, never command lines or transcript paths.
    $ancestor = $PID
    for ($i = 0; $i -lt 10 -and $ancestor -gt 0; $i++) {
        try {
            $process = Get-CimInstance Win32_Process -Filter "ProcessId=$ancestor" -ErrorAction Stop
            if ($null -eq $process) { break }
            if ($process.Name -ieq 'codex.exe') {
                $native = Get-Process -Id $ancestor -ErrorAction Stop
                return [pscustomobject]@{ pid = $ancestor; startUtc = $native.StartTime.ToUniversalTime().ToString('o') }
            }
            $ancestor = [int]$process.ParentProcessId
        } catch { break }
    }
    return [pscustomobject]@{ pid = 0; startUtc = '' }
}

function Add-CodexPetHookEvent {
    param([string]$DataDirectory, $Event, $Owner = $null)
    $queue = Join-Path $DataDirectory 'events'
    [IO.Directory]::CreateDirectory($queue) | Out-Null
    $Event | Add-Member -NotePropertyName ownerPid -NotePropertyValue 0 -Force
    $Event | Add-Member -NotePropertyName ownerStartUtc -NotePropertyValue '' -Force
    if ($null -ne $Owner) { $Event.ownerPid = $Owner.pid; $Event.ownerStartUtc = $Owner.startUtc }
    $ticks = [datetime]::Parse($Event.timestamp).ToUniversalTime().Ticks.ToString('D19')
    Write-CodexPetJson (Join-Path $queue ($ticks + '-' + $Event.id + '.json')) $Event
}

function Add-CodexPetEvent {
    param([string]$DataDirectory, [string]$State, $Owner = $null, [string]$Reaction = '')
    if ($State -notin @('idle','thinking','working','waitingForUser')) { throw 'Invalid base state.' }
    if ($Reaction -notin @('','success','error','stop')) { throw 'Invalid reaction.' }
    $event = [pscustomobject]@{ schema=2; id=[guid]::NewGuid().ToString('N'); hook='Diagnostic';
        timestamp=[datetime]::UtcNow.ToString('o'); baseState=$State; reaction=$Reaction }
    Add-CodexPetHookEvent $DataDirectory $event $Owner
}

function Start-CodexPetWorker {
    param([string]$DataDirectory)
    if (Test-CodexPetWorkerRunning $DataDirectory) { return }
    $script = Join-Path $PSScriptRoot 'Start-CodexPetWorker.ps1'
    if ($DataDirectory.Contains('"')) { throw 'Unsupported quote in data directory.' }
    $arguments = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $script + '" -DataDirectory "' + $DataDirectory + '"'
    Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $arguments -WindowStyle Hidden | Out-Null
}

function Send-CodexPetPipeLine {
    param($Pipe, [string]$Line)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Line + "`n")
    $write = $Pipe.WriteAsync($bytes, 0, $bytes.Length)
    if (-not $write.Wait(300)) { $Pipe.Dispose(); throw 'Hub write timed out.' }
    $write.GetAwaiter().GetResult()
}

# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param([switch]$SkipNativeFrames)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $root '.local\readme-media'
$assets = Join-Path $root 'docs\assets'
[IO.Directory]::CreateDirectory($artifacts) | Out-Null
[IO.Directory]::CreateDirectory($assets) | Out-Null

if (-not $SkipNativeFrames) {
    Push-Location (Join-Path $root 'CodexPetFirmware')
    try {
        & cmd /c tests\Run-Native.cmd --frames
        if ($LASTEXITCODE -ne 0) { throw 'Native firmware preview failed.' }
    } finally { Pop-Location }
}
& python (Join-Path $PSScriptRoot 'render_readme_media.py')
if ($LASTEXITCODE -ne 0) { throw 'Animation gallery rendering failed.' }

$hubProject = Join-Path $root 'CodexPetHub\src\CodexPetHub.Windows'
& dotnet build $hubProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Hub build failed.' }
$exe = Join-Path $hubProject 'bin\Release\net10.0-windows\CodexPetHub.exe'
foreach ($language in @('en', 'fr')) {
    $data = Join-Path $artifacts ('hub-' + $language + '-' + [guid]::NewGuid().ToString('N'))
    $preview = Join-Path $data 'preview'
    [IO.Directory]::CreateDirectory($data) | Out-Null
    # Demo values only; never read the user's runtime settings or device identity.
    $config = @{
        language = $language; brightness = 60; successSound = 'Voice'; serialPort = 'auto'
        sleepOnWindowsLock = $true; sleepOnDisplayOff = $true; launchOnStartup = $false
        currentPet = @{ deviceId = '1234-5678-9ABC'; friendlyName = 'CodexPet' }
    }
    $config | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'config.json') -Encoding utf8
    $pipe = 'CodexPetHub.Readme.' + [guid]::NewGuid().ToString('N')
    $arguments = '--no-serial --pipe ' + $pipe + ' --data "' + $data + '" --render-preview "' + $preview + '"'
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $hubProject -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(30000)) { throw 'Hub preview timed out.' }
        if ($process.ExitCode -ne 0) { throw 'Hub preview failed.' }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
        $process.Dispose()
    }
    Copy-Item -LiteralPath (Join-Path $preview 'settings.png') -Destination (Join-Path $assets ('hub-settings-' + $language + '.png')) -Force
}
Write-Output "README images generated in $assets"

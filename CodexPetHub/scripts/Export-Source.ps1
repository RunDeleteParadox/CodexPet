# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$target = Join-Path $output 'CodexPetHub-source.zip'
$temporary = $target + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
$zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
try {
    $files = @(Get-ChildItem -LiteralPath $root -Force -File | Where-Object {
        $_.Name -in @('LICENSE','NOTICE','SOURCE.md','THIRD_PARTY_NOTICES.md','README.md','README.fr.md',
            'CONTRIBUTING.md','Directory.Build.props','CodexPetHub.slnx','.gitignore','.gitattributes')
    })
    foreach ($folder in @('src','tests','scripts','docs','LICENSES')) {
        $files += Get-ChildItem -LiteralPath (Join-Path $root $folder) -File -Recurse | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|artifacts|__pycache__)[\\/]' -and
            $_.Extension -in @('.cs','.csproj','.resx','.ps1','.md','.txt')
        }
    }
    foreach ($file in ($files | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($root.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative) | Out-Null
    }
} finally { $zip.Dispose() }
Move-Item -LiteralPath $temporary -Destination $target -Force
Write-Output "Corresponding source: $target"

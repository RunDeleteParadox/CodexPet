# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\plugins\codex-pet\scripts\CodexPet.Core.ps1')
. (Join-Path $PSScriptRoot '..\plugins\codex-pet\scripts\CompletionApi.ps1')
$passed=0
function Assert($Condition,[string]$Name) { if (-not $Condition) { throw "FAIL $Name" }; $script:passed++; "PASS $Name" }
function Page($Status='completed',$Date=1790535038,$Turn='t1',$ErrorValue=$null) {
    [pscustomobject]@{data=@([pscustomobject]@{id=$Turn;status=$Status;completedAt=$Date;error=$ErrorValue;items=@()})}
}
$event=ConvertFrom-CodexPetTurnPage (Page) 's1' 't1'
Assert ($event.hook -eq 'TurnComplete' -and $event.session -ceq 's1' -and $event.turn -ceq 't1') 'confirmed exact turn normalized'
foreach ($status in @('inProgress','interrupted','failed','unknown','Completed')) {
    Assert ($null -eq (ConvertFrom-CodexPetTurnPage (Page $status) 's1' 't1')) "no fabricated completion from $status"
}
foreach ($date in @($null,0,-1,'1790535038',1.5,$true)) {
    Assert ($null -eq (ConvertFrom-CodexPetTurnPage (Page -Date $date) 's1' 't1')) 'explicit persisted integer completion time required'
}
Assert ($null -eq (ConvertFrom-CodexPetTurnPage (Page -Turn 'T1') 's1' 't1')) 'opaque turn ID remains case sensitive'
Assert ($null -eq (ConvertFrom-CodexPetTurnPage (Page -ErrorValue @{message='PRIVATE_SENTINEL'}) 's1' 't1')) 'error never becomes Success'
Assert ($null -eq (ConvertFrom-CodexPetTurnPage ([pscustomobject]@{}) 's1' 't1')) 'unknown API schema fails closed'
Assert (-not (($event | ConvertTo-Json -Depth 6).Contains('PRIVATE_SENTINEL'))) 'only allowlisted lifecycle metadata survives'
"RESULT: $passed passed"

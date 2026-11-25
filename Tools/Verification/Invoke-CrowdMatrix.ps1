param(
    [Parameter(Mandatory)][string]$Player,
    [Parameter(Mandatory)][string]$Results,
    [ValidateRange(2,120)][int]$Seconds = 60,
    [ValidateRange(1,10)][int]$Runs = 3,
    [switch]$Transitions,
    [switch]$Floors,
    [switch]$LargeCrowd,
    [switch]$RoomBurst,
    [switch]$AreaBurst,
    [switch]$NaturalGc,
    [ValidateRange(0,1000)][double]$MaxAreaFrameMs = 0,
    [ValidateRange(1,100)][int]$FloorCount = 6
)
$ErrorActionPreference = 'Stop'
if ($MaxAreaFrameMs -gt 0 -and !$AreaBurst) { throw 'MaxAreaFrameMs requires AreaBurst.' }
$overBudget = @()
if ($AreaBurst) {
    $Floors = $true
    $RoomBurst = $true
    if (!$PSBoundParameters.ContainsKey('FloorCount')) { $FloorCount = 1 }
}
if (($Floors -and $Transitions) -or ($LargeCrowd -and ($Floors -or $Transitions))) { throw 'Select one scenario per run.' }
if ($RoomBurst -and !$Floors) { throw 'RoomBurst requires Floors.' }
if ($NaturalGc -and !$Floors) { throw 'NaturalGc requires a floor run.' }
$exe = (Resolve-Path -LiteralPath $Player).Path
$folder = (New-Item -ItemType Directory -Force -Path $Results).FullName
# The verification builder gives each isolated Unity project its own save identity.
# Archive that identity's previous checkpoints so repeat runs start with the same inventory.
$testSaveRoot = $null
if ($AreaBurst -and $FloorCount -gt 1) {
    $project = [IO.FileInfo]::new($exe).Directory.Parent.Parent
    $settings = Join-Path $project.FullName 'ProjectSettings/ProjectSettings.asset'
    if ($project.Name -match '^NecromanderVerification-([0-9a-f]{12})$' -and (Test-Path -LiteralPath $settings)) {
        $product = "Verification-$($Matches[1])"
        $identity = [IO.File]::ReadAllText($settings)
        if ($identity -match '(?m)^  companyName: NecromanderVerification\s*$' -and
            $identity -match ("(?m)^  productName: " + [regex]::Escape($product) + '\s*$')) {
            $testSaveRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) "AppData/LocalLow/NecromanderVerification/$product"
        }
    }
}
for ($run = 1; $run -le $Runs; $run++) {
    $budgetErrorsAtStart = $overBudget.Count
    $suite = if ($Transitions) { 'transitions' } else { 'matrix' }
    $flag = if ($Transitions) { '-flowProbe' } else { '-crowdMatrix' }
    if ($Floors) { $suite = 'floors'; $flag = '-floorSoak' }
    if ($LargeCrowd) { $suite = 'large-crowd' }
    if ($RoomBurst) { $suite = 'floors-burst' }
    if ($AreaBurst) { $suite = 'area-burst' }
    if ($Floors -and $FloorCount -ne 6) { $suite += "-$FloorCount" }
    $file = Join-Path $folder "$suite-$run.json"
    $log = Join-Path $folder "$suite-$run.log"
    $start = Get-Date
    $argsPlayer = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
        '-pixelBench',('"'+$file+'"'),$flag,'-benchSeconds',$Seconds,'-logFile',('"'+$log+'"'))
    if ($Floors) { $argsPlayer += @('-clearRooms','-floorCount',$FloorCount) }
    if ($LargeCrowd) { $argsPlayer += '-largeCrowd' }
    if ($RoomBurst) { $argsPlayer += '-roomBurst' }
    if ($AreaBurst) { $argsPlayer += '-areaBurst' }
    if ($NaturalGc) { $argsPlayer += '-naturalGc' }
    if ($testSaveRoot) {
        $archive = Join-Path $folder ("starting-save-$run-" + [guid]::NewGuid().ToString('N'))
        foreach ($name in 'save_data.json','save_data.json.bak') {
            $previous = Join-Path $testSaveRoot $name
            if (Test-Path -LiteralPath $previous) {
                [IO.Directory]::CreateDirectory($archive) | Out-Null
                Move-Item -LiteralPath $previous -Destination (Join-Path $archive $name)
            }
        }
    }
    $process = Start-Process -FilePath $exe -ArgumentList $argsPlayer -PassThru -WindowStyle Hidden
    $timeout = if ($Floors) { $FloorCount * ($Seconds + 45) + 60 } else { 17*$Seconds+180 }
    if (!$process.WaitForExit($timeout*1000)) { $process.Kill(); throw "Timed out. See $log" }
    if ($process.ExitCode -ne 0 -or (Get-Item $file).LastWriteTime -lt $start) { throw "Failed run. See $log" }
    $report = Get-Content -Raw -LiteralPath $file | ConvertFrom-Json
    if ($Floors) {
        $roots = $null
        if (!($report.sceneResources -contains 'movingLamp restoredNear=True suspendedFar=True')) {
            throw "Moving local light did not restore and release its caster. See $file"
        }
        foreach ($floor in 1..$FloorCount) {
            $prefix = if ($floor -eq 1) { "" } else { "f$floor-" }
            if ($RoomBurst -and ($floor -eq 1 -or $AreaBurst)) {
                $burst = @($report.sceneResources -like "${prefix}authoredBurst *")
                if ($burst.Count -ne 1 -or $burst[0] -notmatch "^${prefix}authoredBurst count=64 peakPerFrame=(\d+) budget=(\d+)$" -or
                    [int]$Matches[1] -gt [int]$Matches[2]) { throw "Room spawn budget failed. See $file" }
            }
            if ($AreaBurst) {
                foreach ($phase in @('area-hit','area-repeat-hit','area-trigger-hit','area-kill-with-drops')) {
                    $section = "$prefix$phase"
                    $drops = if ($phase -eq 'area-kill-with-drops') { 8 } else { 0 }
                    $row = @($report.sections | Where-Object name -eq $section)
                    $contacts = @($report.sceneResources -like "$section targets=*")
                    if ($row.Count -ne 1 -or $row[0].frames -lt 3 -or $row[0].gpuSamples -le 0 -or
                        $contacts.Count -ne 1 -or $contacts[0] -notmatch "^$section targets=64 contacts=64 affected=64 immediate=16 numbers=64 sparks=[1-9]\d* drops=$drops$") {
                        throw "Incomplete damage, particles or drops for $section. See $file"
                    }
                    $spread = @($report.sceneResources -like "$section sparkTargets=*")
                    if ($MaxAreaFrameMs -gt 0 -and $row[0].worst -gt $MaxAreaFrameMs) {
                        $overBudget += "$section $([math]::Round($row[0].worst, 2))ms ($file)"
                    }
                    if ($spread.Count -ne 1 -or $spread[0] -notmatch 'sparkTargets=64 ') {
                        throw "Hit sparks did not reach the crowd in $section. See $file"
                    }
                    $flights = if ($drops) { 6 } else { 0 }
                    if ($report.sceneResources -notcontains "$section hitFlashes=64 finishingFlights=$flights") {
                        throw "Hit flashes or finishing poses changed in $section. See $file"
                    }
                }
                if ($report.sceneResources -notcontains "${prefix}area clear=True items=9 picked=9 gold=200") {
                    throw "Clear or pickup totals failed. See $file"
                }
            }
            if ($AreaBurst -and $floor -gt 1) {
                if (!($report.sceneResources -like "floor-$floor checkpointRestored=True *")) {
                    throw "Saved gold or pouch contents changed before floor $floor. See $file"
                }
                $travel = @($report.sections | Where-Object name -eq "travel-$($floor-1)-$floor")
                if ($travel.Count -ne 1 -or $travel[0].frames -lt 2) { throw "Missing floor transition $floor. See $file" }
                if ($MaxAreaFrameMs -gt 0 -and $travel[0].worst -gt $MaxAreaFrameMs) {
                    $overBudget += "$($travel[0].name) $([math]::Round($travel[0].worst,2))ms ($file)"
                }
            }
            if ($AreaBurst -and $FloorCount -gt 1) {
                $retainedPattern = if ($NaturalGc) { '\d+' } else { '0' }
                if (!($report.sceneResources -match "^floor-$floor oldRoomWrappers=$retainedPattern handoffLoaded=False$")) {
                    throw "Old room or handoff scene retained at floor $floor. See $file"
                }
                foreach ($label in @("floor-$floor", "${prefix}authored-room-64")) {
                    $section = @($report.sections | Where-Object name -eq $label)
                    if ($section.Count -ne 1) { throw "Missing $label. See $file" }
                    if ($MaxAreaFrameMs -gt 0 -and $section[0].worst -gt $MaxAreaFrameMs) {
                        $overBudget += "$label $([math]::Round($section[0].worst, 2))ms ($file)"
                    }
                }
            }
            $row = @($report.sections | Where-Object name -eq "floor-$floor")
            $snapshot = @($report.sceneResources -like "floor=$floor rooms=*")
            if ($row.Count -ne 1 -or $row[0].frames -le 0 -or $snapshot.Count -ne 1 -or
                $snapshot[0] -notmatch 'oldRuntimeNav=0 persistentRoots=(\d+) .*agents=0 ') {
                throw "Unexpected resources at floor $floor. See $file"
            }
            $count = [int]$Matches[1]
            if ($null -ne $roots -and $count -ne $roots) { throw "Persistent roots changed at floor $floor. See $file" }
            $roots = $count
            if (!($report.sceneResources -like "floor-$floor seed=907 frameTargets=1 floorMasks=1 particleFields=1") -or
                (!$AreaBurst -and !($report.sceneResources -match "^clearedFloor=$floor clear=True hits=[1-9]\d*$"))) {
                throw "Missing encounter clear or scene renderer at floor $floor. See $file"
            }
        }
        if ($overBudget.Count -eq $budgetErrorsAtStart) { Write-Output "PASS $file" }
        else { Write-Output "Frame budget exceeded: $file" }
        continue
    }
    if ($Transitions) {
        foreach ($label in 'menu-village','first-dungeon','floor-portal','reward-return','dungeon-revisit') {
            $row = @($report.sections | Where-Object name -eq $label)
            if ($row.Count -ne 1 -or $row[0].frames -le 0 -or !($report.sceneResources -like "$label input released *")) {
                throw "Incomplete $label. See $file"
            }
        }
        Write-Output "PASS $file"
        continue
    }
    $counts = if ($LargeCrowd) { 32,64 } else { 1,4,8,16 }
    foreach ($count in $counts) {
        foreach ($phase in 'movement','spawn','hits','drops') {
            $name = "crowd-$count-$phase"
            $row = @($report.sections | Where-Object name -eq $name)
            if ($row.Count -ne 1 -or $row[0].frames -le 0 -or $row[0].gpuSamples -le 0 -or
                $row[0].endQpc-$row[0].startQpc -lt $Seconds*$report.ticksPerSecond) { throw "Incomplete $name. See $file" }
        }
    }
    Write-Output "PASS $file"
}
if ($overBudget.Count) { throw "Area frame budget ${MaxAreaFrameMs}ms exceeded: $($overBudget -join ', ')" }

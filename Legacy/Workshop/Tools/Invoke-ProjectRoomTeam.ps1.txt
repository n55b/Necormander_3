param([Parameter(Mandatory)][string]$Sandbox,[Parameter(Mandatory)][string]$UnityEditor,[Parameter(Mandatory)][string]$Results)
$ErrorActionPreference='Stop'
$Sandbox=(Resolve-Path -LiteralPath $Sandbox).Path
if ((Split-Path $Sandbox -Leaf) -notmatch '^NecromanderVerification-[a-f0-9]{12}$' -or
    (Get-Content -Raw -LiteralPath "$Sandbox/ProjectSettings/ProjectSettings.asset") -notmatch '(?m)^  companyName: NecromanderVerification\s*$') { throw 'Use an isolated verification project.' }
$Results=[IO.Directory]::CreateDirectory($Results).FullName
$marker=Join-Path $Results 'passed.txt'
if(Test-Path -LiteralPath $marker){Remove-Item -LiteralPath $marker}
$run=[IO.Directory]::CreateDirectory((Join-Path $env:TEMP ("Necromander-Team-"+[Guid]::NewGuid().ToString('N')))).FullName
$remote=Join-Path $run Remote.git
$a=Join-Path $run Terrain
$b=Join-Path $run Rewards
$linked=Join-Path $Sandbox DATA
$original=Join-Path $run OriginalDATA
$fixture=Join-Path $Results fixture.json
function Invoke-Git([string]$at,[string[]]$arguments) {
    $lines=& git -C $at -c 'user.name=Workshop QA' -c 'user.email=qa@localhost' -c 'commit.gpgsign=false' @arguments 2>&1
    if($LASTEXITCODE){throw "Git $arguments failed. $lines"}
    $lines | ForEach-Object { "$_" } | Add-Content -LiteralPath (Join-Path $Results collaboration.log)
}
function Use-Checkout([string]$checkout) {
    if(Test-Path -LiteralPath $linked) {
        $entry=Get-Item -LiteralPath $linked
        if ($entry.LinkType -ne 'Junction' -or !(($entry.Target -in @("$a\DATA","$b\DATA")))) {throw "Unexpected DATA target at $linked"}
        [IO.Directory]::Delete($linked)
    }
    [void](New-Item -ItemType Junction -Path $linked -Target (Join-Path $checkout DATA))
}
function Run-Unity([string]$phase,[string]$method='ProjectContentBuild.StartRoomCheck') {
    $log=Join-Path $Results "$phase.log"
    $arguments=@('-batchmode','-projectPath',('"'+$Sandbox+'"'),'-logFile',('"'+$log+'"'),'-executeMethod',$method)
    $build=$phase -in @('build','build-creator')
    if($build){$arguments+='-quit'}else{$arguments+=@('-projectRoomCheck',$phase,('"'+$fixture+'"'))}
    $extra=@{}
    if($phase -eq 'resolve'){$extra.Environment=@{GIT_DIR=(Join-Path $b '.git');GIT_WORK_TREE=$b}}
    $p=Start-Process $UnityEditor -ArgumentList $arguments -PassThru -WindowStyle Hidden @extra
    if(!$p.WaitForExit(600000)){$p.Kill();throw "Timeout $phase. $log"}
    if($p.ExitCode -or (!$build -and !(Select-String -SimpleMatch "[ProjectRoomCheck] $phase PASS" -LiteralPath $log -Quiet))){throw "Failed $phase. $log"}
}
Invoke-Git $run @('init','--bare','--initial-branch=main',$remote)
Invoke-Git $run @('clone',$remote,$a)
[void][IO.Directory]::CreateDirectory("$a/DATA")
$sourceData=Join-Path $PSScriptRoot '../../DATA'
& robocopy (Resolve-Path -LiteralPath $sourceData).Path "$a/DATA" /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if($LASTEXITCODE -ge 8){throw 'Failed to seed the test checkout with project content.'}
'*.json text eol=lf' | Set-Content -LiteralPath "$a/.gitattributes"
if(Test-Path -LiteralPath $linked){Move-Item -LiteralPath $linked -Destination $original}
try {
    Use-Checkout $a
    $marker=Join-Path $Sandbox 'Library/project-content-crash.marker'
    if(Test-Path -LiteralPath $marker){Remove-Item -LiteralPath $marker}
    $crashLog=Join-Path $Results 'interrupted.log'
    $writer=Start-Process $UnityEditor -ArgumentList @('-batchmode','-projectPath',('"'+$Sandbox+'"'),'-projectContentCrashCheck',
        '-executeMethod','ProjectRoomVerification.InterruptSave','-logFile',('"'+$crashLog+'"'),'-quit') -PassThru -WindowStyle Hidden
    if(!$writer.WaitForExit(180000)){$writer.Kill();throw 'Test writer did not terminate.'}
    if(!(Test-Path -LiteralPath $marker) -or !(Test-Path "$a/DATA/QA/SAVE-RECOVERY/a.json") -or (Test-Path "$a/DATA/QA/SAVE-RECOVERY/b.json")){
        throw "First file must reach disk before the test writer terminates. $crashLog"
    }
    Run-Unity seed
    Invoke-Git $a @('add','DATA','.gitattributes')
    Invoke-Git $a @('commit','-m','Shared room baseline')
    Invoke-Git $a @('push','-u','origin','main')
    Invoke-Git $run @('clone',$remote,$b)
    Run-Unity terrain
    Invoke-Git $a @('add','DATA')
    $terrain=@(& git -C $a diff --cached --name-only)
    if($terrain.Count -ne 1 -or $terrain[0] -notlike 'DATA/WORLD/ROOMS/*/TILES/Ground/*.json'){throw "Unexpected terrain diff: $terrain"}
    Invoke-Git $a @('commit','-m','Edit ground using Ctrl+S')
    Invoke-Git $a @('push')
    Use-Checkout $b
    Run-Unity reward
    Invoke-Git $b @('add','DATA')
    $reward=@(& git -C $b diff --cached --name-only)
    if($reward.Count -ne 1 -or $reward[0] -notlike 'DATA/WORLD/ROOMS/*/ENCOUNTER/reward.json'){throw "Unexpected reward diff: $reward"}
    Invoke-Git $b @('commit','-m','Edit clear reward using Save')
    Invoke-Git $b @('pull','--no-rebase','--no-edit','origin','main')
    Invoke-Git $b @('push')
    Invoke-Git $a @('pull','--ff-only')
    Run-Unity verify
    Run-Unity build 'ProjectContentBuild.BuildRoomCheck'
    $log=Join-Path $Results 'player.log'
    $p=Start-Process "$Sandbox/Builds/ProjectContent/Game.exe" -ArgumentList @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-projectRoomCheck','verify',('"'+$fixture+'"'),'-logFile',('"'+$log+'"')) -PassThru -WindowStyle Hidden
    if(!$p.WaitForExit(150000)){$p.Kill();throw 'Player timeout'}
    if($p.ExitCode -or !(Select-String -SimpleMatch '[ProjectRoomCheck] verify PASS' -LiteralPath $log -Quiet)){throw "Player failed. $log"}
    $merged=& git -C $b rev-parse HEAD
    $rewardFile=$reward[0]
    Invoke-Git $a @('checkout','-b','qa-conflict-source')
    "{`n  `"reward`": 1`n}" | Set-Content -LiteralPath (Join-Path $a $rewardFile) -Encoding utf8NoBOM
    Invoke-Git $a @('commit','-am','First reward choice')
    Invoke-Git $a @('push','origin','qa-conflict-source')
    Invoke-Git $b @('checkout','-b','qa-conflict-local')
    "{`n  `"reward`": 2`n}" | Set-Content -LiteralPath (Join-Path $b $rewardFile) -Encoding utf8NoBOM
    Invoke-Git $b @('commit','-am','Second reward choice')
    Invoke-Git $b @('fetch','origin')
    & git -C $b -c user.name=QA -c user.email=qa@localhost merge --no-commit origin/qa-conflict-source | Out-Null
    if($LASTEXITCODE -ne 1){throw 'Expected an overlapping Git conflict'}
    Run-Unity resolve
    if((& git -C $b ls-files -u) -or (Get-Content -Raw (Join-Path $b $rewardFile) | ConvertFrom-Json).reward -ne 2){throw 'Conflict not resolved by workshop'}
    Run-Unity world
    Run-Unity worldverify
    Run-Unity build-creator 'ProjectContentBuild.BuildCreatorCheck'
    [void][IO.Directory]::CreateDirectory((Join-Path $b ProjectSettings))
    Copy-Item -LiteralPath "$Sandbox/ProjectSettings/ProjectVersion.txt" -Destination "$b/ProjectSettings/ProjectVersion.txt"
    $log=Join-Path $Results 'creator.log'
    $p=Start-Process "$Sandbox/Builds/ProjectContentCreator/Game.exe" -ArgumentList @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-projectRoomCheck','creator',('"'+$fixture+'"'),('"-checkout='+$b+'"'),'-logFile',('"'+$log+'"')) -PassThru -WindowStyle Hidden
    if(!$p.WaitForExit(150000)){$p.Kill();throw 'Creator connection timeout'}
    if($p.ExitCode -or !(Select-String -SimpleMatch '[ProjectRoomCheck] creator PASS' -LiteralPath $log -Quiet) -or
        (Get-Content -Raw -LiteralPath (Join-Path $b $rewardFile) | ConvertFrom-Json).reward -ne 1){throw "Creator did not save to the connected checkout. $log"}
    @("Terrain $($terrain[0])","Reward $rewardFile","Merged $merged","Remote $remote","PASS Unity Editor and Windows player",
      "PASS same-field conflict reviewed and staged","PASS interrupted Unity writer and same-chunk merge","PASS new room and village in ordinary scenes",
      "PASS authoring Windows player connected and saved") | Set-Content -LiteralPath (Join-Path $Results 'passed.txt')
} finally {
    if(Test-Path -LiteralPath $linked) {
        $entry=Get-Item -LiteralPath $linked
        if($entry.LinkType -eq 'Junction' -and $entry.Target -in @("$a\DATA","$b\DATA")){[IO.Directory]::Delete($linked)}
    }
    if((Test-Path -LiteralPath $original) -and !(Test-Path -LiteralPath $linked)){Move-Item -LiteralPath $original -Destination $linked}
}

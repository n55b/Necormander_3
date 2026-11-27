[CmdletBinding(DefaultParameterSetName='Full')]
param(
    [Parameter(Mandatory)][string] $Player,
    [Parameter(Mandatory)][string] $Results,
    [Parameter(ParameterSetName='Handoff')][switch] $HandoffOnly,
    [Parameter(ParameterSetName='Soak')][switch] $SoakOnly
)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Player).Path
$logs=(New-Item -ItemType Directory -Force -Path $Results).FullName
function Assert-WorkshopShutdown([string]$LogPath) {
    if (Select-String -LiteralPath $LogPath -SimpleMatch 'GarbageCollector disposing of ComputeBuffer' -Quiet) {
        throw "Unreleased ComputeBuffer on workshop exit | $LogPath"
    }
}
if (!$HandoffOnly) {
    $output=(New-Item -ItemType Directory -Force -Path (Join-Path $logs 'soak')).FullName
    $log=Join-Path $output 'workshop-player.log'
    $argsList=@('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
        '-workshopSoak','-workshopReport',('"'+$output+'"'),'-logFile',('"'+$log+'"'))
    $start=Get-Date
    $checkProcess=Start-Process -FilePath $exe -ArgumentList $argsList -WindowStyle Hidden -PassThru
    if (!$checkProcess.WaitForExit(300000)) { $checkProcess.Kill(); $checkProcess.WaitForExit() }
    $proof=Join-Path $output 'passed.txt'
    Assert-WorkshopShutdown $log
    if ($checkProcess.ExitCode -ne 0 -or !(Test-Path $proof) -or (Get-Item $proof).LastWriteTime -lt $start -or
        !((Get-Content $proof) -contains 'warm transitions within one second at p95')) {
        throw "FAIL repeated workshop trials | $log"
    }
    Write-Output "PASS repeated workshop trials | $output"
    if ($SoakOnly) { return }
}
$sizes=if (!$HandoffOnly) { '640x360','1280x720','1366x768','1920x1080' }
foreach($size in $sizes) {
    $w,$h=$size.Split('x')
    $output=(New-Item -ItemType Directory -Force -Path (Join-Path $logs $size)).FullName
    $log=Join-Path $output 'workshop-player.log'
    $argsList=@('-force-d3d11','-screen-fullscreen','0','-screen-width',$w,'-screen-height',$h,
        '-workshopReport', ('"' + $output + '"'), '-logFile', ('"' + $log + '"'))
    $start=Get-Date
    $checkProcess=Start-Process -FilePath $exe -ArgumentList $argsList -WindowStyle Hidden -PassThru
    if (!$checkProcess.WaitForExit(300000)) { $checkProcess.Kill(); $checkProcess.WaitForExit() }
    Assert-WorkshopShutdown $log
    $proof=Join-Path $output 'passed.txt'
    if ($checkProcess.ExitCode -eq 0 -and (Test-Path $proof) -and (Get-Item $proof).LastWriteTime -ge $start -and ((Get-Content $proof) -contains "window $w x $h")) {
        Write-Output "PASS standalone workshop $size | $output"
    } else { throw "FAIL standalone workshop $size | $log" }
}

$handoff=Join-Path $logs '1280x720/handoff.levelpack'
if (!(Test-Path -LiteralPath $handoff -PathType Leaf)) { throw "Missing handoff package: $handoff" }
$recipientHome=Join-Path ([IO.Path]::GetTempPath()) ("Necromander-Workshop-Recipient-"+[guid]::NewGuid().ToString('N'))
foreach($phase in 'receive','reopen'){
    $output=(New-Item -ItemType Directory -Force -Path (Join-Path $logs $phase)).FullName
    $log=Join-Path $output 'workshop-player.log'
    $argsList=@('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
        '-workshopReport',('"'+$output+'"'),'-logFile',('"'+$log+'"'),
        '-workshopRecipient',('"'+$handoff+'"'),('"'+$recipientHome+'"'))
    if($phase -eq 'reopen'){ $argsList+='-workshopReopen' }
    $start=Get-Date
    $checkProcess=Start-Process -FilePath $exe -ArgumentList $argsList -WindowStyle Hidden -PassThru
    if(!$checkProcess.WaitForExit(180000)){ $checkProcess.Kill(); $checkProcess.WaitForExit() }
    Assert-WorkshopShutdown $log
    $proof=Join-Path $output 'passed.txt'
    if($checkProcess.ExitCode -ne 0 -or !(Test-Path $proof) -or (Get-Item $proof).LastWriteTime -lt $start -or
       !((Get-Content $proof) -contains 'fresh process plays village with transferred NPC and lighting')){
        throw "FAIL standalone workspace $phase | $log"
    }
    Write-Output "PASS standalone workspace $phase | $output"
}

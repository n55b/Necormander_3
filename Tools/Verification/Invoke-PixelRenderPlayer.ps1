[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Run')][string]$Player,
    [Parameter(Mandatory, ParameterSetName = 'Review')][switch]$ReviewOnly,
    [Parameter(Mandatory)][string]$Results,
    [string]$ExpectedGpu
)
$ErrorActionPreference = 'Stop'
if (!$ReviewOnly) { New-Item -ItemType Directory -Force -Path $Results | Out-Null }
$output = (Resolve-Path -LiteralPath $Results).Path
$report = Join-Path $output 'pixel-render-60.json'
$log = Join-Path $output 'pixel-render-player.log'
$windowReport = Join-Path $output 'WindowChecks/results.txt'
if (!$ReviewOnly) {
    $exe = (Resolve-Path -LiteralPath $Player).Path
    $arguments = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
        '-windowQA','-pixelBench',('"'+$report+'"'),'-benchSeconds','60','-logFile',('"'+$log+'"'))
    $started = Get-Date
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if (!$process.WaitForExit(720000)) { $process.Kill(); throw "Benchmark timed out. See $log" }
    if ($process.ExitCode -ne 0) { throw "Benchmark player exited with $($process.ExitCode). See $log" }
    foreach ($path in @($report, $windowReport)) {
        if (!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).LastWriteTime -lt $started) {
            throw "Benchmark player did not produce a new $path. See $log"
        }
    }
}
$data = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
if (!$data.device -or $data.api -ne 'Direct3D11') { throw "Missing D3D11 device identity in $report" }
if ($data.ticksPerSecond -le 0) { throw "Missing frame timer frequency in $report" }
if ($ExpectedGpu -and $data.device -ne $ExpectedGpu) {
    throw "Expected GPU '$ExpectedGpu', measured '$($data.device)'. See $report"
}
$windows = @(Get-Content -LiteralPath $windowReport)
foreach ($size in @('640x360','1280x720','1366x768','1920x1080')) {
    $width, $height = $size.Split('x')
    $expected = "$size actual=$size frame=640x360 rect=(x:0, y:0, width:$width, height:$height) cornerColors=4/4 inputRaycasts=4/4 clicks=4/4 logicalTargets=1"
    if (@($windows -eq $expected).Count -ne 1) { throw "Missing successful $size viewport and input check. See $windowReport" }
}
if (@($data.sceneResources | Where-Object {$_ -match 'seed=907 frameTargets=1 floorMasks=1 particleFields=1'}).Count -ne 3 -or !($data.sceneResources -like 'boss clear=True*')) {
    throw "Reload or boss clear verification failed. See $report"
}
$crowd = @($data.sections | Where-Object name -eq 'dungeon-crowd')
if ($crowd.Count -ne 1 -or !($data.sceneResources -match 'active dungeon encounter peakActors=[1-9][0-9]* successfulHits=[1-9][0-9]*')) {
    throw "A live dungeon encounter was not measured. See $report"
}
$scenarios = @('Analytic-Low','Analytic-Medium','Analytic-High','Compute-Low','Compute-Medium','Compute-High','dungeon-crowd')
$measured = foreach ($name in $scenarios) {
    $row = @($data.sections | Where-Object name -eq $name)
    if ($row.Count -ne 1 -or $row[0].frames -le 0 -or $row[0].gpuSamples -le 0 -or
        $row[0].cpu95 -le 0 -or $row[0].gpu95 -le 0 -or $row[0].p99 -le 0) {
        throw "Missing CPU, GPU or frame samples for $name. See $report"
    }
    if (($row[0].endQpc - $row[0].startQpc) -lt 60 * $data.ticksPerSecond) {
        throw "The $name timing sample covers less than 60 seconds. See $report"
    }
    $row[0]
}
$measured | Format-Table name,frames,cpu95,gpu95,p99,worst,allocatedBytes95,areaDrops
$over = @($measured | Where-Object { $_.cpu95 -gt 13.3 -or $_.gpu95 -gt 13.3 -or $_.p99 -gt 16.7 })
if ($over.Count) { throw "Draft timing budget exceeded on $($data.device) in $($over.name -join ', '). See $report" }
Write-Host "PASS $($data.device) | $report"

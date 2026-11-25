[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Before,
    [Parameter(Mandatory)][string] $After,
    [Parameter(Mandatory)][string] $OutputCsv,
    [switch] $Transitions
)
$ErrorActionPreference = 'Stop'
$prefix = if ($Transitions) { 'transitions' } else { 'matrix' }
$cases = if ($Transitions) {
    'menu-village', 'first-dungeon', 'floor-portal', 'reward-return', 'dungeon-revisit'
} else {
    foreach ($count in 1,4,8,16) {
        foreach ($phase in 'movement','spawn','hits','drops') { "crowd-$count-$phase" }
    }
}
function Read-Runs([string] $folder) {
    foreach ($number in 1..3) {
        $file = Join-Path $folder "$prefix-$number.json"
        $report = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
        if ($report.ticksPerSecond -le 0) { throw "Missing timer in $file" }
        foreach ($case in $cases) {
            $rows = @($report.sections | Where-Object name -eq $case)
            if ($rows.Count -ne 1 -or $rows[0].frames -le 0) { throw "Incomplete $case in $file" }
            if (!$Transitions -and ($rows[0].gpuSamples -le 0 -or
                $rows[0].endQpc - $rows[0].startQpc -lt 60 * $report.ticksPerSecond)) {
                throw "Missing 60s CPU/GPU sample for $case in $file"
            }
        }
        $report
    }
}
function Median($values) { [Math]::Round([double](@($values | Sort-Object)[1]), 2) }
$beforeRuns = @(Read-Runs $Before)
$afterRuns = @(Read-Runs $After)
foreach ($field in 'device','api','cpu','width','height','seed') {
    $values = @(($beforeRuns + $afterRuns) | ForEach-Object { $_.$field } | Sort-Object -Unique)
    if ($values.Count -ne 1) { throw "Runs differ on $field" }
}
$table = foreach ($case in $cases) {
    foreach ($version in 'Before','After') {
        $runs = if ($version -eq 'Before') { $beforeRuns } else { $afterRuns }
        $rows = @($runs | ForEach-Object { $_.sections | Where-Object name -eq $case })
        $elapsed = for ($n=0; $n -lt 3; $n++) {
            1000 * ($rows[$n].endQpc - $rows[$n].startQpc) / $runs[$n].ticksPerSecond
        }
        [pscustomobject][ordered]@{
            scene = $case
            version = $version
            medianDurationMs = Median $elapsed
            medianCpu95Ms = if (!$Transitions) { Median $rows.cpu95 }
            medianGpu95Ms = if (!$Transitions) { Median $rows.gpu95 }
            medianP99Ms = Median $rows.p99
            medianWorstMs = Median $rows.worst
            worstAnyRunMs = [Math]::Round([double](($rows.worst | Measure-Object -Maximum).Maximum), 2)
            above33msTotal = if ($null -ne $rows[0].PSObject.Properties['above33ms']) { ($rows.above33ms | Measure-Object -Sum).Sum }
            visibleAbove33msTotal = if ($Transitions -and $null -ne $rows[0].PSObject.Properties['visibleAbove33ms']) { ($rows.visibleAbove33ms | Measure-Object -Sum).Sum }
            worstVisibleMs = if ($Transitions -and $null -ne $rows[0].PSObject.Properties['worstVisible']) { [Math]::Round([double](($rows.worstVisible | Measure-Object -Maximum).Maximum), 2) }
        }
    }
}
$table | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding utf8
$table

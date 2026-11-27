[CmdletBinding()]
param(
    [ValidateSet('Baseline', 'Assets', 'Presentation', 'World', 'Dungeon', 'Combat', 'Save', 'Windows', 'Performance', 'Typography', 'UIResolution', 'ArtDirection', 'RenderBudget', 'CombatPolish', 'LevelWorkshop', 'WorkshopPlayMode','ProjectContent')]
    [string[]] $Suite = @('Baseline'),
    [string] $UnityEditor,
    [string] $CompareTo,
    [switch] $IncludeWindowsBuild
)

$ErrorActionPreference = 'Stop'
function Want([string[]] $names) { @($Suite | Where-Object { $_ -in $names }).Count -gt 0 }
if ($CompareTo) {
    if (-not (Want @('Baseline', 'Assets'))) { throw '-CompareTo requires the Baseline or Assets suite.' }
    # Read now: the shared workspace clears its logs before this run starts.
    $previousInventory = Get-Content -LiteralPath (Resolve-Path -LiteralPath $CompareTo).Path -Raw
}
$source = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$legacyExtensions = '.cs', '.dll', '.exe', '.asmdef', '.asmref', '.shader', '.shadergraph', '.compute',
    '.ps1', '.cmd', '.bat', '.sh', '.py', '.js', '.mjs', '.ts', '.lua'
$activeLegacyFiles = @(Get-ChildItem -LiteralPath (Join-Path $source 'Legacy') -File -Recurse -Force |
    Where-Object { $_.Extension -in $legacyExtensions })
if ($activeLegacyFiles.Count) {
    throw "Archive executable files with a .txt suffix: $($activeLegacyFiles.FullName -join ', ')"
}
# Every Legacy subfolder must be read-denied for agents; Legacy/README.md stays readable as the index.
$denied = (Get-Content -LiteralPath (Join-Path $source '.claude/settings.json') -Raw | ConvertFrom-Json).permissions.deny
$unblocked = @(Get-ChildItem -LiteralPath (Join-Path $source 'Legacy') -Directory |
    Where-Object { "Read(/Legacy/$($_.Name)/**)" -notin $denied })
if ($unblocked.Count) {
    throw "Add Read(/Legacy/<folder>/**) to .claude/settings.json for: $($unblocked.Name -join ', ')"
}
$version = (Select-String -LiteralPath (Join-Path $source 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (\S+)').Matches[0].Groups[1].Value
if (-not $UnityEditor) {
    $UnityEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
$UnityEditor = (Resolve-Path -LiteralPath $UnityEditor).Path

# One reusable isolated copy. Mirroring keeps its Library (import cache), so only the first run imports everything.
# The id must stay 12 hex digits: helper scripts check the folder name and derive the product name from it.
$runId = '5a4dbc0ffee0'
$sandbox = Join-Path ([IO.Path]::GetTempPath()) "NecromanderVerification-$runId"
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
$lock = try { [IO.File]::Open("$sandbox.lock", 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw "Another verification run is using $sandbox." }
Write-Host "Verification workspace: $sandbox"

foreach ($name in @('Assets', 'Packages', 'ProjectSettings', 'ARTIST', 'DATA')) {
    & robocopy.exe (Join-Path $source $name) (Join-Path $sandbox $name) /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "Failed to copy $name (robocopy $LASTEXITCODE)." }
}

# The Unity MCP editor package can stop the local MCP server on editor shutdown.
# Exclude it from the verification copy so the user's live editor keeps its session.
$manifestPath = Join-Path $sandbox 'Packages/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.dependencies.PSObject.Properties.Remove('com.coplaydev.unity-mcp')
$manifest | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath $manifestPath -Encoding utf8

# Run tests against a separate PlayerPrefs registry and persistent data directory.
$settingsPath = Join-Path $sandbox 'ProjectSettings/ProjectSettings.asset'
$settings = [IO.File]::ReadAllText($settingsPath)
foreach ($item in @{ companyName = 'NecromanderVerification'; productName = "Verification-$runId" }.GetEnumerator()) {
    $pattern = "(?m)^(  $($item.Key): )[^\r\n]*"
    if (-not [regex]::IsMatch($settings, $pattern)) { throw "Missing player setting $($item.Key)." }
    $settings = [regex]::Replace($settings, $pattern, ('${1}' + $item.Value))
}
[IO.File]::WriteAllText($settingsPath, $settings)

# Reset per-run state so a reused workspace behaves like a fresh one.
$product = "Verification-$runId"
foreach ($stale in @((Join-Path ([Environment]::GetFolderPath('UserProfile')) "AppData/LocalLow/NecromanderVerification/$product"),
    "HKCU:/Software/NecromanderVerification/$product", "HKCU:/Software/Unity/UnityEditor/NecromanderVerification/$product")) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }
}
$logs = Join-Path $sandbox 'Logs/Verification'
if (Test-Path -LiteralPath $logs) { Remove-Item -LiteralPath $logs -Recurse -Force }
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$results = [Collections.Generic.List[string]]::new()

function Invoke-EditorCheck {
    param([string] $Method, [switch] $PlayMode, [switch] $Build, [string[]] $ExtraArgs = @())

    $log = Join-Path $logs "$Method.log"
    $arguments = @('-batchmode', '-projectPath', ('"' + $sandbox + '"'), '-executeMethod', $Method, '-logFile', ('"' + $log + '"'))
    $arguments += $ExtraArgs
    if (-not $PlayMode) { $arguments += '-quit' }
    if ($Build) { $arguments += @('-activeBuildProfile', '"Assets/Settings/Build Profiles/New Windows Profile.asset"') }
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Host "Running $Method (PID $($process.Id))"
    if (-not $process.WaitForExit(900000)) {
        $process.Kill()
        $process.WaitForExit()
        $result = "FAIL $Method (timed out)"
    } elseif ($process.ExitCode -ne 0) {
        $result = "FAIL $Method (exit $($process.ExitCode))"
    } else {
        $result = "PASS $Method"
    }
    $results.Add("$result | $log")
    Write-Host $result
}

if ((Want @('Baseline', 'Assets'))) {
    Invoke-EditorCheck 'ProjectVerification.CaptureAssets'
    if ($CompareTo -and $results[0].StartsWith('PASS ')) {
        $previous = $previousInventory | ConvertFrom-Json
        $current = Get-Content -LiteralPath (Join-Path $logs 'asset-inventory.json') -Raw | ConvertFrom-Json
        $report = [Collections.Generic.List[string]]::new()
        foreach ($field in @('editorVersion', 'windowsBuildProfile', 'windowsDefines', 'enabledScenes', 'files', 'objectReferences', 'missingGuids', 'eventMethods', 'managedTypes', 'runtimeLookups')) {
            if ($null -eq $previous.PSObject.Properties[$field]) { throw "Baseline lacks $field." }
            $before = [string[]] @($previous.$field | Where-Object { $null -ne $_ })
            $after = [string[]] @($current.$field | Where-Object { $null -ne $_ })
            $beforeSet = [Collections.Generic.HashSet[string]]::new([Collections.Generic.IEnumerable[string]] $before, [StringComparer]::Ordinal)
            $afterSet = [Collections.Generic.HashSet[string]]::new([Collections.Generic.IEnumerable[string]] $after, [StringComparer]::Ordinal)
            $report.Add($field)
            foreach ($item in $before) { if (!$afterSet.Contains($item)) { $report.Add("- $item") } }
            foreach ($item in $after) { if (!$beforeSet.Contains($item)) { $report.Add("+ $item") } }
        }
        $reportPath = Join-Path $logs 'asset-inventory-diff.txt'
        $report | Set-Content -LiteralPath $reportPath -Encoding utf8

        # Match by owner GUID to allow moving an asset with its .meta file.
        function Get-UnresolvedReferences($inventory) {
            $owners = @{}
            foreach ($file in $inventory.files) {
                $guid, $path = $file -split ' \| ', 2
                $owners[$path] = $guid
            }
            foreach ($missing in $inventory.missingGuids) {
                $path, $guid = $missing -split ' \| ', 2
                "$($owners[$path]) | $guid"
            }
        }
        $known = @(Get-UnresolvedReferences $previous)
        $added = @(Get-UnresolvedReferences $current | Where-Object { $_ -cnotin $known })
        $result = if ($added.Count) { "FAIL asset comparison ($($added.Count) new unresolved GUID references)" }
        else { 'PASS asset comparison (no new unresolved GUID references)' }
        $results.Add("$result | $reportPath")
        Write-Host $result
    }
}
if ((Want @('Baseline', 'World'))) { Invoke-EditorCheck 'ProjectVerification.CheckWorldAssets' }
if ((Want @('Baseline', 'World', 'Dungeon'))) { Invoke-EditorCheck 'DungeonLayoutCheck.Run' -PlayMode }
if ((Want @('UIResolution'))) {
    Invoke-EditorCheck 'UiResolutionCheck.Run' -PlayMode
    Invoke-EditorCheck 'UiResolutionCheck.RunWorkshop' -PlayMode
    Invoke-EditorCheck 'UiResolutionCheck.RunInventory' -PlayMode
}
if ((Want @('ArtDirection'))) {
    Invoke-EditorCheck 'ArtistWorkflowCheck.Run' -PlayMode
    Invoke-EditorCheck 'PixelDensityAudit.Run'
    Invoke-EditorCheck 'ArtDirectionPlayModeCheck.Run' -PlayMode
}
if ((Want @('Performance'))) { Invoke-EditorCheck 'MiniMapCheck.MeasureTerrain' }
if ((Want @('Baseline', 'Presentation', 'Typography'))) { Invoke-EditorCheck 'PixelTypographyCheck.Run' }
if ((Want @('Baseline', 'Presentation', 'World', 'Performance', 'Typography'))) { Invoke-EditorCheck 'PresentationRegressionCheck.Run' -PlayMode }
if ((Want @('Baseline', 'Typography'))) { Invoke-EditorCheck 'InventoryUICheck.RunRuntimeBatch' -PlayMode }
if ((Want @('Baseline', 'Presentation'))) { Invoke-EditorCheck 'HealingFeedbackValidation.Run' -PlayMode }
if ((Want @('Baseline', 'Combat'))) {
    Invoke-EditorCheck 'CombatMovementCheck.RunPlayerControlChecks'
    Invoke-EditorCheck 'EnemyAIDecisionCheck.Run'
    Invoke-EditorCheck 'BoneMasterCombatCheck.Run'
    Invoke-EditorCheck 'EliteTelegraphCleanupCheck.RunBatch' -PlayMode
    Invoke-EditorCheck 'PlaytestRegressionCheck.Run' -PlayMode
    Invoke-EditorCheck 'PlaytestRegressionCheck.RunThreats' -PlayMode
}
if ((Want @('Baseline', 'Save'))) {
    Invoke-EditorCheck 'SaveCompatibilityCheck.VerifyFileStorage'
    Invoke-EditorCheck 'SaveCompatibilityCheck.VerifyBakedVariantKeys'
    Invoke-EditorCheck 'SaveCompatibilityCheck.VerifyItemLifecycle'
    Invoke-EditorCheck 'SaveCompatibilityCheck.WriteAndReload' -PlayMode
    Invoke-EditorCheck 'SaveCompatibilityCheck.ReadAfterRestart' -PlayMode
}
if ((Want @('CombatPolish'))) {
    Invoke-EditorCheck 'SceneLoadingTools.RunTransitions' -PlayMode
    Invoke-EditorCheck 'ArtDirectionPlayModeCheck.Run' -PlayMode
    Invoke-EditorCheck 'PixelTypographyCheck.Run'
    Invoke-EditorCheck 'CombatMovementCheck.RunPlayerControlChecks'
}
if ((Want @('RenderBudget','CombatPolish'))) {
    & (Join-Path $source 'Tools/Verification/Build-GpuTimer.ps1') -UnityEditor $UnityEditor -VerificationProject $sandbox
    Invoke-EditorCheck 'PixelBenchmarkBuild.Run'
    if ($results[$results.Count-1] -like 'PASS*') {
        $exe = Join-Path $sandbox 'Builds/PixelBench/PixelBench.exe'
        if ((Want @('CombatPolish'))) {
            $matrix = Join-Path $source 'Tools/Verification/Invoke-CrowdMatrix.ps1'
            & $matrix -Player $exe -Results $logs -Transitions
            & $matrix -Player $exe -Results $logs -Floors -RoomBurst -Runs 1 -Seconds 2
            & $matrix -Player $exe -Results $logs -AreaBurst -Runs 1 -Seconds 2
            & $matrix -Player $exe -Results $logs
            $results.Add("PASS five transitions, six cleared floors, 64-enemy spawning, area damage and loot, and four crowd sizes | $logs")
        } else {
            & (Join-Path $source 'Tools/Verification/Invoke-PixelRenderPlayer.ps1') -Player $exe -Results $logs
            $results.Add("PASS Windows frame sampling and four native windows | $logs")
        }
    }
}
if ((Want @('LevelWorkshop', 'ProjectContent'))) { Invoke-EditorCheck 'ProjectRoomRebaseCheck.Run' -PlayMode }
if ((Want @('WorkshopPlayMode', 'LevelWorkshop'))) {
    Invoke-EditorCheck 'WorkshopPlayModeCheck.Run' -PlayMode
    Invoke-EditorCheck 'ProjectContentBuild.StartRoomCheck' -PlayMode -ExtraArgs @('-projectRoomCheck', 'passages', 'unused')
}
if ((Want @('ProjectContent'))) {
    Invoke-EditorCheck 'ProjectDataBakeCheck.Run'
    Invoke-EditorCheck 'ProjectRoomBakeCheck.Run'
    & (Join-Path $source 'Tools/Verification/Invoke-ProjectRoomTeam.ps1') -Sandbox $sandbox -UnityEditor $UnityEditor -Results $logs
    $results.Add("PASS project content Ctrl+S, save button, two Git checkouts and Windows dungeon | $logs")
}
if ((Want @('LevelWorkshop'))) {
    Invoke-EditorCheck 'WorkshopRoomImport.VerifySources'
    Invoke-EditorCheck 'WorkshopBuild.BuildVerificationPlayer'
    if ($results[$results.Count-1] -like 'PASS*') {
        $exe = Join-Path $sandbox 'Builds/LevelWorkshop/QA/NecromanderWorkshop.exe'
        & (Join-Path $source 'Tools/Verification/Invoke-WorkshopPlayer.ps1') -Player $exe -Results $logs
        $results.Add("PASS workshop trial latency and resource lifetime, largest room stamp and undo, four window sizes and recipient import across process restarts | $logs")
    }
}
if ((Want @('Windows')) -or $IncludeWindowsBuild) { Invoke-EditorCheck 'ProjectVerification.BuildWindows' -Build }
$results | Set-Content -LiteralPath (Join-Path $logs 'verification-summary.txt') -Encoding utf8
# Keep each run's logs; the shared Logs/Verification folder is cleared by the next run.
$archive = Join-Path $sandbox ('Logs/Runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + ($Suite -join '+'))
New-Item -ItemType Directory -Path (Split-Path $archive) -Force | Out-Null
Copy-Item -LiteralPath $logs -Destination $archive -Recurse
Get-ChildItem -LiteralPath (Split-Path $archive) -Directory | Sort-Object Name -Descending | Select-Object -Skip 20 |
    Remove-Item -Recurse -Force
$lock.Dispose()
Write-Host "Results: $archive"
if ($results -like 'FAIL *') { exit 1 }

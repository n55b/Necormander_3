param([Parameter(Mandatory)][string]$UnityEditor, [Parameter(Mandatory)][string]$VerificationProject)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $VerificationProject).Path
$settings = Get-Content -Raw -LiteralPath (Join-Path $project 'ProjectSettings/ProjectSettings.asset')
if ($settings -notmatch '(?m)^  companyName: NecromanderVerification\s*$') { throw 'Use an isolated verification project.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Visual Studio x64 C++ compiler unavailable.' }
$tools = Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
$include = Join-Path (Split-Path $UnityEditor) 'Data/PluginAPI'
$output = Join-Path $project 'Assets/Plugins/x86_64'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$source = Join-Path $PSScriptRoot 'PixelGpuTimer.cpp'
$build = Join-Path $project 'Logs/Verification'
Push-Location -LiteralPath $build
try {
    $command = ('call "{0}" -arch=x64 -host_arch=x64 >nul && cl /nologo /utf-8 /O2 /EHsc /MD /std:c++17 /LD /I"{1}" "{2}" /link /IMPLIB:PixelGpuTimer.lib /out:"{3}"' -f $tools,$include,$source,(Join-Path $output 'PixelGpuTimer.dll'))
    & $env:ComSpec /d /c $command
    if ($LASTEXITCODE -ne 0) {throw 'Timestamp plugin compilation failed.'}
} finally { Pop-Location }

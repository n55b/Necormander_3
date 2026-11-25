using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class PixelBenchmarkBuild
{
    public static void Run() => Build(BuildOptions.None);
    public static void RunWithProfiler() => Build(BuildOptions.Development);
    private static void Build(BuildOptions flags)
    {
        if (PlayerSettings.companyName!="NecromanderVerification")
            throw new InvalidOperationException("Build the benchmark in an isolated verification workspace.");
        bool previous=PlayerSettings.enableFrameTimingStats;
        try
        {
            PlayerSettings.enableFrameTimingStats=true;
            var timer = AssetImporter.GetAtPath("Assets/Plugins/x86_64/PixelGpuTimer.dll") as PluginImporter;
            if (timer != null && !timer.isPreloaded) { timer.isPreloaded=true; timer.SaveAndReimport(); }
            var options=new BuildPlayerOptions { scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
                locationPathName="Builds/PixelBench/PixelBench.exe",target=BuildTarget.StandaloneWindows64,
                options=flags,extraScriptingDefines=System.IO.File.Exists("Assets/Plugins/x86_64/PixelGpuTimer.dll") ? new[]{"NECRO_RENDER_BENCH","PIXEL_GPU_TIMER"} : new[]{"NECRO_RENDER_BENCH"} };
            var report=BuildPipeline.BuildPlayer(options);
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Pixel benchmark build failed.");
        }
        finally { PlayerSettings.enableFrameTimingStats=previous; }
    }
}

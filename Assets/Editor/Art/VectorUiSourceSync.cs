using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Rebuilds the committed UI mesh catalog after an artist saves a vector source.</summary>
[InitializeOnLoad]
public static class VectorUiSourceSync
{
    private const string Catalog = "Assets/Resources/UI/VectorUiCatalog.asset";
    private static readonly string Project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static readonly string Source = Path.Combine(Project, "ARTIST", "UI", "Vector");
    private static readonly string Dependencies = Path.Combine(Project, "Library", "ArtistSync", "VectorPython");
    private static Task<string> work;
    private static bool pending;

    public static string Status { get; private set; } = "SVG 원본 감시 중";

    public static bool IsSource(string path) => path.StartsWith(Source + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase) && (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));

    public static void CheckAll()
    {
        if (!Directory.Exists(Source)) return;
        var lastBuild = File.GetLastWriteTimeUtc(Path.Combine(Project, Catalog));
        if (Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories)
            .Any(file => IsSource(file) && File.GetLastWriteTimeUtc(file) > lastBuild))
            Schedule();
    }

    [MenuItem("Tools/아트/UI 벡터 다시 빌드")]
    public static void Schedule() => pending = true;

    public static void Tick()
    {
        if (work != null)
        {
            if (!work.IsCompleted) return;
            var complete = work;
            work = null;
            try
            {
                Status = complete.GetAwaiter().GetResult().Trim();
                AssetDatabase.ImportAsset(Catalog, ImportAssetOptions.ForceSynchronousImport);
                foreach (var image in UnityEngine.Object.FindObjectsByType<VectorUiImage>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
                    image.SetAllDirty();
            }
            catch (Exception exception)
            {
                Status = "SVG 빌드 실패 · ARTIST/README.md의 설치와 오류를 확인하세요.";
                Debug.LogWarning($"[Vector UI] {exception.Message}");
            }
        }

        if (!pending) return;
        pending = false;
        Status = "SVG 컴파일 중";
        work = Task.Run(Build);
    }

    private static string Build()
    {
        var start = new ProcessStartInfo("python")
        {
            WorkingDirectory = Project,
            Arguments = "Tools/Build-VectorUi.py",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        if (Directory.Exists(Dependencies))
            start.EnvironmentVariables["PYTHONPATH"] = Dependencies + Path.PathSeparator +
                Environment.GetEnvironmentVariable("PYTHONPATH");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Python을 실행하지 못했습니다.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill();
            throw new TimeoutException("SVG 변환 제한 시간 30초를 넘겼습니다.");
        }

        if (process.ExitCode != 0) throw new InvalidDataException(errors.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }
}

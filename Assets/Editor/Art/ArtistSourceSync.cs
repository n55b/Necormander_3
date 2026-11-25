using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.U2D.Aseprite;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static class ArtistSourceSync
{
    [Serializable] private sealed class Manifest { public int version; public Entry[] entries; }
    [Serializable] private sealed class Entry
    {
        public string source, asset, kind, sourceSha256, assetSha256;
        public int width, height, frames;
        public string[] requiredTags;
        public bool UsesTagContract => kind == "native" && requiredTags != null && requiredTags.Length > 0;
    }
    [Serializable] private sealed class Stamp { public string source, input, output; }
    [Serializable] private sealed class History { public Stamp[] stamps; }
    private sealed class Export { public Entry entry; public string input; public byte[] bytes; }

    private const string ExecutablePreference = "Necromander.Artist.Aseprite";
    private static readonly string Project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static readonly string Root = Path.Combine(Project, "ARTIST");
    private static readonly string Cache = Path.Combine(Project, "Library", "ArtistSync");
    private static readonly string ManifestPath = Path.Combine(Root, "manifest.json");
    private static readonly ConcurrentQueue<string> Changed = new();
    private static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Entry> Sources = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Stamp> Stamps = new(StringComparer.OrdinalIgnoreCase);
    private static FileSystemWatcher watcher;
    private static Task<Export> work;
    private static string workingSource;
    private static double readyAt;
    private static bool rescan;
    private static DateTime manifestTime;
    public static string Status { get; private set; } = "원본 목록 읽는 중";
    public static int SourceCount => Sources.Count;
    public static int PendingCount => Pending.Count + (work == null ? 0 : 1);

    static ArtistSourceSync()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += Start;
    }

    internal static void Start()
    {
        if (!File.Exists(ManifestPath)) return;
        try
        {
            ReadManifest();
            ReadHistory();
            Status = $"{Sources.Count}개 원본 감시 중";
            watcher = new FileSystemWatcher(Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += FileChanged;
            watcher.Created += FileChanged;
            watcher.Deleted += FileChanged;
            watcher.Renamed += (_, args) =>
            {
                Changed.Enqueue(args.OldFullPath);
                Changed.Enqueue(args.FullPath);
            };
            watcher.Error += (_, _) => Changed.Enqueue(ManifestPath);
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            CheckAll();
        }
        catch (Exception exception)
        {
            Stop();
            ReportError(exception);
        }
    }

    internal static void Stop()
    {
        EditorApplication.update -= Tick;
        watcher?.Dispose();
        watcher = null;
    }

    private static void FileChanged(object sender, FileSystemEventArgs args)
    {
        if (args.FullPath.EndsWith(".aseprite", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args.FullPath, ManifestPath, StringComparison.OrdinalIgnoreCase) ||
            VectorUiSourceSync.IsSource(args.FullPath))
            Changed.Enqueue(args.FullPath);
    }

    private static void ReadManifest()
    {
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        if (manifest?.entries == null || manifest.version != 1) throw new InvalidDataException("ARTIST 목록 형식이 맞지 않습니다.");
        var validated = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.entries)
        {
            string source = ResolveInside(Root, entry.source);
            ResolveInside(Application.dataPath, entry.asset.StartsWith("Assets/", StringComparison.Ordinal) ? entry.asset.Substring(7) : "");
            string extension = entry.kind == "native" ? ".aseprite" : ".png";
            if ((entry.kind != "native" && entry.kind != "png") ||
                !entry.asset.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
                !source.EndsWith(".aseprite", StringComparison.OrdinalIgnoreCase) ||
                !outputs.Add(entry.asset))
                throw new InvalidDataException("잘못되었거나 중복된 ARTIST 매핑: " + entry.asset);
            validated.Add(source, entry);
        }
        Sources.Clear();
        foreach (var item in validated) Sources.Add(item.Key, item.Value);
        manifestTime = File.GetLastWriteTimeUtc(ManifestPath);
    }

    private static string ResolveInside(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative))
            throw new InvalidDataException("ARTIST 매핑에는 저장소 상대 경로를 사용해야 합니다.");
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ARTIST 워크플로 폴더 밖의 경로입니다: " + relative);
        return full;
    }

    private static void ReadHistory()
    {
        string path = Path.Combine(Cache, "state.json");
        if (!File.Exists(path)) return;
        try
        {
            var history = JsonUtility.FromJson<History>(File.ReadAllText(path));
            foreach (var stamp in history?.stamps ?? Array.Empty<Stamp>()) Stamps[stamp.source] = stamp;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ARTIST] 동기화 기록을 다시 만듭니다. " + exception.Message);
        }
    }

    [MenuItem("Tools/아트/ARTIST 변경 확인")]
    public static void CheckAll()
    {
        if (watcher == null && !Application.isBatchMode)
        {
            Start();
            return;
        }
        rescan = true;
        VectorUiSourceSync.CheckAll();
        readyAt = EditorApplication.timeSinceStartup;
    }

    [Serializable] private class VectorLinks { public VectorLink[] entries; }
    [Serializable] private class VectorLink { public string svg, guid; public long fileId; }

    public static void RevealSelection()
    {
        UnityEngine.Object selected = Selection.activeObject;
        if (selected is GameObject go)
        {
            selected = go.GetComponent<UnityEngine.UI.Image>()?.overrideSprite;
            if (selected == null) selected = go.GetComponent<SpriteRenderer>()?.sprite;
        }
        if (selected is Sprite sprite && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string guid,out long id))
        {
            var vectorRoot = Path.Combine(Root, "UI", "Vector");
            string json = File.ReadAllText(Path.Combine(vectorRoot,"manifest.json"));
            var links = JsonUtility.FromJson<VectorLinks>("{\"entries\":" + json + "}");
            var match = Array.Find(links.entries, item => item.guid == guid && item.fileId == id);
            if (match != null)
            {
                EditorUtility.RevealInFinder(ResolveInside(vectorRoot, match.svg));
                return;
            }
        }
        var path = selected != null ? AssetDatabase.GetAssetPath(selected) : null;
        var source = Sources.FirstOrDefault(pair => pair.Value.asset == path).Key;
        if (source != null) EditorUtility.RevealInFinder(source);
        else Status = "선택한 오브젝트에서 연결된 그림 원본을 찾을 수 없습니다.";
    }

    public static void OpenFolder() => EditorUtility.RevealInFinder(Root);

    public static void SelectAseprite()
    {
        string path = EditorUtility.OpenFilePanel("Aseprite 실행 파일", "", "");
        if (string.IsNullOrEmpty(path)) return;
        EditorPrefs.SetString(ExecutablePreference, path);
        CheckAll();
    }

    private static void Tick()
    {
        while (Changed.TryDequeue(out string path))
        {
            if (Sources.ContainsKey(path)) Pending.Add(path);
            else if (string.Equals(path, ManifestPath, StringComparison.OrdinalIgnoreCase)) rescan = true;
            else if (VectorUiSourceSync.IsSource(path)) VectorUiSourceSync.Schedule();
            readyAt = EditorApplication.timeSinceStartup + .35;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < readyAt) return;
        VectorUiSourceSync.Tick();
        try
        {
            if (work != null)
            {
                if (!work.IsCompleted) return;
                var complete = work;
                work = null;
                var export = complete.GetAwaiter().GetResult();
                if (File.GetLastWriteTimeUtc(ManifestPath) != manifestTime)
                {
                    rescan = true;
                    return;
                }
                // A second save may have happened while Aseprite was exporting the first one.
                if (Digest(File.ReadAllBytes(workingSource)) == export.input) Apply(export);
                else Pending.Add(workingSource);
                return;
            }
            if (rescan)
            {
                rescan = false;
                if (File.GetLastWriteTimeUtc(ManifestPath) != manifestTime) ReadManifest();
                foreach (string source in Sources.Keys) Pending.Add(source);
            }
            double deadline = EditorApplication.timeSinceStartup + .004;
            while (Pending.Count > 0 && EditorApplication.timeSinceStartup < deadline)
            {
                string source = Pending.First();
                Pending.Remove(source);
                if (!Sources.TryGetValue(source, out var entry)) continue;
                if (!File.Exists(source))
                {
                    ReportError(new FileNotFoundException("등록 원본이 사라졌습니다. Unity 출력은 보존합니다. " + entry.source));
                    continue;
                }
                var bytes = File.ReadAllBytes(source);
                var input = Digest(bytes);
                bool known = Stamps.TryGetValue(entry.source, out var stamp);
                if (input == (known ? stamp.input : entry.sourceSha256))
                {
                    var target = Path.Combine(Project, entry.asset);
                    if (!File.Exists(target) || Digest(File.ReadAllBytes(target)) != (known ? stamp.output : entry.assetSha256))
                        ReportError(new IOException("원본과 연결된 Unity 출력을 확인하세요. " + entry.asset));
                    continue;
                }
                workingSource = source;
                string executable = entry.kind == "png" ? FindAseprite() : null;
                Status = "내보내는 중 · " + entry.source;
                work = Task.Run(() => Stage(entry, input, bytes, executable));
                break;
            }
        }
        catch (Exception exception) { ReportError(exception); }
    }

    private static Export Stage(Entry entry, string input, byte[] bytes, string executable)
    {
        if (bytes.Length < 128 || Read16(bytes, 4) != 0xa5e0 ||
            (!entry.UsesTagContract && Read16(bytes, 6) != entry.frames) || Read16(bytes, 8) != entry.width || Read16(bytes, 10) != entry.height)
            throw new InvalidDataException($"캔버스 또는 프레임 수가 바뀌었습니다 ({entry.width}×{entry.height}, {entry.frames}프레임). {entry.source}");
        if (entry.kind == "native") return new Export { entry = entry, input = input, bytes = bytes };
        if (string.IsNullOrEmpty(executable)) throw new FileNotFoundException("Aseprite를 찾을 수 없습니다. Sunyunie Panel의 아트 탭에서 실행 파일을 지정하세요.");

        Directory.CreateDirectory(Cache);
        string id = Guid.NewGuid().ToString("N");
        string from = Path.Combine(Cache, id + ".aseprite");
        string to = Path.Combine(Cache, id + ".png");
        try
        {
            File.WriteAllBytes(from, bytes);
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                Arguments = $"-b \"{from}\" --save-as \"{to}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) throw new InvalidOperationException("Aseprite를 실행하지 못했습니다.");
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new TimeoutException("Aseprite PNG 내보내기 시간 초과: " + entry.source);
            }
            if (process.ExitCode != 0 || !File.Exists(to)) throw new IOException("PNG 내보내기 실패: " + entry.source);
            return new Export { entry = entry, input = input, bytes = File.ReadAllBytes(to) };
        }
        finally
        {
            File.Delete(from);
            File.Delete(to);
        }
    }

    private static ushort Read16(byte[] bytes, int offset) => (ushort)(bytes[offset] | bytes[offset + 1] << 8);

    private static void Apply(Export export)
    {
        var entry = export.entry;
        string output = Path.Combine(Project, entry.asset);
        var previous = File.ReadAllBytes(output);
        var outputHash = Digest(export.bytes);
        string oldHash = Digest(previous);
        if (oldHash != outputHash)
        {
            var lastOutput = Stamps.TryGetValue(entry.source, out var stamp) ? stamp.output : entry.assetSha256;
            if (oldHash != lastOutput && oldHash != entry.assetSha256)
                throw new IOException("Unity 출력에도 별도 변경이 있습니다. 두 편집본을 확인하세요. " + entry.asset);

            var previousMeta = File.ReadAllBytes(output + ".meta");
            var before = AssetSignature(entry);
            try
            {
                ReplaceFile(output, export.bytes);
                AssetDatabase.ImportAsset(entry.asset, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (!before.SetEquals(AssetSignature(entry)))
                    throw new InvalidDataException("스프라이트 ID, 슬라이스, 피벗 또는 애니메이션 연결이 바뀌어 이전 출력을 복원했습니다. " + entry.source);
            }
            catch
            {
                ReplaceFile(output, previous);
                if (!File.ReadAllBytes(output + ".meta").SequenceEqual(previousMeta))
                    ReplaceFile(output + ".meta", previousMeta);
                AssetDatabase.ImportAsset(entry.asset, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                throw;
            }
        }
        Stamps[entry.source] = new Stamp { source = entry.source, input = export.input, output = outputHash };
        Directory.CreateDirectory(Cache);
        ReplaceFile(Path.Combine(Cache, "state.json"), System.Text.Encoding.UTF8.GetBytes(
            JsonUtility.ToJson(new History { stamps = Stamps.Values.ToArray() })));
        Status = "반영 완료 · " + entry.source;
        Debug.Log("[ARTIST] " + Status);
    }

    private static HashSet<string> AssetSignature(Entry entry)
    {
        var result = new HashSet<string>();
        string path = entry.asset;
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        if (entry.UsesTagContract)
        {
            // Named states are the runtime contract. Artists can redraw the silhouette,
            // retime cels or insert frames without preserving individual sprite IDs.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = prefab != null ? prefab.GetComponent<Animator>() : null;
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null || prefab.GetComponent<SpriteRenderer>() == null)
                throw new InvalidDataException("애니메이션 프리팹과 합쳐진 스프라이트가 필요합니다. " + path);
            foreach (var tag in entry.requiredTags)
            {
                var clip = controller.animationClips.FirstOrDefault(candidate => candidate.name == tag);
                if (clip == null || !AnimationUtility.GetObjectReferenceCurveBindings(clip).Any(binding =>
                    binding.type == typeof(SpriteRenderer) && binding.propertyName == "m_Sprite" &&
                    AnimationUtility.GetObjectReferenceCurve(clip, binding).Any(key => key.value is Sprite)))
                    throw new InvalidDataException("필수 애니메이션 태그 또는 그림이 없습니다. " + tag);
                result.Add(tag);
            }
            return result;
        }
        var importer = AssetImporter.GetAtPath(path) as AsepriteImporter;
        bool animated = importer != null && importer.importMode == FileImportModes.AnimatedSprite;
        if (animated)
            result.Add($"pivot:{importer.pivotSpace}:{importer.pivotAlignment}:{importer.customPivotPosition:R}");

        foreach (var asset in assets)
        {
            if (asset == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id)) continue;
            string identity = $"{guid}:{id}:{asset.GetType().Name}:{asset.name}";
            if (asset is Sprite sprite)
            {
                identity += $":{sprite.border}:{sprite.pixelsPerUnit}";
                // Animated cels are trimmed and repacked after painting. Their atlas rectangle
                // and trim-relative canvas pivot are generated values, not references to the art.
                if (!animated) identity += $":{sprite.rect}:{sprite.pivot}";
                else if (importer.pivotSpace == PivotSpaces.Local)
                    identity += $":{sprite.pivot.x / sprite.rect.width:F5}:{sprite.pivot.y / sprite.rect.height:F5}";
            }
            if (asset is AnimationClip clip)
                identity += $":{clip.length}:{string.Join(",", AnimationUtility.GetAnimationEvents(clip).Select(e => $"{e.time}:{e.functionName}"))}";
            result.Add(identity);
        }
        if (result.Count == 0) throw new InvalidDataException("Unity가 출력 에셋을 불러오지 못했습니다. " + path);
        return result;
    }

    private static void ReplaceFile(string path, byte[] bytes)
    {
        string temp = Path.Combine(Path.GetDirectoryName(path), "." + Path.GetFileName(path) + ".artist-tmp");
        File.WriteAllBytes(temp, bytes);
        try
        {
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string Digest(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    internal static string FindAseprite()
    {
        foreach (var candidate in new[]
        {
            Environment.GetEnvironmentVariable("ASEPRITE_PATH"),
            EditorPrefs.GetString(ExecutablePreference, ""),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Aseprite", "Aseprite.exe"),
            "/Applications/Aseprite.app/Contents/MacOS/aseprite"
        })
            if (File.Exists(candidate)) return candidate;
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            foreach (string name in new[] { "aseprite.exe", "aseprite" })
                if (!string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder.Trim('"'), name)))
                    return Path.Combine(folder.Trim('"'), name);
        return null;
    }

    private static void ReportError(Exception exception)
    {
        Status = exception.Message;
        Debug.LogWarning("[ARTIST] " + Status);
    }
}

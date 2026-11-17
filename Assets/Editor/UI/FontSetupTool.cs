using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 폰트 사용 현황 보고. (메뉴: Tools > Font Setup > 1. Report)
/// 현재 폰트 에셋 설정/폴백/사용처/머티리얼 프리셋을 콘솔과 Logs/FontSetup.log 에 기록한다. 아무것도 저장하지 않는다.
/// (ThinMulmaru 생성·전체 교체·가짜 볼드 정리·메뉴 씬 가독성 보정은 끝난 일회성 이행이라 메뉴에서 뺐다 —
///  다시 돌리면 씬·프리팹에서 조정한 굵기·글자색·머티리얼을 덮어썼다.)
/// </summary>
public static class FontSetupTool
{
    const string FontDir       = "Assets/TextMesh Pro/Fonts";

    // 예전 폰트. 남아 있는 사용처를 찾는 데만 쓴다.
    static readonly string[] OldFontPaths =
    {
        FontDir + "/Galmuri11 SDF.asset",
        FontDir + "/Galmuri11-Bold SDF.asset",
        "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset",
    };

    static readonly string[] SceneFolders      = { "Assets/Scenes" };
    static readonly string[] SkipFolders       = { "Assets/TextMesh Pro", "Assets/Plugins", "Assets/Samples" };

    const string LogFile   = "Logs/FontSetup.log";
    // 로그: <프로젝트>/Logs/FontSetup.log
 

    static void Log(string msg)
    {
        Debug.Log("[FontSetup] " + msg);
        var line = System.DateTime.Now.ToString("HH:mm:ss ") + msg;
        try { File.AppendAllText(LogFile, line + "\n"); } catch { }
         
         
    }

    static IEnumerable<TMP_FontAsset> OldFonts() =>
        OldFontPaths.Select(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>).Where(f => f != null);

    // ─── 1. Report ────────────────────────────────────────────────────
    [MenuItem("Tools/Font Setup/1. Report", priority = 1)]
    public static void Report()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("플레이 모드에서는 실행할 수 없습니다."); return; }
        // 모든 씬을 차례로 열어 본다. 저장 안 한 변경이 있으면 먼저 묻는다(취소하면 중단).
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var sb = new StringBuilder();

        sb.AppendLine("== Font assets ==");
        foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p);
            if (f == null) continue;
            var fb = f.fallbackFontAssetTable != null ? string.Join(", ", f.fallbackFontAssetTable.Where(x => x != null).Select(x => x.name)) : "";
            sb.AppendLine($"  {p}\n    mode={f.atlasPopulationMode} render={f.atlasRenderMode} point={f.faceInfo.pointSize} pad={f.atlasPadding} atlas={f.atlasWidth}x{f.atlasHeight} x{f.atlasTextures?.Length} multi={f.isMultiAtlasTexturesEnabled} chars={f.characterTable?.Count} src={(f.sourceFontFile ? f.sourceFontFile.name : "-")} weight={f.faceInfo.styleName}\n    fallback=[{fb}] boldSpacing={f.boldSpacing} boldStyle(normal/bold)={f.normalStyle}/{f.boldStyle}");
        }

        var settings = TMP_Settings.instance;
        sb.AppendLine("== TMP Settings ==");
        sb.AppendLine($"  default={(TMP_Settings.defaultFontAsset ? TMP_Settings.defaultFontAsset.name : "-")} fallbacks=[{string.Join(", ", (TMP_Settings.fallbackFontAssets ?? new List<TMP_FontAsset>()).Where(x => x != null).Select(x => x.name))}]");

        sb.AppendLine("== Materials using old atlases ==");
        var oldAtlases = new HashSet<Texture>(OldFonts().SelectMany(f => f.atlasTextures ?? new Texture2D[0]).Where(t => t != null));
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>())
                if (m.HasProperty("_MainTex") && oldAtlases.Contains(m.GetTexture("_MainTex")))
                    sb.AppendLine($"  {p} :: {m.name} ({m.shader.name})");
        }

        // 사용처 (씬·프리팹)
        var fontUse = new Dictionary<string, int>();
        var matUse  = new Dictionary<string, int>();
        int boldStyle = 0, bTags = 0, total = 0;
        var boldSamples = new List<string>();
        void Count(GameObject root, string ctx)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                total++;
                var fn = t.font ? t.font.name : "(none)";
                fontUse[fn] = fontUse.TryGetValue(fn, out var c) ? c + 1 : 1;
                var mn = t.fontSharedMaterial ? t.fontSharedMaterial.name : "(none)";
                matUse[mn] = matUse.TryGetValue(mn, out var c2) ? c2 + 1 : 1;
                if ((t.fontStyle & FontStyles.Bold) != 0) { boldStyle++; if (boldSamples.Count < 40) boldSamples.Add($"{ctx} :: {GetPath(t.transform)} font={fn}"); }
                if (t.text != null && t.text.Contains("<b>")) bTags++;
            }
        }
        ForEachPrefab((root, path) => { Count(root, path); return false; });
        ForEachScene((root, path) => { Count(root, path); return false; });

        sb.AppendLine($"== Usage (TMP_Text {total}) ==");
        foreach (var kv in fontUse.OrderByDescending(k => k.Value)) sb.AppendLine($"  font {kv.Key}: {kv.Value}");
        foreach (var kv in matUse.OrderByDescending(k => k.Value)) sb.AppendLine($"  mat  {kv.Key}: {kv.Value}");
        sb.AppendLine($"  fontStyle Bold: {boldStyle}, text with <b>: {bTags}");
        foreach (var s in boldSamples) sb.AppendLine("    bold " + s);

        sb.AppendLine("== Scripts mentioning fonts ==");
        foreach (var file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            var f = file.Replace('\\', '/');
            if (f.Contains("/Editor/")) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains("<b>") || lines[i].Contains("TMP_FontAsset") || lines[i].Contains("Galmuri") || lines[i].Contains("FontStyles.Bold") || lines[i].Contains(".font ="))
                    sb.AppendLine($"  {f}:{i + 1} {lines[i].Trim()}");
        }

        Log("Report\n" + sb);
    }

    // ─── helpers ─────────────────────────────────────────────────────
    /// <summary>visit 이 true 를 돌려주면 저장한다.</summary>
    static void ForEachPrefab(System.Func<GameObject, string, bool> visit)
    {
        var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !SkipFolders.Any(f => p.StartsWith(f)))
            .OrderBy(p => PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(p)) == PrefabAssetType.Variant ? 1 : 0)
            .ThenBy(p => p).ToList();
        foreach (var path in paths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            var type = PrefabUtility.GetPrefabAssetType(asset);
            if (type == PrefabAssetType.Model || type == PrefabAssetType.MissingAsset) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { if (visit(root, path)) PrefabUtility.SaveAsPrefabAsset(root, path); }
            catch (System.Exception e) { Log($"ERROR {path}: {e.Message}"); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    static void ForEachScene(System.Func<GameObject, string, bool> visit)
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var path in AssetDatabase.FindAssets("t:Scene", SceneFolders).Select(AssetDatabase.GUIDToAssetPath))
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool dirty = false;
                foreach (var r in scene.GetRootGameObjects()) dirty |= visit(r, path);
                if (dirty) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
        }
        finally
        {
            if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    static string GetPath(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

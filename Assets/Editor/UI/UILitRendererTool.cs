using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 레이어/UI Sorting Layer 로 올라간 렌더러 중 '라이팅을 받는(Lit)' 머티리얼을 쓰는 것을 찾고 고친다.
///
/// 2D Renderer 에서 Lit 스프라이트는 Light2D 의 Target Sorting Layers 에 포함된 레이어에서만 빛을 받는다.
/// 새로 만든 "UI" Sorting Layer 는 어떤 라이트에도 포함되지 않으므로 Lit 머티리얼은 새까맣게 나온다.
/// UI 는 라이팅을 받을 필요가 없으므로 Unlit 으로 바꾸는 것이 정답이다.
///
/// 메뉴: Tools > UI Canvas Setup > 4. Scan / 5. Fix
/// </summary>
public static class UILitRendererTool
{
    const int    UILayer          = 5;
    const string UISortingLayer   = "UI";
    const string LitDefaultPath   = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";
    const string UnlitDefaultPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
    // 런타임에 코드로 만드는 스프라이트(F 아이콘 등)가 Resources 로 불러 쓰는 Unlit 머티리얼
    public const string RuntimeUnlitPath = "Assets/Resources/Materials/UI/UISpriteUnlit.mat";
    const string LogFile   = "Logs/UILitRenderer.log";
    // 로그: <프로젝트>/Logs/UILitRenderer.log

    static readonly string[] SceneFolders      = { "Assets/Scenes" };
    static readonly string[] SkipPrefabFolders = { "Assets/TextMesh Pro", "Assets/Plugins", "Assets/Samples" };

    [MenuItem("Tools/UI Canvas Setup/4. Scan Lit Renderers On UI", priority = 20)]
    public static void Scan() => Run(fix: false);

    [MenuItem("Tools/UI Canvas Setup/5. Fix Lit Renderers On UI", priority = 21)]
    public static void Fix() => Run(fix: true);

    static bool IsLit(Material m)
    {
        if (m == null || m.shader == null) return false;
        var n = m.shader.name;
        return n.Contains("Lit") && !n.Contains("Unlit");
    }

    static void Run(bool fix)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("플레이 모드에서는 실행할 수 없습니다."); return; }
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty) { Log($"저장되지 않은 씬({EditorSceneManager.GetSceneAt(i).name})이 있어 중단합니다."); return; }

        var litDefault   = AssetDatabase.LoadAssetAtPath<Material>(LitDefaultPath);
        var unlitDefault = AssetDatabase.LoadAssetAtPath<Material>(UnlitDefaultPath);
        if (fix) EnsureRuntimeUnlitMaterial();

        var sb = new StringBuilder();
        int found = 0, fixedCount = 0;

        // 프리팹
        var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !SkipPrefabFolders.Any(f => p.StartsWith(f)))
            .OrderBy(p => PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(p)) == PrefabAssetType.Variant ? 1 : 0)
            .ThenBy(p => p).ToList();

        foreach (var path in prefabPaths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            var type = PrefabUtility.GetPrefabAssetType(asset);
            if (type == PrefabAssetType.Model || type == PrefabAssetType.MissingAsset) continue;
            if (!HasCandidate(asset)) continue;

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int f = Process(root, path, fix, litDefault, unlitDefault, sb, ref found);
                if (f > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); fixedCount += f; }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // 씬
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var path in AssetDatabase.FindAssets("t:Scene", SceneFolders).Select(AssetDatabase.GUIDToAssetPath))
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int f = 0;
                foreach (var r in scene.GetRootGameObjects())
                    f += Process(r, path, fix, litDefault, unlitDefault, sb, ref found);

                // 라이트가 UI Sorting Layer 를 비추는지 참고용으로 기록
                foreach (var r in scene.GetRootGameObjects())
                    foreach (var c in r.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || c.GetType().Name != "Light2D") continue;
                        var so = new SerializedObject(c);
                        var arr = so.FindProperty("m_ApplyToSortingLayers");
                        if (arr == null) continue;
                        var names = new List<string>();
                        for (int i = 0; i < arr.arraySize; i++) names.Add(SortingLayer.IDToName(arr.GetArrayElementAtIndex(i).intValue));
                        sb.AppendLine($"  [Light2D] {path} :: {GetPath(c.transform)} → {string.Join(", ", names)}");
                    }

                if (f > 0 && fix) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); fixedCount += f; }
            }
        }
        finally
        {
            if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        AssetDatabase.SaveAssets();
        Log($"{(fix ? "수정" : "검사")} 완료: Lit 렌더러 {found}개 발견, {fixedCount}개 수정\n{sb}");
    }

    static bool HasCandidate(GameObject go) =>
        go.GetComponentsInChildren<Renderer>(true).Any(r => OnUI(r.gameObject, r.sortingLayerName) && r.sharedMaterials.Any(IsLit)) ||
        go.GetComponentsInChildren<Graphic>(true).Any(g => IsLit(SerializedMaterial(g)));

    static bool OnUI(GameObject go, string sortingLayer) => go.layer == UILayer || sortingLayer == UISortingLayer;

    static Material SerializedMaterial(Graphic g) =>
        new SerializedObject(g).FindProperty("m_Material")?.objectReferenceValue as Material;

    static int Process(GameObject root, string ctx, bool fix, Material litDefault, Material unlitDefault, StringBuilder sb, ref int found)
    {
        int fixedCount = 0;

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!OnUI(r.gameObject, r.sortingLayerName)) continue;
            var mats = r.sharedMaterials;
            bool any = false, changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!IsLit(mats[i])) continue;
                any = true;
                string action = "확인 필요(커스텀 Lit 머티리얼 — 자동 수정 안 함)";
                if (mats[i] == litDefault && unlitDefault != null && r is SpriteRenderer)
                {
                    action = "→ Sprite-Unlit-Default";
                    if (fix) { mats[i] = unlitDefault; changed = true; }
                }
                sb.AppendLine($"  [{r.GetType().Name}] {ctx} :: {GetPath(r.transform)}  mat={mats[i].name}({mats[i].shader.name}) sort={r.sortingLayerName}  {action}");
            }
            if (any) found++;
            if (changed) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); fixedCount++; }
        }

        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
        {
            var m = SerializedMaterial(g);
            if (!IsLit(m)) continue;
            found++;
            // UI Graphic 에 스프라이트 Lit 머티리얼이 들어간 경우는 비우면 기본 UI 머티리얼(Unlit)로 돌아간다.
            bool auto = m == litDefault;
            sb.AppendLine($"  [{g.GetType().Name}] {ctx} :: {GetPath(g.transform)}  mat={m.name}({m.shader.name})  {(auto ? "→ 기본 UI 머티리얼" : "확인 필요")}");
            if (fix && auto)
            {
                var so = new SerializedObject(g);
                so.FindProperty("m_Material").objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(g);
                fixedCount++;
            }
        }
        return fixedCount;
    }

    /// <summary>런타임 코드가 Resources.Load 로 쓰는 Unlit 스프라이트 머티리얼을 만든다.</summary>
    [MenuItem("Tools/UI Canvas Setup/6. Create Runtime Unlit Material", priority = 22)]
    public static void EnsureRuntimeUnlitMaterial()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(RuntimeUnlitPath) != null) return;
        var src = AssetDatabase.LoadAssetAtPath<Material>(UnlitDefaultPath);
        if (src == null) { Log("Sprite-Unlit-Default 를 찾지 못했습니다."); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(RuntimeUnlitPath));
        var mat = new Material(src) { name = "UISpriteUnlit" };
        AssetDatabase.CreateAsset(mat, RuntimeUnlitPath);
        AssetDatabase.SaveAssets();
        Log("런타임용 Unlit 머티리얼 생성: " + RuntimeUnlitPath);
    }

    static string GetPath(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    static void Log(string msg)
    {
        Debug.Log("[UILitRenderer] " + msg);
        var line = System.DateTime.Now.ToString("HH:mm:ss ") + msg;
        try { File.AppendAllText(LogFile, line + "\n"); } catch { }
         
    }


    [MenuItem("Tools/UI Canvas Setup/7. Scan Runtime-Created Renderers", priority = 23)]
    public static void ScanRuntimeRenderers()
    {
        string[] patterns =
        {
            "AddComponent<SpriteRenderer>", "AddComponent<LineRenderer>", "AddComponent<TrailRenderer>",
            "AddComponent<ParticleSystem>", "AddComponent<MeshRenderer>", "sortingLayerName", "sortingLayerID",
            "NameToLayer(\"UI\")", "layer = 5", ".layer = UILayer",
        };
        var sb = new StringBuilder();
        int hits = 0;
        foreach (var file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            var f = file.Replace('\\', '/');
            if (f.Contains("/Editor/")) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var hit = patterns.FirstOrDefault(p => lines[i].Contains(p));
                if (hit == null) continue;
                sb.AppendLine($"  {f}:{i + 1}  [{hit}] {lines[i].Trim()}");
                hits++;
            }
        }
        Log($"런타임 렌더러 검사: {hits}건\n{sb}");
    }
}

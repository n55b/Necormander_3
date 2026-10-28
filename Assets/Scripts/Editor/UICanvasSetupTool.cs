using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// UI 캔버스 일괄 정리 도구. (메뉴: Tools > UI Canvas Setup)
///  1) Sorting Layer "UI" 를 만들고 맨 아래(= 가장 위에 그려짐)로 옮긴다.
///  2) 씬 + 프리팹의 캔버스를 정리한다.
///     - Screen Space - Overlay → Screen Space - Camera (Main Camera 에서 포스트 프로세스를 받도록)
///     - 루트 캔버스(월드 스페이스 포함)의 Sorting Layer = UI, 캔버스와 모든 자식의 Layer = UI(5)
///       → PixelArtUpscaleRig 에서 저해상도 RT 가 아니라 Main Camera 가 화면 해상도로 그린다(글자 선명).
///     - Screen Space - Camera 캔버스에는 CanvasCameraBinder 를 붙여 런타임에 Camera.main 을 연결한다.
///  3) 전환 시 확인이 필요한 스크립트(오버레이 좌표 가정 등)를 나열한다.
/// 결과는 콘솔과 Logs/UICanvasSetup.log 에 남는다.
/// </summary>
public static class UICanvasSetupTool
{
    const string SortingLayerName = "UI";
    const int    UILayer          = 5;
    const float  PlaneDistance    = 1f;
    const string LogTag           = "[UICanvasSetup] ";
    const string LogFile          = "Logs/UICanvasSetup.log"; // 프로젝트 폴더/Logs (Assets 밖)
    // 로그 파일 위치: <프로젝트>/Logs/UICanvasSetup.log
     

    static void Write(string line)
    {
        try { File.AppendAllText(LogFile, line + "\n"); } catch { }
         
    }

    static readonly string[] ScanFolders       = { "Assets/Scripts" };
    static readonly string[] SceneFolders      = { "Assets/Scenes" };
    static readonly string[] SkipPrefabFolders = { "Assets/TextMesh Pro", "Assets/Plugins", "Assets/Samples" };

    static void Log(string msg)
    {
        Debug.Log(LogTag + msg);
        Write(System.DateTime.Now.ToString("HH:mm:ss ") + msg);
    }

    static void LogError(string msg)
    {
        Debug.LogError(LogTag + msg);
        Write(System.DateTime.Now.ToString("HH:mm:ss ") + "ERROR " + msg);
    }

    // ─── 1. Sorting Layer ─────────────────────────────────────────────
    [MenuItem("Tools/UI Canvas Setup/1. UI Sorting Layer To Top", priority = 1)]
    public static void EnsureUISortingLayerOnTop()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("m_SortingLayers");

        Log("Sorting Layers (before): " + string.Join(" < ", SortingLayer.layers.Select(l => l.name)));

        int idx = -1;
        var ids = new HashSet<long>();
        for (int i = 0; i < layers.arraySize; i++)
        {
            var e = layers.GetArrayElementAtIndex(i);
            ids.Add(e.FindPropertyRelative("uniqueID").longValue);
            if (string.Equals(e.FindPropertyRelative("name").stringValue, SortingLayerName, System.StringComparison.OrdinalIgnoreCase))
                idx = i;
        }

        if (idx < 0)
        {
            layers.InsertArrayElementAtIndex(layers.arraySize);
            var e = layers.GetArrayElementAtIndex(layers.arraySize - 1);
            e.FindPropertyRelative("name").stringValue = SortingLayerName;
            long id;
            var rnd = new System.Random();
            do { id = rnd.Next(1, int.MaxValue); } while (ids.Contains(id));
            e.FindPropertyRelative("uniqueID").longValue = id;
            var locked = e.FindPropertyRelative("locked");
            if (locked != null) locked.boolValue = false;
            Log($"Sorting Layer '{SortingLayerName}' 생성 (맨 위)");
        }
        else
        {
            var e = layers.GetArrayElementAtIndex(idx);
            e.FindPropertyRelative("name").stringValue = SortingLayerName; // 대소문자 통일
            if (idx != layers.arraySize - 1)
            {
                layers.MoveArrayElement(idx, layers.arraySize - 1);
                Log($"Sorting Layer '{SortingLayerName}' 를 맨 위로 이동 ({idx} → {layers.arraySize - 1})");
            }
            else Log($"Sorting Layer '{SortingLayerName}' 는 이미 맨 위");
        }

        tagManager.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Log("Sorting Layers (after): " + string.Join(" < ", SortingLayer.layers.Select(l => l.name)));
    }

    // ─── 2. 캔버스 정리 ───────────────────────────────────────────────
    [MenuItem("Tools/UI Canvas Setup/2. Convert Canvases (Scenes + Prefabs)", priority = 2)]
    public static void ConvertAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            LogError("플레이 모드에서는 실행할 수 없습니다.");
            return;
        }
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var s = EditorSceneManager.GetSceneAt(i);
            if (s.isDirty)
            {
                LogError($"저장되지 않은 씬({s.name})이 있어 중단합니다. 저장 후 다시 실행하세요.");
                return;
            }
        }

        EnsureUISortingLayerOnTop();

        var log = new StringBuilder();
        int changed = 0;

        // 프리팹 먼저 (씬의 프리팹 인스턴스에 불필요한 오버라이드가 생기지 않도록). 일반 → 변형 순서.
        var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !SkipPrefabFolders.Any(f => p.StartsWith(f)))
            .OrderBy(p => PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(p)) == PrefabAssetType.Variant ? 1 : 0)
            .ThenBy(p => p)
            .ToList();

        foreach (var path in prefabPaths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            var type = PrefabUtility.GetPrefabAssetType(asset);
            if (type == PrefabAssetType.Model || type == PrefabAssetType.MissingAsset) continue;
            if (asset.GetComponentsInChildren<Canvas>(true).Length == 0) continue;

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int n = ProcessHierarchy(root, null, log, path);
                if (n > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed += n;
                }
            }
            catch (System.Exception ex) { LogError($"{path}: {ex.Message}"); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // 씬
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var scenePaths = AssetDatabase.FindAssets("t:Scene", SceneFolders).Select(AssetDatabase.GUIDToAssetPath).ToList();
        try
        {
            foreach (var path in scenePaths)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Camera sceneCam = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<Camera>(true))
                    .FirstOrDefault(c => c.CompareTag("MainCamera"));

                int n = 0;
                foreach (var r in scene.GetRootGameObjects())
                    n += ProcessHierarchy(r, sceneCam, log, path);

                if (n > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    changed += n;
                }
            }
        }
        catch (System.Exception ex) { LogError("씬 처리 중 오류: " + ex); }
        finally
        {
            if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        AssetDatabase.SaveAssets();
        Log($"완료: 캔버스 {changed}개 변경\n" + log);
    }

    /// <returns>변경한 캔버스 수</returns>
    /// <returns>변경한 캔버스 수</returns>
    /// Canvas.renderMode 게터는 카메라가 비어 있으면 Camera 모드여도 Overlay 를 돌려주므로 직렬화 값을 직접 다룬다.
    static int ProcessHierarchy(GameObject root, Camera sceneCam, StringBuilder log, string context)
    {
        int uiSortId = SortingLayer.NameToID(SortingLayerName);
        int count = 0;
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            var parent = canvas.transform.parent;
            bool isRoot = parent == null || parent.GetComponentInParent<Canvas>(true) == null;
            var go = canvas.gameObject;
            var before = Describe(canvas);
            bool dirty = false;

            var so = new SerializedObject(canvas);
            var mode    = so.FindProperty("m_RenderMode");     // 0 Overlay, 1 Camera, 2 World
            var camProp = so.FindProperty("m_Camera");
            var plane   = so.FindProperty("m_PlaneDistance");
            var sortId  = so.FindProperty("m_SortingLayerID");
            var over    = so.FindProperty("m_OverrideSorting");

            if (isRoot)
            {
                if (mode.intValue == 0)
                {
                    mode.intValue = 1;
                    plane.floatValue = PlaneDistance;
                    dirty = true;
                }
                if (mode.intValue == 1)
                {
                    if (sceneCam != null && camProp.objectReferenceValue == null) { camProp.objectReferenceValue = sceneCam; dirty = true; }
                    if (go.GetComponent<CanvasCameraBinder>() == null) { go.AddComponent<CanvasCameraBinder>(); dirty = true; }
                }
                if (sortId.intValue != uiSortId) { sortId.intValue = uiSortId; dirty = true; }
            }
            else if (over.boolValue && sortId.intValue != uiSortId)
            {
                sortId.intValue = uiSortId;
                dirty = true;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            if (isRoot && SetLayerRecursive(go)) dirty = true;

            if (dirty)
            {
                EditorUtility.SetDirty(canvas);
                count++;
                log.AppendLine($"  {context} :: {GetPath(canvas.transform)}  [{before}] -> [{Describe(canvas)}]");
            }
        }
        return count;
    }

    static bool SetLayerRecursive(GameObject go)
    {
        bool changed = false;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            if (t.gameObject.layer == UILayer) continue;
            t.gameObject.layer = UILayer;
            EditorUtility.SetDirty(t.gameObject);
            changed = true;
        }
        return changed;
    }

    static string Describe(Canvas c)
    {
        var so = new SerializedObject(c);
        var mode = (RenderMode)so.FindProperty("m_RenderMode").intValue;
        var cam  = so.FindProperty("m_Camera").objectReferenceValue;
        var sort = SortingLayer.IDToName(so.FindProperty("m_SortingLayerID").intValue);
        return $"{mode}{(mode == RenderMode.ScreenSpaceCamera ? (cam != null ? "(cam)" : "(binder)") : "")}, sort={(string.IsNullOrEmpty(sort) ? "?" : sort)}/{c.sortingOrder}, layer={LayerMask.LayerToName(c.gameObject.layer)}";
    }

    static string GetPath(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    // ─── 3. 영향 스크립트 검사 ─────────────────────────────────────────
    static readonly string[] ScanPatterns =
    {
        "RenderMode.ScreenSpaceOverlay",
        "ScreenPointToLocalPointInRectangle",
        "ScreenPointToWorldPointInRectangle",
        "WorldToScreenPoint",
        "mousePosition",
        "renderMode",
    };

    [MenuItem("Tools/UI Canvas Setup/3. Scan Affected Scripts", priority = 3)]
    public static void ScanScripts()
    {
        var sb = new StringBuilder();
        int hits = 0;
        foreach (var folder in ScanFolders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                var f = file.Replace('\\', '/');
                if (f.Contains("/Editor/")) continue;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var hit = ScanPatterns.FirstOrDefault(p => lines[i].Contains(p));
                    if (hit == null) continue;
                    sb.AppendLine($"  {f}:{i + 1}  [{hit}] {lines[i].Trim()}");
                    hits++;
                }
            }
        }
        Log($"스크립트 검사: {hits}건\n" + sb);
    }
}

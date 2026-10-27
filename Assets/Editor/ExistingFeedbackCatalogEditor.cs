using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>기존 설정은 원본에 둔다. Catalog에는 분류·사용 여부·편집할 원본 연결만 저장한다.</summary>
public sealed class ExistingFeedbackCatalogEditor
{
    private readonly Dictionary<int, int> choices = new Dictionary<int, int>();
    private readonly Dictionary<Object, Editor> sourceEditors = new Dictionary<Object, Editor>();

    public void Dispose()
    {
        foreach (var editor in sourceEditors.Values) if (editor != null) Object.DestroyImmediate(editor);
        sourceEditors.Clear();
    }

    public void Draw(SerializedObject serialized, string search)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("기존 연출 — 원본 값 그대로 조절", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("대상을 고르면 기존 프리팹·재질의 실제 값을 여기서 편집합니다. 다른 개체의 설정은 덮어쓰지 않습니다. 에셋 수정은 저장되며, 이미 생성된 개체에는 다시 생성할 때 반영됩니다. 사용 체크는 실행 중인 연출에도 적용됩니다.", MessageType.None);
        if (GUILayout.Button("기존 연출 연결 목록 새로고침 (수치 유지)"))
        {
            serialized.ApplyModifiedProperties();
            foreach (var obj in serialized.targetObjects) Rebuild((FeedbackCatalog)obj);
            serialized.Update();
        }
        var entries = serialized.FindProperty("existingEffects");
        for (int i = 0; i < entries.arraySize; i++)
        {
            var entry = entries.GetArrayElementAtIndex(i);
            var cue = (ExistingFeedbackCue)entry.FindPropertyRelative("cue").intValue;
            string title = entry.FindPropertyRelative("title").stringValue;
            if (!string.IsNullOrWhiteSpace(search) && title.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("enabled"), new GUIContent(title));
                if (GUILayout.Button(entry.isExpanded ? "접기" : "조절", GUILayout.Width(44))) entry.isExpanded = !entry.isExpanded;
            }
            if (!entry.isExpanded) continue;
            EditorGUILayout.HelpBox(entry.FindPropertyRelative("description").stringValue, MessageType.None);
            var sources = entry.FindPropertyRelative("sources");
            var candidates = new List<Object>();
            for (int j = 0; j < sources.arraySize; j++)
            {
                var source = sources.GetArrayElementAtIndex(j).objectReferenceValue;
                if (source != null) candidates.Add(source);
            }
            if (candidates.Count == 0) { EditorGUILayout.LabelField("연결된 원본 없음 — 새로고침으로 다시 찾습니다."); continue; }
            Object selected = Select((int)cue, candidates);
            if (selected is SceneAsset sceneAsset)
            {
                string path = AssetDatabase.GetAssetPath(sceneAsset);
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.isLoaded)
                {
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                        if (GUILayout.Button("이 씬 설정 불러오기 (현재 씬 유지)")) EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    EditorGUILayout.HelpBox("씬에 직접 배치된 카메라/히트스톱 설정입니다. 씬을 추가로 열면 아래에서 수정할 수 있습니다.", MessageType.None);
                    continue;
                }
                var components = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Component>(true))
                    .Where(c => c != null && Matches(c, cue)).Cast<Object>().ToList();
                if (components.Count == 0) continue;
                selected = Select(100 + (int)cue, components);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    if (GUILayout.Button("이 씬 설정 저장")) EditorSceneManager.SaveScene(scene);
            }
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("원본", selected, typeof(Object), true);
            DrawSource(selected, cue);
        }
    }

    private Object Select(int key, List<Object> sources)
    {
        choices.TryGetValue(key, out int choice);
        choice = EditorGUILayout.Popup("조절 대상", Mathf.Clamp(choice, 0, sources.Count - 1), sources.Select(Label).ToArray());
        choices[key] = choice;
        return sources[choice];
    }
    private static string Label(Object source)
    {
        string path = AssetDatabase.GetAssetPath(source);
        string name = source.name;
        if (source is Component component)
        {
            name = component.transform.name;
            for (var p = component.transform.parent; p != null; p = p.parent) name = p.name + " > " + name;
        }
        return $"{name} ({source.GetType().Name}) — {path}";
    }

    private void DrawSource(Object source, ExistingFeedbackCue cue)
    {
        string[] fields = null;
        if (source is CharacterVisualFeedback)
            fields = cue == ExistingFeedbackCue.HitStop ? new[] { "playerHitStopDuration" }
                : new[] { "hitFlashStrength", "hitFlashDuration", "tintFlashDuration", "playerHurtVignetteDuration" };
        else if (source is SkillSO)
            fields = cue == ExistingFeedbackCue.HitStop ? new[] { "hitStopDuration" } : new[] { "shakeForce" };
        else if (source is AutoDestroyEffect) fields = new[] { "playbackSpeed" };
        if (fields != null)
        {
            var data = new SerializedObject(source); data.Update();
            foreach (var name in fields)
            {
                var property = data.FindProperty(name);
                if (property == null) continue;
                var field = DesignerInspectorView.ResolveField(source, name);
                EditorGUILayout.PropertyField(property, DesignerInspectorLabels.Content(field, property.displayName, property.tooltip), true);
            }
            data.ApplyModifiedProperties();
            return;
        }
        if (!sourceEditors.TryGetValue(source, out var sourceEditor) || sourceEditor == null)
            sourceEditors[source] = sourceEditor = Editor.CreateEditor(source);
        sourceEditor.OnInspectorGUI();
    }

    public static bool Matches(Object obj, ExistingFeedbackCue cue)
    {
        switch (cue)
        {
            case ExistingFeedbackCue.Slash: return obj is ImpactFeedback;
            case ExistingFeedbackCue.Squash: return obj is VisualSquashStretch;
            case ExistingFeedbackCue.Flash: return obj is CharacterVisualFeedback;
            case ExistingFeedbackCue.Afterimage: return obj is DashAfterimage;
            case ExistingFeedbackCue.Silhouette: return obj is PlayerOcclusionSilhouette;
            case ExistingFeedbackCue.Fade: return obj is Fader;
            case ExistingFeedbackCue.UIShake: return obj is UIShaker;
            case ExistingFeedbackCue.CameraShake: return obj is CameraManager || obj is CameraShaker || obj is SkillSO
                || obj is Unity.Cinemachine.CinemachineImpulseSource || obj is Unity.Cinemachine.CinemachineImpulseListener;
            case ExistingFeedbackCue.HitStop: return obj is HitStopManager || obj is CharacterVisualFeedback || obj is SkillSO;
            default: return false;
        }
    }

    [MenuItem("Tools/연출/기존 연출을 Catalog에 연결 (값 유지)")]
    public static void Setup()
    {
        WireSmoke("Assets/Prefabs/Effect/Walk_Smog_Effect.prefab", ExistingFeedbackCue.WalkSmoke);
        WireSmoke("Assets/Prefabs/Effect/DashSmog/DashSmogEffect.prefab", ExistingFeedbackCue.DashSmoke);
        Rebuild(AssetDatabase.LoadAssetAtPath<FeedbackCatalog>(FeedbackSetup.CatalogPath));
    }
    private static void WireSmoke(string path, ExistingFeedbackCue cue)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var data = new SerializedObject(root.GetComponent<AutoDestroyEffect>());
            data.FindProperty("catalogEffect").intValue = (int)cue;
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void Rebuild(FeedbackCatalog catalog)
    {
        if (catalog == null) throw new InvalidOperationException("FeedbackCatalog 에셋을 먼저 연결하세요.");
        var cues = Enum.GetValues(typeof(ExistingFeedbackCue)).Cast<ExistingFeedbackCue>().Where(c => c != ExistingFeedbackCue.None).ToArray();
        var sources = cues.ToDictionary(c => c, c => new List<Object>());
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null) foreach (var cue in cues) if (Matches(component, cue)) sources[cue].Add(component);
            if (path == "Assets/Prefabs/Effect/Walk_Smog_Effect.prefab" || path == "Assets/Prefabs/Effect/DashSmog/DashSmogEffect.prefab")
            {
                var cue = path.Contains("Walk_") ? ExistingFeedbackCue.WalkSmoke : ExistingFeedbackCue.DashSmoke;
                sources[cue].Add(prefab.GetComponent<AutoDestroyEffect>());
                sources[cue].AddRange(prefab.GetComponentsInChildren<SpriteRenderer>(true));
            }
            if (prefab.GetComponent<ImpactFeedback>() != null)
                sources[ExistingFeedbackCue.Slash].AddRange(prefab.GetComponentsInChildren<LineRenderer>(true));
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Material/Feedback" }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material.shader.name.Contains("Pixel")) sources[ExistingFeedbackCue.Dither].Add(material);
            if (material.shader.name.Contains("Additive")) sources[ExistingFeedbackCue.Slash].Add(material);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:SkillSO"))
        {
            var skill = AssetDatabase.LoadAssetAtPath<SkillSO>(AssetDatabase.GUIDToAssetPath(guid));
            sources[ExistingFeedbackCue.CameraShake].Add(skill);
            sources[ExistingFeedbackCue.HitStop].Add(skill);
        }
        // 씬 개체를 에셋에 참조할 수 없으므로 SceneAsset을 등록하고 편집 시 실제 개체를 찾는다.
        var sceneTypes = new[] { typeof(CameraManager), typeof(CameraShaker), typeof(HitStopManager), typeof(Fader), typeof(UIShaker) };
        foreach (var type in sceneTypes)
        {
            string scriptGuid = AssetDatabase.FindAssets(type.Name + " t:MonoScript").FirstOrDefault(g => AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g)).GetClass() == type);
            if (scriptGuid == null) continue;
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!File.ReadAllText(path).Contains(scriptGuid)) continue;
                var cue = type == typeof(HitStopManager) ? ExistingFeedbackCue.HitStop : type == typeof(Fader) ? ExistingFeedbackCue.Fade
                    : type == typeof(UIShaker) ? ExistingFeedbackCue.UIShake : ExistingFeedbackCue.CameraShake;
                sources[cue].Add(AssetDatabase.LoadAssetAtPath<SceneAsset>(path));
            }
        }
        Undo.RecordObject(catalog, "기존 연출 연결 새로고침");
        catalog.existingEffects = cues.Select(cue =>
        {
            var entry = catalog.existingEffects?.FirstOrDefault(e => e != null && e.cue == cue) ?? new ExistingFeedbackEntry { cue = cue };
            entry.title = typeof(ExistingFeedbackCue).GetField(cue.ToString()).GetCustomAttributes(typeof(InspectorNameAttribute), false).Cast<InspectorNameAttribute>().First().displayName;
            entry.description = cue == ExistingFeedbackCue.Fade ? "끄면 부드러운 전환만 생략합니다. 로딩 완료 대기·암전·입력 차단은 유지합니다."
                : cue == ExistingFeedbackCue.Dither ? "끄면 도트 무늬 대신 원래 알파로 표시합니다. 완전 암전과 투명도는 유지합니다."
                : "대상별 원본 수치와 곡선을 직접 수정합니다. 체크를 끄면 이 종류의 기존 연출을 중단합니다.";
            entry.sources = sources[cue].Where(s => s != null).Distinct().OrderBy(Label).ToArray();
            return entry;
        }).ToArray();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
        Debug.Log($"[ExistingFeedbackCatalog] {catalog.existingEffects.Length} groups, {catalog.existingEffects.Sum(e => e.sources.Length)} source links. Original values retained.");
    }

    [InitializeOnLoadMethod]
    private static void ClearPreviewGlobals()
    {
        EditorApplication.playModeStateChanged += state =>
        { if (state == PlayModeStateChange.EnteredEditMode) Shader.SetGlobalFloat("_FeedbackDitherDisabled", 0f); };
    }
}

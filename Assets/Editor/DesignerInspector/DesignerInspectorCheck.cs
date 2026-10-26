using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>실제 프리팹/SO의 표시 항목과 Undo·직렬화를 확인한다. 게임 에셋은 저장하지 않는다.</summary>
public static class DesignerInspectorCheck
{
    [MenuItem("Tools/기획/인스펙터와 연출 함께 검증")]
    public static void RunWithFeedback()
    {
        Run();
        FeedbackCheck.Run();
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            DesignerInspectorGuiCheck.Run();
    }
    [MenuItem("Tools/기획/한글 인스펙터 검증")]
    public static void Run()
    {
        DesignerInspectionInventory.Export();
        var fields = new SortedSet<string>();
        var types = new HashSet<Type>();
        var unknown = new SortedSet<string>();
        var missing = new SortedSet<string>();
        int objects = 0;
        var report = new System.Text.StringBuilder();
        var inventory = JsonUtility.FromJson<DesignerInspectionInventory.Report>(File.ReadAllText("Logs/Designer-fields.json"));
        foreach (var field in inventory.fields)
            foreach (string word in DesignerInspectorLabels.UnknownWords(field.field)) unknown.Add(word);
        void Inspect(Object target)
        {
            if (target == null || !DesignerInspectorView.IsProjectType(target.GetType())) return;
            objects++; types.Add(target.GetType());
            string before = EditorJsonUtility.ToJson(target);
            using (var serialized = new SerializedObject(target))
            {
                var p = serialized.GetIterator();
                while (p.NextVisible(true))
                {
                    if (p.name == "m_Script" || p.name.StartsWith("data[")) continue;
                    var f = DesignerInspectorView.ResolveField(target, p.propertyPath);
                    if (f == null || !DesignerInspectorView.IsProjectType(f.DeclaringType)) continue;
                    foreach (string word in DesignerInspectorLabels.UnknownWords(f.Name)) unknown.Add(word);
                    var label = DesignerInspectorLabels.Content(f, p.displayName, p.tooltip);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(label.text, "[가-힣]|^(ID|UI|X|Y|Z|A|B)$")) missing.Add(f.DeclaringType.Name + "." + f.Name);
                    Require(label.tooltip.Contains("코드:"), "설명 경로 없음: " + f.Name);
                    string key = f.DeclaringType.Name + "." + f.Name;
                    if (fields.Add(key)) report.AppendLine(key + " → " + label.text + " | " + label.tooltip.Replace("\n", " / "));
                }
            }
            Require(before == EditorJsonUtility.ToJson(target), "표시 중 값 변경: " + target.name);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) Inspect(component);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/SOData" }))
            Inspect(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
        Inspect(Resources.Load<StatusBalanceSO>(StatusRules.BalanceResourcePath));
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/Designer-inspector-untranslated.txt", "단어: " + string.Join(", ", unknown) + "\n필드: " + string.Join(", ", missing));
        Require(unknown.Count == 0 && missing.Count == 0, "번역 누락: " + string.Join(", ", unknown) + " / " + string.Join(", ", missing));
        CheckEditorsAndUndo();
        CheckDefaults();
        CheckStatusBalance();
        Directory.CreateDirectory("Logs");
        string summary = $"[DesignerInspector] PASS: {objects} objects / {types.Count} types / {fields.Count} unique visible fields; labels read-only, Undo, nested fields, multi-edit, defaults.";
        File.WriteAllText("Logs/Designer-inspector-coverage.txt", summary + "\n\n" + report);
        Debug.Log(summary);
    }

    private static void CheckStatusBalance()
    {
        var balance = Resources.Load<StatusBalanceSO>(StatusRules.BalanceResourcePath);
        Require(balance != null, "상태이상 공통 설정 에셋 누락");
        string before = JsonUtility.ToJson(balance);
        var go = new GameObject("DesignerStatusCheck") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            // 설정의 비기본값이 실제 상태 컨테이너에 반영되고, 명시한 시간은 보존되는지 확인한다.
            balance.defaultDuration = 7.25f;
            balance.poisonTickInterval = 0.4f;
            balance.stunMinDuration = 0.3f;
            balance.stunMaxDuration = 0.8f;
            var status = go.AddComponent<CharacterStatus>();
            status.ApplyStatus(StatusType.Poison);
            var states = (System.Collections.IDictionary)typeof(CharacterStatus)
                .GetField("_statuses", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(status);
            float Value(StatusType type, string field) => (float)states[type].GetType().GetField(field).GetValue(states[type]);
            Require(Mathf.Abs(Value(StatusType.Poison, "EndTime") - Time.time - 7.25f) < 0.01f, "중독 기본 지속시간 반영 실패");
            Require(Mathf.Abs(Value(StatusType.Poison, "NextTickTime") - Time.time - 0.4f) < 0.01f, "중독 주기 반영 실패");
            status.ApplyStatus(StatusType.Freeze, 4f);
            Require(Mathf.Abs(Value(StatusType.Freeze, "EndTime") - Time.time - 4f) < 0.01f, "공격에 지정한 시간 변경됨");
            status.ApplyStatus(StatusType.Stun, 8f);
            Require(Mathf.Abs(Value(StatusType.Stun, "EndTime") - Time.time - 0.8f) < 0.01f, "기절 상한 반영 실패");
            status.ApplyFixedStun(4f);
            Require(Mathf.Abs(Value(StatusType.Stun, "EndTime") - Time.time - 4f) < 0.01f, "보스 그로기 시간 제한됨");
            balance.poisonTickInterval = 0f;
            balance.stunMaxDuration = 0.1f;
            Require(StatusRules.POISON_TICK_INTERVAL > 0f && StatusRules.STUN_MAX >= StatusRules.STUN_MIN, "잘못된 입력 안전 보정 실패");
        }
        finally
        {
            JsonUtility.FromJsonOverwrite(before, balance);
            Object.DestroyImmediate(go);
        }
        Require(before == JsonUtility.ToJson(balance), "검사 후 상태이상 에셋 변경됨");
    }

    private static void CheckEditorsAndUndo()
    {
        var a = ScriptableObject.CreateInstance<BoneMasterAIPatternSO>();
        var b = ScriptableObject.CreateInstance<BoneMasterAIPatternSO>();
        Editor editor = null;
        try
        {
            editor = Editor.CreateEditor(new Object[] { a, b });
            Require(editor is DesignerAssetInspector, "SO 공통 인스펙터 선택 실패");
            float old = a.attackGap;
            using (var so = new SerializedObject(new Object[] { a, b }))
            {
                so.Update(); so.FindProperty("attackGap").floatValue = 2.345f;
                so.ApplyModifiedProperties(); Undo.FlushUndoRecordObjects();
                Require(Mathf.Approximately(a.attackGap, 2.345f) && Mathf.Approximately(b.attackGap, 2.345f), "다중 선택 편집 실패");
                Undo.PerformUndo();
                Require(Mathf.Approximately(a.attackGap, old) && Mathf.Approximately(b.attackGap, old), "Undo 복구 실패");
            }
        }
        finally { if (editor != null) Object.DestroyImmediate(editor); Object.DestroyImmediate(a); Object.DestroyImmediate(b); }

        // Awake를 실행하지 않고 실제 프리팹 에셋에서 중첩 목록/파생 컴포넌트를 검사한다.
        var squashPath = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Enemy" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .First(p => p.GetComponentInChildren<VisualSquashStretch>(true) != null);
        var squash = squashPath.GetComponentInChildren<VisualSquashStretch>(true);
        editor = Editor.CreateEditor(squash);
        try { Require(editor is DesignerBehaviourInspector, "컴포넌트 공통 인스펙터 선택 실패"); }
        finally { Object.DestroyImmediate(editor); }
        var field = DesignerInspectorView.ResolveField(squash, "pulses.Array.data[0].curve");
        Require(field != null && field.DeclaringType == typeof(VisualSquashStretch.Pulse), "중첩 목록 필드 탐색 실패");
        var text = DesignerInspectorLabels.Content(field, "curve", "");
        Require(text.tooltip.Contains("X=") && text.tooltip.Contains("Y="), "변형 곡선 축 설명 없음");
    }
    private static void CheckDefaults()
    {
        var root = new GameObject("Designer default check"); root.SetActive(false);
        try
        {
            var impact = root.AddComponent<ImpactFeedback>();
            using (var so = new SerializedObject(impact))
            {
                Require(so.FindProperty("flashDuration").floatValue == .07f, "섬광 기본값 변경");
                Require(so.FindProperty("streakLifetimeRatio").floatValue == .65f, "파편 기본값 변경");
                Require(so.FindProperty("arcScaleRange").vector2Value == new Vector2(.3f, 1.65f), "파동 기본값 변경");
            }
            var feedback = root.AddComponent<CharacterVisualFeedback>();
            using (var so = new SerializedObject(feedback))
                Require(so.FindProperty("tintFlashDuration").floatValue == .1f, "피격 색 기본값 변경");
        }
        finally { Object.DestroyImmediate(root); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("[DesignerInspector] " + message); }
}

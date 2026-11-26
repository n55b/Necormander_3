using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Events;

// Unity/패키지의 전용 인스펙터보다 우선하지 않는다. 게임 스크립트만 한글 표시한다.
[CustomEditor(typeof(MonoBehaviour), true, isFallback = true), CanEditMultipleObjects]
public class DesignerBehaviourInspector : Editor
{
    private DesignerInspectorView view;
    public override void OnInspectorGUI()
    {
        if (!DesignerInspectorView.IsProjectType(target.GetType())) { DrawDefaultInspector(); return; }
        (view ??= new DesignerInspectorView()).Draw(serializedObject);
    }
}

[CustomEditor(typeof(ScriptableObject), true, isFallback = true), CanEditMultipleObjects]
public class DesignerAssetInspector : Editor
{
    private DesignerInspectorView view;
    public override void OnInspectorGUI()
    {
        if (!DesignerInspectorView.IsProjectType(target.GetType())) { DrawDefaultInspector(); return; }
        (view ??= new DesignerInspectorView()).Draw(serializedObject);
    }
}

/// <summary>저장 키를 바꾸지 않고 표시만 번역. 기본 PropertyField를 써서 Undo/프리팹 오버라이드를 유지.</summary>
public sealed class DesignerInspectorView
{
    private string search = "";
    private bool showCode;
    private readonly Dictionary<string, ReorderableList> lists = new Dictionary<string, ReorderableList>();
    private readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();
    private SerializedObject current;
    private const float Gap = 2f;
    public static bool IsProjectType(Type type) => type != null && type.Assembly == typeof(PlayerController).Assembly;

    // 이 화면이 그리는 SO의 DATA 덮어쓰기 키. 파일 수정 시각이 같으면 다시 읽지 않는다.
    private string overridePath; private DateTime overrideStamp; private HashSet<string> overrides = new HashSet<string>();
    private bool owned;
    // 레벨 제작실이 편집을 맡은 타입. 도메인이 제작실로 옮겨 갈 때마다 한 줄씩 더한다(상태이상은 P1 S8).
    internal static readonly Type[] OwnedByWorkshop = Array.Empty<Type>();

    private static string DataFile(UnityEngine.Object target, string root)
    {
        string path = AssetDatabase.GetAssetPath(target), group = ProjectParameterBindings.Group(path);
        return group == null ? null : Path.Combine(root ?? ProjectContentStore.Root, group, AssetDatabase.AssetPathToGUID(path), "parameters.json");
    }
    // root: 검사는 임시 폴더를 넘겨 실제 DATA와 그 잠금·저널을 건드리지 않는다.
    internal static HashSet<string> DataOverrides(UnityEngine.Object target, string root = null)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string file = DataFile(target, root);
        try { if (file != null && File.Exists(file)) foreach (var p in JObject.Parse(File.ReadAllText(file)).Properties()) keys.Add(p.Name); }
        catch (Exception e) when (e is IOException || e is Newtonsoft.Json.JsonException) { }   // 읽을 수 없는 문서는 게임도 기본값을 쓴다.
        return keys;
    }
    private HashSet<string> CachedOverrides(UnityEngine.Object target)
    {
        string file = DataFile(target, null);
        var stamp = file != null && File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
        if (file == overridePath && stamp == overrideStamp) return overrides;
        overridePath = file; overrideStamp = stamp;
        return DataOverrides(target);
    }
    // SerializedProperty 경로 → ProjectParameters 키. "a.Array.data[2].b" → "a/@<id>/b" 또는 "a/2/b"
    internal static string DataKey(SerializedProperty p)
    {
        var parts = new List<string>(); object node = p.serializedObject.targetObject;
        foreach (string segment in p.propertyPath.Replace(".Array.data[", "[").Split('.'))
        {
            int bracket = segment.IndexOf('[');
            string name = bracket < 0 ? segment : segment.Substring(0, bracket);
            parts.Add(name);
            FieldInfo field = null;
            for (var t = node?.GetType(); t != null && field == null; t = t.BaseType)
                field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            node = field?.GetValue(node);
            if (bracket < 0) continue;
            int index = int.Parse(segment.Substring(bracket + 1).TrimEnd(']'), CultureInfo.InvariantCulture);
            var list = node as IList; node = list != null && index < list.Count ? list[index] : null;
            string Id(object e) => e?.GetType().GetField("id")?.GetValue(e) as string;
            string id = Id(node);
            // ProjectParameters.Add와 같은 규칙: 같은 id가 앞에 이미 있으면 인덱스 키. 목록의 +는 id까지 복제한다.
            bool first = !string.IsNullOrEmpty(id) && Enumerable.Range(0, index).All(i => Id(list[i]) != id);
            parts.Add(first ? "@" + Uri.EscapeDataString(id) : index.ToString(CultureInfo.InvariantCulture));
        }
        return string.Join("/", parts);
    }
    // 자식이 덮인 묶음도 통째로 회색이다. PropertyField(includeChildren)는 자식을 따로 막을 수 없다.
    private bool Overridden(SerializedProperty p)
    {
        if (overrides.Count == 0) return false;
        string key = DataKey(p);
        return overrides.Contains(key) || overrides.Any(k => k.StartsWith(key + "/", StringComparison.Ordinal));
    }

    public void Draw(SerializedObject serialized)
    {
        using var propertyFonts = new KoreanEditorText.PropertyFontScope(EditorStyles.label, EditorStyles.boldLabel);
        float previousWidth = EditorGUIUtility.labelWidth;
        try
        {
            // 긴 한글 항목명에 여유를 주되 숫자/연결 필드 공간도 남긴다.
            EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * .5f, 140f, 260f);
            DrawContents(serialized);
        }
        finally { EditorGUIUtility.labelWidth = previousWidth; }
    }

    private void DrawContents(SerializedObject serialized)
    {
        if (current != serialized) { lists.Clear(); fields.Clear(); current = serialized; }
        fields.Clear();
        serialized.Update();
        using (new EditorGUILayout.HorizontalScope())
        {
            search = EditorGUILayout.TextField(new GUIContent("항목 검색", "한글 이름·원래 필드 이름·설명으로 검색합니다."), search);
            showCode = GUILayout.Toggle(showCode, new GUIContent("코드 이름", "개발자와 이야기할 때 원래 필드 이름을 함께 표시합니다."), GUILayout.Width(78));
        }
        KoreanEditorText.Help(Application.isPlaying
            ? "씬 오브젝트의 실행 중 수정은 종료 시 되돌아갑니다. SO 에셋 수정은 남을 수 있으니 원래 값을 기록하세요."
            : "이름 위에 마우스를 올리면 단위와 설명이 나옵니다. 곡선은 클릭해서 편집합니다.", MessageType.None);
        var target = serialized.targetObject;
        owned = Array.Exists(OwnedByWorkshop, t => t.IsInstanceOfType(target));
        overrides = target is ScriptableObject ? CachedOverrides(target) : new HashSet<string>();
        if (owned) KoreanEditorText.Help("레벨 제작실 > 게임 콘텐츠에서 편집합니다. 여기서는 읽기만 합니다.", MessageType.Info);
        else if (overrides.Count > 0) KoreanEditorText.Help("DATA가 덮어씀: " + overrides.Count + "개 항목은 제작실 값이 게임에 적용됩니다. 회색 항목은 여기서 바꿔도 반영되지 않습니다.", MessageType.Warning);
        var iterator = serialized.GetIterator();
        bool enter = true;
        while (iterator.NextVisible(enter))
        {
            enter = false;
            if (iterator.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(iterator, new GUIContent("스크립트"));
                continue;
            }
            if (!Matches(iterator)) continue;
            var p = iterator.Copy();
            if (!string.IsNullOrWhiteSpace(search)) p.isExpanded = true;
            DrawValue(EditorGUILayout.GetControlRect(false, Height(p)), p);
            EditorGUILayout.Space(Gap);
        }
        serialized.ApplyModifiedProperties();
    }

    private bool Matches(SerializedProperty p)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var label = Content(p);
        if ((p.propertyPath + " " + label.text + " " + label.tooltip).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        // 접힌 목록 안의 이름도 검색한다. 값이나 연결된 오브젝트는 바꾸지 않는다.
        if (p.hasVisibleChildren)
        {
            var child = p.Copy(); var end = p.GetEndProperty();
            while (child.NextVisible(true) && !SerializedProperty.EqualContents(child, end))
            {
                label = Content(child);
                if ((child.propertyPath + " " + label.text + " " + label.tooltip).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
        }
        return false;
    }

    private FieldInfo Field(SerializedProperty p)
    {
        // 매 그리기 시작 시 비우므로 관리 참조의 구체 타입을 바꿔도 다음 프레임에 반영된다.
        if (fields.TryGetValue(p.propertyPath, out var cached)) return cached;
        var found = ResolveField(p.serializedObject.targetObject, p.propertyPath);
        fields[p.propertyPath] = found;
        return found;
    }

    internal static FieldInfo ResolveField(object instance, string path)
    {
        Type type = instance?.GetType(); FieldInfo field = null;
        var segments = path.Replace(".Array.data[", "[").Split('.');
        foreach (string segment in segments)
        {
            int bracket = segment.IndexOf('[');
            string name = bracket >= 0 ? segment.Substring(0, bracket) : segment;
            field = null;
            for (var t = instance?.GetType() ?? type; t != null && field == null; t = t.BaseType)
                field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null) return null;
            type = field.FieldType; instance = instance != null ? field.GetValue(instance) : null;
            if (bracket >= 0)
            {
                type = type.IsArray ? type.GetElementType() : type.IsGenericType ? type.GetGenericArguments()[0] : type;
                int.TryParse(segment.Substring(bracket + 1).TrimEnd(']'), out int index);
                instance = instance is IList list && index < list.Count ? list[index] : null;
            }
        }
        return field;
    }

    private GUIContent Content(SerializedProperty p)
    {
        var field = Field(p);
        if (p.propertyType == SerializedPropertyType.ArraySize) return new GUIContent("개수");
        if (p.name.StartsWith("data[", StringComparison.Ordinal)) return new GUIContent("항목 " + (int.Parse(p.name.Substring(5).TrimEnd(']')) + 1));
        return DesignerInspectorLabels.Content(field, p.displayName, p.tooltip, showCode);
    }

    private bool OwnContainer(SerializedProperty p)
    {
        if (p.propertyType != SerializedPropertyType.Generic && p.propertyType != SerializedPropertyType.ManagedReference) return false;
        var f = Field(p);
        if (f == null) return false;
        var type = f.FieldType;
        if (type.IsArray) type = type.GetElementType();
        else if (type.IsGenericType) type = type.GetGenericArguments()[0];
        // UnityEvent 등은 전용 기본 드로워에 맡긴다.
        return IsProjectType(type) && !typeof(UnityEventBase).IsAssignableFrom(type);
    }

    private IEnumerable<SerializedProperty> Children(SerializedProperty p)
    {
        var child = p.Copy(); var end = p.GetEndProperty();
        if (!child.NextVisible(true)) yield break;
        do { if (SerializedProperty.EqualContents(child, end)) yield break; yield return child.Copy(); }
        while (child.NextVisible(false));
    }

    private ReorderableList List(SerializedProperty p)
    {
        if (lists.TryGetValue(p.propertyPath, out var existing)) return existing;
        var array = p.Copy();
        var list = new ReorderableList(p.serializedObject, array, true, true, true, true);
        list.drawHeaderCallback = r => EditorGUI.LabelField(r, new GUIContent("항목 (" + array.arraySize + ")", "왼쪽 손잡이를 끌어 순서를 변경합니다. + / -로 추가하거나 삭제합니다."));
        list.elementHeightCallback = i => i < array.arraySize ? Height(array.GetArrayElementAtIndex(i)) + 4 : EditorGUIUtility.singleLineHeight;
        list.drawElementCallback = (r, i, active, focused) =>
        {
            if (i >= array.arraySize) return;
            r.y += 2; r.height = Height(array.GetArrayElementAtIndex(i));
            DrawValue(r, array.GetArrayElementAtIndex(i));
        };
        lists[p.propertyPath] = list;
        return list;
    }

    private float Height(SerializedProperty p)
    {
        if (!string.IsNullOrWhiteSpace(search) && (p.isArray || OwnContainer(p))) p.isExpanded = true;
        if (p.isArray && p.propertyType != SerializedPropertyType.String)
            return EditorGUI.GetPropertyHeight(p, Content(p), false) + (p.isExpanded ? List(p).GetHeight() + Gap : 0);
        if (!OwnContainer(p)) return EditorGUI.GetPropertyHeight(p, Content(p), true);
        float height = EditorGUI.GetPropertyHeight(p, Content(p), false);
        if (p.isExpanded) foreach (var child in Children(p)) height += Height(child) + Gap;
        return height;
    }

    private void DrawValue(Rect rect, SerializedProperty p)
    {
        var label = Content(p);
        using (new EditorGUI.DisabledScope(owned || DesignerInspectorLabels.ReadOnly(Field(p)) || Overridden(p)))
        {
            if (p.isArray && p.propertyType != SerializedPropertyType.String)
            {
                float h = EditorGUI.GetPropertyHeight(p, label, false);
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, h), p, label, false);
                if (p.isExpanded) { rect.y += h + Gap; rect.height = List(p).GetHeight(); List(p).DoList(rect); }
                return;
            }
            if (!OwnContainer(p)) { EditorGUI.PropertyField(rect, p, label, true); return; }
            float head = EditorGUI.GetPropertyHeight(p, label, false);
            EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, head), p, label, false);
            if (!p.isExpanded) return;
            rect.y += head + Gap; EditorGUI.indentLevel++;
            foreach (var child in Children(p))
            {
                rect.height = Height(child); DrawValue(rect, child); rect.y += rect.height + Gap;
            }
            EditorGUI.indentLevel--;
        }
    }
}

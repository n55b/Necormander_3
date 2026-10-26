using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

    public void Draw(SerializedObject serialized)
    {
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
        EditorGUILayout.HelpBox(Application.isPlaying
            ? "씬 오브젝트의 실행 중 수정은 종료 시 되돌아갑니다. SO 에셋 수정은 남을 수 있으니 원래 값을 기록하세요."
            : "이름 위에 마우스를 올리면 단위와 설명이 나옵니다. 곡선은 클릭해서 편집합니다.", MessageType.None);
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
        using (new EditorGUI.DisabledScope(DesignerInspectorLabels.ReadOnly(Field(p))))
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

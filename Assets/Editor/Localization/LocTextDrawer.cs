using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// LocText를 한국어 글상자 하나로 그린다. 줄바꿈해서 내용만큼 높이가 늘고, 짧으면 한 줄이다.
/// SO의 번역 키는 이름 위 툴팁으로만 보인다(키는 "Tools/번역/번역 표 갱신"이 채운다).
/// 프리팹·씬 컴포넌트의 키는 자동으로 채워지지 않으므로 글상자 아래 '번역 키' 칸에 직접 넣는다.
/// </summary>
[CustomPropertyDrawer(typeof(LocText))]
public class LocTextDrawer : PropertyDrawer
{
    static GUIStyle s_Style;
    static GUIStyle Style => s_Style ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

    // GetPropertyHeight에는 폭이 오지 않으므로 마지막으로 그린 글상자 폭을 기억한다.
    static readonly Dictionary<string, float> s_Widths = new Dictionary<string, float>();

    static bool HasLabel(GUIContent label) => label != null && label != GUIContent.none && (!string.IsNullOrEmpty(label.text) || label.image != null);

    static string WidthKey(SerializedProperty p) => p.serializedObject.targetObject.GetInstanceID() + "/" + p.propertyPath;

    // 번역 표 갱신은 SO의 키만 채운다. 그 밖(프리팹·씬 컴포넌트)은 키 칸을 한 줄 더 그린다.
    static bool KeyRow(SerializedProperty p) => !(p.serializedObject.targetObject is ScriptableObject);
    static float KeyRowHeight => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var ko = property.FindPropertyRelative("ko");
        float extra = KeyRow(property) ? KeyRowHeight : 0f;
        if (ko == null || ko.hasMultipleDifferentValues) return EditorGUIUtility.singleLineHeight + extra;
        if (!s_Widths.TryGetValue(WidthKey(property), out float width))
            width = EditorGUIUtility.currentViewWidth - 40f - (HasLabel(label) ? EditorGUIUtility.labelWidth : 0f);
        float h = Style.CalcHeight(new GUIContent(ko.stringValue), Mathf.Max(40f, width));
        return Mathf.Max(EditorGUIUtility.singleLineHeight, h) + extra;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var ko = property.FindPropertyRelative("ko");
        var key = property.FindPropertyRelative("key");
        if (ko == null) { EditorGUI.PropertyField(position, property, label, true); return; }

        bool keyRow = KeyRow(property) && key != null;
        string keyText = key == null || key.hasMultipleDifferentValues ? "(여러 값)"
            : !string.IsNullOrEmpty(key.stringValue) ? key.stringValue
            : keyRow ? "(없음 - 아래 '번역 키' 칸에 넣어야 번역됨)" : "(없음 - 번역 표 갱신 때 자동으로 채워짐)";
        string tip = (HasLabel(label) && !string.IsNullOrEmpty(label.tooltip) ? label.tooltip + "\n" : "") + "번역 키: " + keyText;

        var content = HasLabel(label) ? new GUIContent(label.text, label.image, tip) : GUIContent.none;
        EditorGUI.BeginProperty(position, content, property);
        Rect area = position;
        if (keyRow) area.height -= KeyRowHeight;
        Rect field = HasLabel(label) ? EditorGUI.PrefixLabel(area, content) : area;
        if (!HasLabel(label)) GUI.Label(field, new GUIContent(string.Empty, tip)); // 이름 없는 표 칸에서도 키를 툴팁으로 보인다.

        if (Event.current.type == EventType.Repaint) s_Widths[WidthKey(property)] = field.width;

        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        EditorGUI.showMixedValue = ko.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        string value = EditorGUI.TextArea(field, ko.stringValue, Style);
        if (EditorGUI.EndChangeCheck()) ko.stringValue = value;
        EditorGUI.showMixedValue = false;
        if (keyRow)
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 48f;
            EditorGUI.PropertyField(new Rect(field.x, field.yMax + EditorGUIUtility.standardVerticalSpacing, field.width, EditorGUIUtility.singleLineHeight),
                key, new GUIContent("번역 키", "Game Text Table의 키. 같은 키는 같은 번역을 쓴다. 비우면 번역되지 않는다."));
            EditorGUIUtility.labelWidth = labelWidth;
        }
        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }
}

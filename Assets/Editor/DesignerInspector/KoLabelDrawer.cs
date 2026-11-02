using UnityEditor;
using UnityEngine;

/// <summary>[KoLabel] 이 붙은 값을 한글 이름(+슬라이더)으로 그린다.</summary>
[CustomPropertyDrawer(typeof(KoLabelAttribute))]
public class KoLabelDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var a = (KoLabelAttribute)attribute;
        // 이름 없이 그려 달라고 하면(GUIContent.none, 표 형태 UI) 그대로 이름 없이 그린다.
        var content = string.IsNullOrEmpty(label.text) ? label : new GUIContent(a.label, label.tooltip);

        EditorGUI.BeginProperty(position, content, property);
        if (property.propertyType == SerializedPropertyType.Float && a.hasMax)
        {
            EditorGUI.BeginChangeCheck();
            float v = EditorGUI.Slider(position, content, property.floatValue, a.min, a.max);
            if (EditorGUI.EndChangeCheck()) property.floatValue = v;
        }
        else if (property.propertyType == SerializedPropertyType.Integer && a.hasMax)
        {
            EditorGUI.BeginChangeCheck();
            int v = EditorGUI.IntSlider(position, content, property.intValue, (int)a.min, (int)a.max);
            if (EditorGUI.EndChangeCheck()) property.intValue = v;
        }
        else
        {
            EditorGUI.PropertyField(position, property, content, true);
            if (a.hasMin && property.propertyType == SerializedPropertyType.Float && property.floatValue < a.min)
                property.floatValue = a.min;
        }
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUI.GetPropertyHeight(property, label, true);
}

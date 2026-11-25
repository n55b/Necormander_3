using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class KoreanEditorText
{
    private static Font font;
    private static Font PanelFont => font != null ? font :
        font = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/Galmuri11.ttf");

    private static readonly Dictionary<GUIStyle, GUIStyle> styles = new();

    internal static GUIStyle Style(GUIStyle original)
    {
        if (!styles.TryGetValue(original, out var style))
            styles.Add(original, style = new GUIStyle(original) { font = PanelFont });
        return style;
    }

    // Unity's native property and header drawers choose these styles internally.
    // Each scope restores the fonts it found, including in nested custom inspectors.
    internal readonly struct PropertyFontScope : IDisposable
    {
        private readonly GUIStyle label, header;
        private readonly Font previousLabel, previousHeader;

        internal PropertyFontScope(GUIStyle label, GUIStyle header)
        {
            this.label = label;
            this.header = header;
            previousLabel = label.font;
            previousHeader = header.font;
            label.font = header.font = PanelFont;
        }

        public void Dispose()
        {
            label.font = previousLabel;
            header.font = previousHeader;
        }
    }

    // IMGUI measures wrapped text after OnGUI returns, so it needs its own Korean font style.
    internal static void Help(string message, MessageType type)
    {
        var icon = type == MessageType.Error ? EditorGUIUtility.IconContent("console.erroricon.sml").image :
            type == MessageType.Info ? EditorGUIUtility.IconContent("console.infoicon.sml").image : null;
        GUILayout.Label(new GUIContent(message, icon), Style(EditorStyles.helpBox));
    }
}

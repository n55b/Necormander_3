using UnityEditor;
using UnityEngine;

/// <summary>
/// 장비 편집기 창 (Tools/기획/장비 편집기).
/// 화면 내용은 EquipmentEditorPanel(EquipmentEditor.cs)이 그린다.
/// 새 장비는 이 창 위쪽의 '새 장비' 버튼으로 만든다.
/// </summary>
public class EquipmentEditorWindow : EditorWindow
{
    [SerializeField] private EquipmentEditorPanel panel = new EquipmentEditorPanel();

    [MenuItem("Tools/기획/장비 편집기", priority = 3)]
    public static void OpenMenu() => Open(null);

    /// <summary>장비 편집기를 열고(선택이 있으면 그 장비를 골라) 화면을 돌려준다.</summary>
    public static EquipmentEditorPanel Open(EquipmentSO select)
    {
        var w = GetWindow<EquipmentEditorWindow>();
        w.titleContent = new GUIContent("장비 편집기");
        w.minSize = new Vector2(760, 420);
        w.Show();
        w.Focus();
        w.panel.Select(select);
        w.Repaint();
        return w.panel;
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("장비 편집기");
        if (panel == null) panel = new EquipmentEditorPanel();
        panel.Enable(this);
    }

    private void OnDisable() => panel?.Disable();

    private void OnGUI() => panel.OnGUI();
}

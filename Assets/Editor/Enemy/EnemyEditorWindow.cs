using UnityEditor;
using UnityEngine;

/// <summary>
/// 몬스터 편집기 창 (Tools/기획/몬스터 편집기).
/// 화면 내용은 EnemyEditorPanel(EnemyEditor.cs)이 그린다.
/// 새 몬스터는 이 창 위쪽의 '새 적' 버튼으로, 군집은 '군집 편집' 탭에서 만든다.
/// </summary>
public class EnemyEditorWindow : EditorWindow
{
    [SerializeField] private EnemyEditorPanel panel = new EnemyEditorPanel();

    [MenuItem("Tools/기획/몬스터 편집기", priority = 4)]
    public static void OpenMenu() => Open(null);

    /// <summary>몬스터 편집기를 열고(선택이 있으면 그 적을 골라) 화면을 돌려준다.</summary>
    public static EnemyEditorPanel Open(EnemyMinionDataSO select)
    {
        var w = GetWindow<EnemyEditorWindow>();
        w.titleContent = new GUIContent("몬스터 편집기");
        w.minSize = new Vector2(820, 460);
        w.Show();
        w.Focus();
        w.panel.Select(select);
        w.Repaint();
        return w.panel;
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("몬스터 편집기");
        if (panel == null) panel = new EnemyEditorPanel();
        panel.Enable(this);
    }

    private void OnDisable() => panel?.Disable();

    private void OnGUI() => panel.OnGUI();
}

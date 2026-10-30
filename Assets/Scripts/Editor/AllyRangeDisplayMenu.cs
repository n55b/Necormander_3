using UnityEditor;
using UnityEngine;

/// <summary>
/// 메뉴: Tools > 디버그 > 소환수 공격 범위 표시   (단축키 Alt + Shift + H)
/// 체크 = 보임(디버그), 체크 해제 = 숨김(기본). 플레이 중에도 바로 바뀐다. 에디터 전용.
/// </summary>
public static class AllyRangeDisplayMenu
{
    private const string MenuName = "Tools/디버그/소환수 공격 범위 표시";

    [MenuItem(MenuName + " &#h", priority = 100)]
    private static void Toggle()
    {
        AllyRangeDisplay.Visible = !AllyRangeDisplay.Visible;
        string msg = "소환수 공격 범위: " + (AllyRangeDisplay.Visible ? "보임 (디버그)" : "숨김");
        Debug.Log($"<color=cyan>[디버그]</color> {msg}");
        var content = new GUIContent(msg);
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.ShowNotification(content, 1.5f);
        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if (w.GetType().Name == "GameView") w.ShowNotification(content, 1.5f);
    }

    [MenuItem(MenuName + " &#h", true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuName, AllyRangeDisplay.Visible);
        return true;
    }
}

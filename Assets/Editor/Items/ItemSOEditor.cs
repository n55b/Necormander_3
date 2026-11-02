using UnityEditor;
using UnityEngine;

/// <summary>
/// 아이템(ItemSO) 전용 인스펙터. 공통 기획자 인스펙터(DesignerAssetInspector, isFallback)보다 우선한다.
/// 효과 종류를 메뉴에서 골라 추가하고, 수치를 % 로 입력하고, 게임 표시 문구를 바로 미리 본다.
/// 여러 아이템을 한 번에 고치려면 Tools > Items > 아이템 편집기.
/// </summary>
[CustomEditor(typeof(ItemSO))]
public class ItemSOEditor : Editor
{
    // 마지막으로 그린 아이템. Project 창에서 다른 아이템을 고르면 입력 중이던 글 칸을 닫는다
    // (안 닫으면 입력 중이던 글이 새로 고른 아이템에 붙는다).
    private static Object lastTarget;

    public override void OnInspectorGUI()
    {
        if (target != lastTarget) { lastTarget = target; ItemEditorGUI.EndTextEditing(); }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("아이템 편집기 열기", "모든 아이템을 표로 보고 한 번에 고친다."), GUILayout.Width(140)))
                ItemDatabaseWindow.Open(target as ItemSO);
        }
        ItemEditorGUI.DrawItem(serializedObject);
    }
}

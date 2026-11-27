using UnityEditor;
using UnityEngine;

/// <summary>
/// 옛 아이템 인스펙터. 아이템 편집은 제작실 > 게임 콘텐츠 > 아이템으로 옮겼다. [CustomEditor]를 떼어
/// 공통 기획자 인스펙터(DesignerAssetInspector)가 읽기 전용 + 안내로 그린다(DesignerInspector.OwnedByWorkshop).
/// 대조표 승인 뒤 Legacy로 보낸다.
/// </summary>
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

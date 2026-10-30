using UnityEditor;
using UnityEngine;

/// <summary>UI 스킨 설정 인스펙터: 사용법 안내 + 자주 쓰는 버튼.</summary>
[CustomEditor(typeof(UISkinSettings))]
public class UISkinSettingsEditor : Editor
{
    private static bool _showHelp = true;

    public override void OnInspectorGUI()
    {
        var s = (UISkinSettings)target;

        _showHelp = EditorGUILayout.Foldout(_showHelp, "사용법", true);
        if (_showHelp)
        {
            EditorGUILayout.HelpBox(
                "이 파일 하나로 인게임 UI의 '모양'과 '움직임'을 조절합니다.\n\n" +
                "• ①~③ 모양: 역할마다 스프라이트·색·테두리 굵기를 정합니다. 바꾸면 같은 역할의 UI가 전부 함께 바뀝니다.\n" +
                "• ④~⑧ 연출: 창 열림, 체력바 잔상·번쩍임·위험 경고, 스킬 준비 완료, 마우스 반응의 시간과 크기를 정합니다.\n\n" +
                "플레이 중에 바꿔도 바로 보이고, 값은 그대로 저장됩니다. 마음에 들 때까지 플레이하면서 만져 보세요.\n" +
                "모든 칸에 마우스를 올리면 설명이 나옵니다.",
                MessageType.Info);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("모양을 UI_Panel 기본값으로", "①~③ 의 스프라이트·색·굵기를 처음 값으로 되돌립니다. 연출 값은 그대로 둡니다.")))
            {
                Undo.RecordObject(s, "UI 스킨 모양 초기화");
                UISkinApplyTool.FillDefaultSkins(s);
                UISkinSettings.NotifyChanged();
            }
            if (GUILayout.Button(new GUIContent("연출을 기본값으로", "④~⑧ 의 시간·크기·색을 처음 값으로 되돌립니다. 모양은 그대로 둡니다.")))
            {
                Undo.RecordObject(s, "UI 연출 초기화");
                UISkinApplyTool.FillDefaultMotions(s);
                UISkinSettings.NotifyChanged();
            }
        }
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button(new GUIContent("인게임 UI 프리팹에 스킨 다시 붙이기", "HUD·미니맵·주머니·장착 정보·툴팁 프리팹에 스킨 부품과 연출을 다시 붙입니다. (플레이 중에는 못 누릅니다)")))
                UISkinApplyTool.ApplyAll();
        }

        EditorGUILayout.Space(6);
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        if (serializedObject.ApplyModifiedProperties()) UISkinSettings.NotifyChanged();
    }
}

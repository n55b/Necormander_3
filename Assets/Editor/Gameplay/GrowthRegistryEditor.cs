using UnityEngine;
using UnityEditor;

/// <summary>
/// GrowthRegistrySO 인스펙터에 자동 갱신 버튼을 추가하는 커스텀 에디터입니다.
/// </summary>
[CustomEditor(typeof(GrowthRegistrySO))]
public class GrowthRegistryEditor : Editor
{
    private DesignerInspectorView designerView;
    public override void OnInspectorGUI()
    {
        // 기본 인스펙터 필드들을 먼저 그립니다.
        (designerView ??= new DesignerInspectorView()).Draw(serializedObject);

        EditorGUILayout.Space(20);
        EditorGUILayout.HelpBox("새 메인 소환수를 이 목록 맨 뒤에 붙입니다. 기존 순서(마을 카드 순서)는 바뀌지 않습니다.\n" +
                                "소환수 레지스트리(MinionRegistrySO)의 아군/일반 적/보스 목록도 다시 분류해 저장합니다(엘리트 목록은 유지).\n" +
                                "(Tools/기획/레지스트리 새로고침 (새 소환수 등록) 과 같은 동작)", MessageType.Info);

        GUI.backgroundColor = Color.cyan;
        // RefreshRegistry(전체 재구성)는 순서를 바꾸므로 쓰지 않는다 — 덧붙이기만 하는 메뉴와 같은 경로.
        if (GUILayout.Button("새 소환수 등록 (레지스트리 새로고침)", GUILayout.Height(40)))
        {
            RegistryMenuTools.RefreshAllRegistries();
        }
        GUI.backgroundColor = Color.white;
    }
}

using UnityEngine;
using UnityEditor;

/// <summary>
/// GrowthRegistrySO 인스펙터에 자동 갱신 버튼을 추가하는 커스텀 에디터입니다.
/// </summary>
[CustomEditor(typeof(GrowthRegistrySO))]
public class GrowthRegistryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 기본 인스펙터 필드들을 먼저 그립니다.
        DrawDefaultInspector();

        EditorGUILayout.Space(20);
        EditorGUILayout.HelpBox("프로젝트 내의 모든 계보, 보석, 보물을 자동으로 찾아 리스트를 갱신합니다.", MessageType.Info);

        GUI.backgroundColor = Color.cyan;
        if (GUILayout.Button("전체 데이터 자동 스캔 및 갱신", GUILayout.Height(40)))
        {
            GrowthRegistrySO registry = (GrowthRegistrySO)target;
            registry.RefreshRegistry();
        }
        GUI.backgroundColor = Color.white;
    }
}

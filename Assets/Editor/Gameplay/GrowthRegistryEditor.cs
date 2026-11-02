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
        EditorGUILayout.HelpBox("프로젝트의 현재 성장 데이터 목록을 다시 스캔합니다. 수동으로 구성한 목록도 바뀌므로 필요한 경우에만 실행하세요.", MessageType.Info);

        GUI.backgroundColor = Color.cyan;
        if (GUILayout.Button("전체 데이터 자동 스캔 및 갱신", GUILayout.Height(40)))
        {
            GrowthRegistrySO registry = (GrowthRegistrySO)target;
            Undo.RecordObject(registry, "성장 데이터 목록 갱신");
            registry.RefreshRegistry();
        }
        GUI.backgroundColor = Color.white;
    }
}

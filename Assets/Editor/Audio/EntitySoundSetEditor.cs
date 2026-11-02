using UnityEditor;
using UnityEngine;

/// <summary>사운드 세트 인스펙터: 안내 + 사운드 테스트 툴 바로가기.</summary>
[CustomEditor(typeof(EntitySoundSet))]
public class EntitySoundSetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "소리를 넣고 바로 들어보려면 아래 버튼으로 사운드 테스트 툴을 여세요.\n" +
            "맞았을 때/회복/죽을 때 소리는 체력에, 공격 소리는 애니메이션 이벤트(OnHitEvent)에 자동으로 연결됩니다.",
            MessageType.Info);
        if (GUILayout.Button("사운드 테스트 툴에서 열기", GUILayout.Height(26)))
            SoundTestWindow.OpenFor(((Component)target).gameObject);
        EditorGUILayout.Space(4);
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();
    }
}

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FeedbackCatalog)), CanEditMultipleObjects]
public sealed class FeedbackCatalogEditor : Editor
{
    private string search="";
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("그림 크기·피해 판정은 그대로입니다. 체크를 끄면 해당 신규 연출만 멈춥니다. 0~3은 기본 / 강화 / 진화 / 최종 단계입니다. 가드와 상태이상 신규 연출은 제외했습니다.",MessageType.Info);
        var property=serializedObject.GetIterator();
        bool enter=true;
        while(property.NextVisible(enter))
        {
            enter=false;
            if(property.name=="m_Script"||property.name=="effects") continue;
            if(property.name=="tiers")
            {
                property.isExpanded=EditorGUILayout.Foldout(property.isExpanded,"공격 성장 4단계",true);
                if(property.isExpanded) for(int i=0;i<property.arraySize;i++)
                {
                    var tier=property.GetArrayElementAtIndex(i); EditorGUILayout.LabelField($"{i}단계",EditorStyles.boldLabel);
                    foreach(var name in new[]{"title","intensity","density","reach"}) Draw(tier.FindPropertyRelative(name),typeof(FeedbackTier));
                }
            }
            else Draw(property,typeof(FeedbackCatalog));
        }
        search=EditorGUILayout.TextField("효과 찾기 (번호·이름)",search);
        var effects=serializedObject.FindProperty("effects");
        for(int i=0;i<effects.arraySize;i++)
        {
            var entry=effects.GetArrayElementAtIndex(i);
            string label=$"{entry.FindPropertyRelative("cue").intValue:00} · {entry.FindPropertyRelative("title").stringValue}";
            if(!string.IsNullOrWhiteSpace(search)&&!label.Contains(search)) continue;
            EditorGUILayout.BeginHorizontal();
            var on=entry.FindPropertyRelative("enabled"); on.boolValue=EditorGUILayout.ToggleLeft(label,on.boolValue);
            if(GUILayout.Button(entry.isExpanded?"접기":"조절",GUILayout.Width(44))) entry.isExpanded=!entry.isExpanded;
            EditorGUILayout.EndHorizontal();
            if(!entry.isExpanded) continue;
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(entry.FindPropertyRelative("description").stringValue,MessageType.None);
            foreach(var field in typeof(FeedbackRecipe).GetFields())
            {
                if(field.Name=="cue"||field.Name=="enabled"||field.Name=="description"||field.Name=="title") continue;
                Draw(entry.FindPropertyRelative(field.Name),typeof(FeedbackRecipe));
            }
            EditorGUI.indentLevel--;
        }
        serializedObject.ApplyModifiedProperties();
    }
    private static void Draw(SerializedProperty property,System.Type type)
    {
        var field=type.GetField(property.name);
        var label=DesignerInspectorLabels.Content(field,property.displayName,property.tooltip);
        EditorGUILayout.PropertyField(property,label,true);
    }
}

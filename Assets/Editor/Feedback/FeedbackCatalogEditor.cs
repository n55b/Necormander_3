using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FeedbackCatalog)), CanEditMultipleObjects]
public sealed class FeedbackCatalogEditor : Editor
{
    private string search="";
    private ExistingFeedbackCatalogEditor existing;
    private ImpactKind previewKind;
    private Vector2 previewDirection = Vector2.right;
    private void OnDisable() => existing?.Dispose();
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.LabelField("카메라·시간 연출", KoreanEditorText.Style(EditorStyles.boldLabel));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("impactProfiles"), new GUIContent("타격별 프리셋"), true);
        previewKind = (ImpactKind)EditorGUILayout.EnumPopup("미리 볼 타격", previewKind);
        previewDirection = EditorGUILayout.Vector2Field("타격 방향", previewDirection);
        var manager = CameraManager.Instance;
        using (new EditorGUI.DisabledScope(!Application.isPlaying || manager == null))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("단일 타격")) manager.Request(previewKind, previewDirection);
                if (GUILayout.Button("3연타")) manager.StartCoroutine(PreviewHits(manager, previewKind, previewDirection));
                if (GUILayout.Button("보스 처치")) manager.Request(ImpactKind.BossDeath, previewDirection);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("1배 줌")) manager.SetPixelZoom(1);
                if (GUILayout.Button("2배 줌")) manager.SetPixelZoom(2);
                if (GUILayout.Button("기본 줌")) manager.SetZoom(false);
            }
        }
        if (Application.isPlaying && manager != null)
            foreach (var entry in manager.Trace) EditorGUILayout.LabelField(entry, EditorStyles.miniLabel);
        else KoreanEditorText.Help("마을이나 전투 씬을 재생하면 미리보기와 최근 요청을 확인할 수 있습니다.", MessageType.None);
        KoreanEditorText.Help("전체 사용은 기존·신규 연출을 함께 켜고 끕니다. 기존 연출의 원본 값은 아래에서 대상별로 조절합니다. 피해 판정·로딩 대기는 유지합니다. 0~3은 기본 / 강화 / 진화 / 최종 단계입니다.",MessageType.Info);
        var property=serializedObject.GetIterator();
        bool enter=true;
        while(property.NextVisible(enter))
        {
            enter=false;
            if(property.name=="m_Script"||property.name=="effects"||property.name=="existingEffects"||property.name=="impactProfiles") continue;
            if(property.name=="tiers")
            {
                property.isExpanded=EditorGUILayout.Foldout(property.isExpanded,"공격 성장 4단계",true);
                if(property.isExpanded) for(int i=0;i<property.arraySize;i++)
                {
                    var tier=property.GetArrayElementAtIndex(i); EditorGUILayout.LabelField($"{i}단계",KoreanEditorText.Style(EditorStyles.boldLabel));
                    foreach(var name in new[]{"title","intensity","density","reach"}) Draw(tier.FindPropertyRelative(name),typeof(FeedbackTier));
                }
            }
            else Draw(property,typeof(FeedbackCatalog));
        }
        search=EditorGUILayout.TextField("효과 찾기 (번호·이름)",search);
        (existing ??= new ExistingFeedbackCatalogEditor()).Draw(serializedObject, search);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("시각 연출 · 행동 효과음", KoreanEditorText.Style(EditorStyles.boldLabel));
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
            KoreanEditorText.Help(entry.FindPropertyRelative("description").stringValue,MessageType.None);
            foreach(var field in typeof(FeedbackRecipe).GetFields())
            {
                if(field.Name=="cue"||field.Name=="enabled"||field.Name=="description"||field.Name=="title"||field.IsDefined(typeof(HideInInspector),false)) continue;
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

    private static System.Collections.IEnumerator PreviewHits(CameraManager manager, ImpactKind kind, Vector2 direction)
    {
        for (int i = 0; i < 3; i++)
        {
            manager.Request(kind, direction);
            yield return new WaitForSecondsRealtime(.12f);
        }
    }
}

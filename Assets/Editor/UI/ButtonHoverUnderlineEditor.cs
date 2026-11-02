using UnityEditor;
using UnityEngine;

/// <summary>
/// 버튼 호버 밑줄 인스펙터 (기획자용).
///  - 연결된 설정 에셋을 이 오브젝트 인스펙터 안에서 바로 편집 (같은 에셋을 쓰는 버튼 모두에 적용)
///  - 값을 바꾸면 Play 없이 Game 뷰에 즉시 반영
///  - '나타나기 / 사라지기 재생' 버튼으로 실제 속도를 Play 없이 확인
/// </summary>
[CustomEditor(typeof(ButtonHoverUnderline))]
[CanEditMultipleObjects]
public class ButtonHoverUnderlineEditor : Editor
{
    private static bool _foldLook = true, _foldLength = true, _foldShow = true, _foldHide = true, _foldEtc = false;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "버튼(글자)에 마우스를 올리면 아래변에 밑줄이 차오릅니다.\n" +
            "여기서 바꾼 값은 같은 설정 에셋을 쓰는 버튼 전부에 바로 적용되고, Play를 꺼도 남습니다.\n" +
            "길이·두께·위치 단위는 캔버스 픽셀입니다.",
            MessageType.Info);

        // ── 설정 에셋 ─────────────────────────────────────────
        var settingsProp = serializedObject.FindProperty("settings");
        EditorGUILayout.PropertyField(settingsProp, new GUIContent("설정 에셋", "값이 저장되는 에셋. 버튼마다 다른 에셋을 끼우면 버튼별로 다르게 할 수도 있음"));
        if (settingsProp.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox("선택한 버튼들이 서로 다른 설정 에셋을 쓰고 있습니다. 하나씩 선택해서 편집하세요.", MessageType.None);
            serializedObject.ApplyModifiedProperties();
            return;
        }
        if (settingsProp.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("설정 에셋이 없습니다.", MessageType.Warning);
            if (GUILayout.Button("새 설정 에셋 만들기"))
            {
                var path = EditorUtility.SaveFilePanelInProject("설정 에셋 만들기", "ButtonHoverUnderlineSettings", "asset", "");
                if (!string.IsNullOrEmpty(path))
                {
                    var created = CreateInstance<ButtonHoverUnderlineSettingsSO>();
                    AssetDatabase.CreateAsset(created, path);
                    settingsProp.objectReferenceValue = created;
                }
            }
            serializedObject.ApplyModifiedProperties();
            return;
        }

        // ── 미리보기 ─────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("미리보기", EditorStyles.boldLabel);
        var previewProp = serializedObject.FindProperty("editorPreview");
        EditorGUILayout.Slider(previewProp, 0f, 1f, new GUIContent("채워진 정도", "0 = 안 보임, 1 = 끝까지 참. Play 중에는 실제 마우스를 따름"));
        bool changedHere = serializedObject.ApplyModifiedProperties();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("▶ 나타나기 재생")) ForEach(u => u.EditorPlay(true));
            if (GUILayout.Button("◀ 사라지기 재생")) ForEach(u => u.EditorPlay(false));
        }
        if (changedHere) ForEach(u => u.Refresh());

        // ── 설정 에셋 내용 ────────────────────────────────────
        EditorGUILayout.Space(8);
        var settings = (ButtonHoverUnderlineSettingsSO)settingsProp.objectReferenceValue;
        var s = new SerializedObject(settings);
        s.Update();

        _foldLook = Section(_foldLook, "모양");
        if (_foldLook)
        {
            Prop(s, "color", "색", "밑줄 색 (투명도 포함)");
            Prop(s, "thickness", "두께", "밑줄 두께 (캔버스 픽셀)");
            Prop(s, "offsetY", "위아래 위치", "0 = 버튼 아래변에 딱 붙음. - 더 아래로, + 위로 (캔버스 픽셀)");
            Prop(s, "sprite", "그림", "비워두면 단색 막대");
        }

        _foldLength = Section(_foldLength, "길이");
        if (_foldLength)
        {
            Prop(s, "lengthRatio", "길이 (버튼 너비 대비)", "1 = 버튼 아래변 전체를 채움. 0.5 = 절반");
            Prop(s, "extraLength", "양 끝 추가 길이", "한쪽당 더 늘리는 길이 (캔버스 픽셀). - 로 하면 양쪽이 짧아짐");
            using (new EditorGUI.DisabledScope(Mathf.Approximately(s.FindProperty("lengthRatio").floatValue, 1f)))
                Prop(s, "align", "붙는 위치", "길이가 버튼보다 짧을 때만 의미 있음. 0 = 왼쪽, 0.5 = 가운데, 1 = 오른쪽");
        }

        _foldShow = Section(_foldShow, "나타날 때 (마우스 올림)");
        if (_foldShow)
        {
            Prop(s, "direction", "차오르는 방향", "");
            Prop(s, "showDuration", "걸리는 시간 (초)", "끝까지 차오르는 데 걸리는 시간. 작을수록 빠름");
            Prop(s, "showCurve", "속도 곡선", "가로 = 시간, 세로 = 채워진 정도. 처음에 가파르면 빠르게 튀어나갔다가 끝에서 느려짐");
            Prop(s, "showDelay", "시작 지연 (초)", "마우스를 올리고 이만큼 기다린 뒤 차오르기 시작");
        }

        _foldHide = Section(_foldHide, "사라질 때 (마우스 벗어남)");
        if (_foldHide)
        {
            Prop(s, "hideStyle", "사라지는 방식", "'계속 진행'은 '가운데 → 양옆' 방향일 때 되감기로 동작");
            Prop(s, "hideDuration", "걸리는 시간 (초)", "다 사라지는 데 걸리는 시간");
            Prop(s, "hideCurve", "속도 곡선", "가로 = 시간, 세로 = 진행 정도");
        }

        _foldEtc = Section(_foldEtc, "기타");
        if (_foldEtc)
        {
            Prop(s, "showOnSelect", "키보드/패드 선택 시에도 표시", "");
            Prop(s, "ignoreTimeScale", "일시정지 중에도 움직임", "");
        }

        if (s.ApplyModifiedProperties())
        {
            settings.NotifyChanged();
            ForEach(u => u.Refresh());
        }

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("설정 에셋 선택"))
                EditorGUIUtility.PingObject(settings);
            if (GUILayout.Button("기본값으로 되돌리기") &&
                EditorUtility.DisplayDialog("기본값으로 되돌리기", "그림을 제외한 모든 값을 처음 값으로 되돌립니다.\n(Ctrl+Z 로 취소 가능)", "되돌리기", "취소"))
            {
                Undo.RecordObject(settings, "Reset Button Hover Underline");
                settings.ResetToDefaults();
                EditorUtility.SetDirty(settings);
                ForEach(u => u.Refresh());
            }
        }
    }

    private void ForEach(System.Action<ButtonHoverUnderline> a)
    {
        foreach (var t in targets)
            if (t is ButtonHoverUnderline u && u != null) a(u);
    }

    private static bool Section(bool open, string title)
    {
        EditorGUILayout.Space(4);
        return EditorGUILayout.BeginFoldoutHeaderGroup(open, title) & EndHeader();
    }

    private static bool EndHeader() { EditorGUILayout.EndFoldoutHeaderGroup(); return true; }

    private static void Prop(SerializedObject so, string name, string label, string tip)
    {
        var p = so.FindProperty(name);
        if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label, tip));
    }
}

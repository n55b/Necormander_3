using UnityEditor;
using UnityEngine;

/// <summary>
/// 타이틀 수정구슬 인스펙터 (기획자용).
///  - 연결된 설정 에셋을 이 오브젝트 인스펙터 안에서 바로 편집
///  - 값을 바꾸면 Play 없이 Game 뷰에 즉시 반영
///  - '둘러보기 미리보기' 패드를 드래그하면 마우스를 움직인 것처럼 미리 볼 수 있음
/// </summary>
[CustomEditor(typeof(TitleCrystalBall))]
public class TitleCrystalBallEditor : Editor
{
    // 섹션 접기 상태 (에디터 세션 동안 유지)

    private static bool _foldImage = true, _foldOutside = true, _foldOrb = true, _foldLook = true, _foldLayer1 = true, _foldLayer2 = true;

    public override void OnInspectorGUI()
    {
        var ball = (TitleCrystalBall)target;
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "여기서 값을 바꾸면 Play 없이 Game 뷰에 바로 보입니다.\n" +
            "설정은 에셋에 저장되므로 Play 중에 바꾼 값도 Play를 꺼도 남습니다.\n" +
            "거리 값은 '그림 높이 대비 비율'입니다 (0.1 = 그림 높이의 10%).",
            MessageType.Info);

        // ── 설정 에셋 ─────────────────────────────────────────
        var settingsProp = serializedObject.FindProperty("settings");
        EditorGUILayout.PropertyField(settingsProp, new GUIContent("설정 에셋", "값이 저장되는 에셋. 다른 버전을 만들어 바꿔 끼워 비교할 수도 있음"));
        if (settingsProp.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("설정 에셋이 없습니다.", MessageType.Warning);
            if (GUILayout.Button("새 설정 에셋 만들기"))
            {
                var path = EditorUtility.SaveFilePanelInProject("설정 에셋 만들기", "TitleCrystalBallSettings", "asset", "");
                if (!string.IsNullOrEmpty(path))
                {
                    var so = CreateInstance<TitleCrystalBallSettingsSO>();
                    AssetDatabase.CreateAsset(so, path);
                    settingsProp.objectReferenceValue = so;
                }
            }
            serializedObject.ApplyModifiedProperties();
            return;
        }

        // ── 미리보기 패드 ─────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("둘러보기 미리보기 (에디터 전용)", EditorStyles.boldLabel);
        var lookProp = serializedObject.FindProperty("editorPreviewLook");
        using (new EditorGUILayout.HorizontalScope())
        {
            Rect pad = GUILayoutUtility.GetRect(110, 110, GUILayout.Width(110), GUILayout.Height(110));
            Vector2 v = DrawLookPad(pad, lookProp.vector2Value);
            if (v != lookProp.vector2Value) lookProp.vector2Value = v;

            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("패드를 드래그하면 마우스를 그쪽으로", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("최대로 옮겼을 때 모습을 미리 봅니다.", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Play 중에는 실제 마우스를 따릅니다.", EditorStyles.miniLabel);
                GUILayout.Space(6);
                if (GUILayout.Button("가운데로 (기본 모습)", GUILayout.Width(150))) lookProp.vector2Value = Vector2.zero;
            }
        }
        if (serializedObject.ApplyModifiedProperties()) ball.Refresh();

        // ── 설정 에셋 내용 ────────────────────────────────────
        EditorGUILayout.Space(8);
        var settings = (TitleCrystalBallSettingsSO)settingsProp.objectReferenceValue;
        var s = new SerializedObject(settings);
        s.Update();

        _foldImage = Section(_foldImage, "그림 / 색");
        if (_foldImage)
        {
            Prop(s, "titleSprite", "그림", "구슬 안에 보일 그림 (Title_Image)");
            Prop(s, "imageBrightness", "그림 밝기", "1 = 원본. 낮추면 어둡게, 높이면 밝게");
            Prop(s, "imageTint", "그림 색조", "그림에 곱해지는 색. 흰색 = 원본");
        }

        _foldOutside = Section(_foldOutside, "구슬 바깥 (어둡게)");
        if (_foldOutside)
        {
            Prop(s, "outsideBrightness", "테두리 바깥 밝기", "구슬 바로 바깥 그림 밝기. 0 = 바로 깜깜, 1 = 구슬 안과 같음");
            Prop(s, "outsideFade", "어두워지는 거리", "구슬에서 이만큼 멀어지면 완전히 배경색이 됨. 작을수록 빨리 깜깜해짐");
            Prop(s, "outsideFadeCurve", "어두워지는 곡선", "1 = 일정하게. 클수록 구슬 근처는 밝게 유지되다가 멀리서 급하게 어두워짐");
            Prop(s, "backgroundColor", "가장 먼 곳 색", "구슬에서 멀어질수록 이 색으로 사라짐");
        }

        _foldOrb = Section(_foldOrb, "구슬 위치 / 크기");
        if (_foldOrb)
        {
            var c = s.FindProperty("orbCenter");
            Vector2 cv = c.vector2Value;
            cv.x = EditorGUILayout.Slider(new GUIContent("가로 위치", "0 = 왼쪽 끝, 1 = 오른쪽 끝"), cv.x, -0.5f, 1.5f);
            cv.y = EditorGUILayout.Slider(new GUIContent("세로 위치", "0 = 아래 끝, 1 = 위 끝"), cv.y, -0.5f, 1.5f);
            c.vector2Value = cv;
            Prop(s, "orbRadius", "크기 (반지름)", "0.5 = 화면 위아래에 딱 닿는 크기");
        }

        _foldLook = Section(_foldLook, "둘러보기 (마우스 따라 움직임)");
        if (_foldLook)
        {
            Prop(s, "lookRadius", "반응 거리", "마우스가 구슬 중심에서 이만큼 떨어지면 최대로 움직임. 작을수록 예민");
            Prop(s, "orbMove", "구슬 이동량", "구슬 테두리 자체가 움직이는 최대 거리");
            Prop(s, "imagePan", "그림 이동량", "구슬 안 그림이 움직이는 최대 거리 (마우스 쪽 풍경이 드러남)");
            Prop(s, "smoothTime", "따라오는 느긋함", "클수록 천천히 따라옴 (초)");
            Prop(s, "idleBob", "떠다님 세기", "가만히 있어도 살짝 움직이는 정도. 0 = 끔");
            Prop(s, "idleBobSpeed", "떠다님 속도", "");
        }

        _foldLayer1 = Section(_foldLayer1, "어둠 원 1 (원 바깥이 어두워짐, 뒤쪽)");
        if (_foldLayer1) LayerGUI(s.FindProperty("layer1"));

        _foldLayer2 = Section(_foldLayer2, "어둠 원 2 (원 바깥이 어두워짐, 앞쪽)");
        if (_foldLayer2) LayerGUI(s.FindProperty("layer2"));

        if (s.ApplyModifiedProperties()) ball.Refresh();

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("설정 에셋 선택"))
                EditorGUIUtility.PingObject(settings);
            if (GUILayout.Button("기본값으로 되돌리기") &&
                EditorUtility.DisplayDialog("기본값으로 되돌리기", "그림을 제외한 모든 값을 처음 값으로 되돌립니다.\n(Ctrl+Z 로 취소 가능)", "되돌리기", "취소"))
            {
                Undo.RecordObject(settings, "Reset Title Crystal Ball");
                settings.ResetToDefaults();
                EditorUtility.SetDirty(settings);
                ball.Refresh();
            }
        }
    }

    private static void LayerGUI(SerializedProperty layer)
    {
        Rel(layer, "enabled", "켜기", "");
        using (new EditorGUI.DisabledScope(!layer.FindPropertyRelative("enabled").boolValue))
        {
            var off = layer.FindPropertyRelative("offset");
            Vector2 o = off.vector2Value;
            o.x = EditorGUILayout.Slider(new GUIContent("가로 위치", "구슬 중심 기준. - 왼쪽 / + 오른쪽 (구슬 반지름 대비)"), o.x, -1.5f, 1.5f);
            o.y = EditorGUILayout.Slider(new GUIContent("세로 위치", "구슬 중심 기준. - 아래 / + 위 (구슬 반지름 대비)"), o.y, -1.5f, 1.5f);
            off.vector2Value = o;
            Rel(layer, "radius", "크기", "1 = 구슬과 같은 크기. 이 원 안쪽이 밝게 남는다");
            Rel(layer, "color", "바깥 덮는 색", "원 바깥을 덮는 색. 보통 검정");
            Rel(layer, "alpha", "바깥 어둡기", "원 바깥을 얼마나 어둡게 덮을지. 0 = 효과 없음, 1 = 완전히 덮음");
            Rel(layer, "parallax", "이동량", "마우스에 따라 움직이는 최대 거리. 클수록 앞에 있는 느낌");
        }
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

    private static void Rel(SerializedProperty parent, string name, string label, string tip)
    {
        var p = parent.FindPropertyRelative(name);
        if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label, tip));
    }

    /// <summary>-1~1 2D 패드. 드래그로 값 지정.</summary>
    private static Vector2 DrawLookPad(Rect r, Vector2 value)
    {
        EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.12f));
        Handles.color = new Color(1, 1, 1, 0.15f);
        Handles.DrawLine(new Vector2(r.center.x, r.yMin), new Vector2(r.center.x, r.yMax));
        Handles.DrawLine(new Vector2(r.xMin, r.center.y), new Vector2(r.xMax, r.center.y));
        Handles.color = new Color(1, 1, 1, 0.3f);
        Handles.DrawWireDisc(r.center, Vector3.forward, r.width * 0.5f - 1);

        int id = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; value = PadValue(r, e.mousePosition); GUI.changed = true; e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id) { value = PadValue(r, e.mousePosition); GUI.changed = true; e.Use(); }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
        }

        Vector2 dot = new Vector2(r.center.x + value.x * r.width * 0.5f, r.center.y - value.y * r.height * 0.5f);
        EditorGUI.DrawRect(new Rect(dot.x - 4, dot.y - 4, 8, 8), new Color(1f, 0.8f, 0.3f));
        return value;
    }

    private static Vector2 PadValue(Rect r, Vector2 mouse)
    {
        Vector2 v = new Vector2((mouse.x - r.center.x) / (r.width * 0.5f), -(mouse.y - r.center.y) / (r.height * 0.5f));
        return Vector2.ClampMagnitude(v, 1f);
    }
}

using static KoreanEditorText;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public sealed class SunyuniePanel : EditorWindow
{
    private static readonly string[] Pages = { "화면·이동", "던전", "상태이상", "연출", "UI 소리", "글자 모션", "분위기", "GPU 입자", "피해 숫자", "아트" };
    private static readonly string[] AssetsByPage =
    {
        "Assets/SOData/Map/MapGenerationData.asset",
        "Assets/Resources/Settings/StatusBalance.asset",
        "Assets/Resources/FeedbackCatalog.asset",
        "Assets/Resources/UI/UISoundSettings.asset",
        "Assets/Resources/UI/TextMotion.asset",
        "Assets/Resources/WorldAtmosphere/Profiles.asset",
        "Assets/Resources/WorldAtmosphere/Particles.asset",
        "Assets/Resources/UI/CombatText.asset"
    };

    private readonly TextMotionPreview textPreview = new();
    private SerializedObject settings;
    private Editor assetEditor;
    private int page;
    private int combatStyle;
    private Vector2 scroll;

    [MenuItem("Tools/Sunyunie Panel", priority = 0)]
    private static void Open()
    {
        var window = GetWindow<SunyuniePanel>("Sunyunie Panel");
        window.minSize = new Vector2(460f, 300f);
        window.Show();
    }

    private void OnDisable()
    {
        if(page==6 && WorldAtmosphere.Current!=null) WorldAtmosphere.Current.Preview=WorldAtmosphere.View.Composite;
        if (assetEditor != null) DestroyImmediate(assetEditor);
    }

    private void OnInspectorUpdate()
    {
        if (EditorApplication.isPlaying || page == 5 || page == Pages.Length - 1) Repaint();
    }

    private void OnGUI()
    {
        using var propertyFonts = new PropertyFontScope(EditorStyles.label, EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!WorkshopBuild.CanOpenInPlayMode()))
        {
            if (Button(EditorApplication.isPlaying ? "현재 플레이에서 제작실로 이동" : "제작실로 플레이 시작"))
                WorkshopBuild.OpenInPlayMode();
        }
        int selected = GUILayout.SelectionGrid(page, Pages, 3, Style(GUI.skin.button));
        if (selected != page)
        {
            if(page==6 && WorldAtmosphere.Current!=null) WorldAtmosphere.Current.Preview=WorldAtmosphere.View.Composite;
            page = selected;
            scroll = Vector2.zero;
        }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (page == 0) DrawRendering();
        else if (page == Pages.Length - 1) DrawArt();
        else
        {
            if (page == 5) textPreview.Draw(TextMotionLibrary.Instance);
            if (page == 6 && LightingDebugPanel.Instance != null && Button("인게임 조명 디버그 열기 · F8"))
                LightingDebugPanel.Instance.SetOpen(true);
            if (page == 6 && Button("절벽 높이와 밑단 설정"))
                Selection.activeObject = Resources.Load<TerrainReliefProfile>("WorldAtmosphere/TerrainRelief");
            if (page == 6 && WorldAtmosphere.Current != null)
            {
                var atmosphere = WorldAtmosphere.Current;
                Label("현재 방 프로필",atmosphere.ProfileSource);
                atmosphere.Preview=(WorldAtmosphere.View)EditorGUILayout.EnumPopup("월드 패스 미리보기",atmosphere.Preview);
            }
            if (page == 7 && WorldSparkField.Current != null)
            {
                var field=WorldSparkField.Current;
                Label("백엔드", field.UsesCompute ? "Compute" : "Vertex");
                Label("요청 / 실제 / 예산 생략 / 시야 밖", $"{field.Requested} / {field.Emitted} / {field.BudgetDrops} / {field.Offscreen}");
                Label("면적 제한 생략 / 살아 있는 입자", $"{field.AreaDrops} / {field.ActiveSlots}");
                Label("CPU 충돌 쿼리", field.CollisionQueries.ToString());
            }
            if (page == 8) DrawCombatTextPreview();
            DrawSharedAsset(AssetsByPage[page - 1]);
        }
        EditorGUILayout.EndScrollView();
    }

    private bool Button(string caption) => GUILayout.Button(caption, Style(GUI.skin.button));

    private void Label(string caption, GUIStyle style) => GUILayout.Label(caption, Style(style));

    private void Label(string caption, string value)
    {
        var row = EditorGUILayout.GetControlRect();
        var labelStyle = Style(EditorStyles.label);
        var valueRect = EditorGUI.PrefixLabel(row, new GUIContent(caption), labelStyle);
        GUI.Label(valueRect, value, labelStyle);
    }

    private void DrawCombatTextPreview()
    {
        var library = AssetDatabase.LoadAssetAtPath<CombatTextLibrary>(AssetsByPage[7]);
        if (library == null || library.styles == null || library.styles.Length == 0) return;
        string[] names = System.Array.ConvertAll(library.styles, style => style?.id ?? "—");
        combatStyle = EditorGUILayout.Popup("미리 볼 연출", Mathf.Clamp(combatStyle, 0, names.Length - 1), names);
        var style = library.styles[combatStyle];
        var player = Application.isPlaying && GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        using (new EditorGUI.DisabledScope(player == null || FloatingTextManager.Instance == null || style == null))
        {
            if (Button("플레이어 위에서 피해 숫자 미리보기"))
            {
                var anchor = player.GetComponentInChildren<FloatingTextSpawner>();
                var point = anchor != null ? anchor.FloatPoint.position : player.transform.position;
                FloatingTextManager.Instance.ShowDamage(style.id == "Miss" ? "MISS" : "128", Color.white, point,
                    style.id == "Critical", style.id);
            }
        }
    }

    private void DrawRendering()
    {
        var cameraSettings = CameraSettingsSO.Instance;
        if (cameraSettings == null)
        {
            Help("Resources/CameraSettings.asset을 찾을 수 없습니다.", MessageType.Error);
            return;
        }
        if (settings == null || settings.targetObject != cameraSettings)
            settings = new SerializedObject(cameraSettings);

        settings.Update();
        Label("화면", EditorStyles.boldLabel);
        Label("내부 해상도", $"{PixelViewport.Width} × {PixelViewport.Height}");
        Vector2Int output = PixelArtUpscaleRig.OutputSize;
        Label("출력 크기", output.x > 0 ? $"{output.x} × {output.y}" : "Play Mode에서 표시");
        Label("UI 폰트", "논리 해상도에 맞춰 구운 Point 비트맵");
        var ppu = settings.FindProperty(nameof(CameraSettingsSO.assetsPPU));
        EditorGUILayout.PropertyField(ppu, new GUIContent("아트 원본 PPU"));
        var lens = settings.FindProperty(nameof(CameraSettingsSO.orthographicSize));
        EditorGUILayout.PropertyField(lens, new GUIContent("카메라 Ortho Size"));
        if (Button("아트 1픽셀을 화면 1픽셀에 맞추기"))
            lens.floatValue = PixelViewport.Height / (2f * Mathf.Max(1, ppu.intValue));

        EditorGUILayout.Space();
        Label("캐릭터 표시 이동", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.edgeMistOpacity)), new GUIContent("외곽 안개 농도"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.edgeMistWidth)), new GUIContent("외곽 안개 폭 (논리 px)"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.edgeMistSpeed)), new GUIContent("외곽 안개 속도"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.mistStrength)), new GUIContent("지면 안개"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.bloomStrength)), new GUIContent("월드 빛 번짐"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.playerPoseStrength)), new GUIContent("플레이어 몸 반동"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.enemyPoseStrength)), new GUIContent("적 몸 반동"));
        if (Button("UI 대기 주기와 강도 · 스킨 설정")) Selection.activeObject = Resources.Load<UISkinSettings>(UISkinSettings.ResourcePath);
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.singleEnemyContactRadius)), new GUIContent("밀착 조준 거리 (유닛)"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.contactKnockbackBonus)), new GUIContent("밀착 추가 넉백 비율"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.enemyRecoilPixels)), new GUIContent("적 피격 반동 (픽셀)"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.hudMotionStrength)), new GUIContent("HUD 반응 강도"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.grazeDuration)), new GUIContent("아슬아슬한 회피 감속 시간"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.grazeTimeScale)), new GUIContent("회피 중 시간 배율"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.grazeMargin)), new GUIContent("회피 감지 거리 (월드 단위)"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.finalBlowDuration)), new GUIContent("마지막 적 처치 감속 시간"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.finalBlowTimeScale)), new GUIContent("마지막 적 처치 시간 배율"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.grazeCooldown)), new GUIContent("회피 연출 재사용 시간"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.occlusionMistSize)), new GUIContent("플레이어 가림 안개 반경"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.groundShadowStrength)), new GUIContent("바닥 그림자 농도"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.groundShadowAngle)), new GUIContent("기본 그림자 방향 (°)"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.groundShadowLength)), new GUIContent("기본 그림자 길이"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.canopyWindPixels)), new GUIContent("나뭇잎 바람 (px)"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.cameraMotion)), new GUIContent("카메라"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.playerMotion)), new GUIContent("플레이어"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.enemyMotion)), new GUIContent("적과 보스"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.summonMotion)), new GUIContent("동행 소환수"));
        Help("이동 단위는 표시 위치에 적용됩니다. 물리, 충돌, 파티클 위치는 소수점으로 계속 계산합니다.", MessageType.None);
        EditorGUILayout.Space();
        Label("벡터 이펙트 (논리 픽셀)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.playerLocatorPixels)), new GUIContent("플레이어 발밑 표시 크기"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.enemyLocatorPixels)), new GUIContent("적 발밑 표시 크기"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.offscreenArrowPixels)), new GUIContent("화면 밖 화살표 크기"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.offscreenArrowInsetPixels)), new GUIContent("화살표 화면 안쪽 여백"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.vectorIndicatorStrokePixels)), new GUIContent("방향 표시 외곽선"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.vectorIndicatorStroke)), new GUIContent("방향 표시 외곽선 색"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.vectorTelegraphStrokePixels)), new GUIContent("공격 전조 외곽선"));
        Label("연속 명중 줌", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.comboZoomPixelsPerHit)), new GUIContent("명중당 화면 상하 축소량 (px)"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.comboZoomMaxPixels)), new GUIContent("한쪽 최대 축소량 (px)"));
        EditorGUILayout.PropertyField(settings.FindProperty(nameof(CameraSettingsSO.comboZoomHalfLife)), new GUIContent("잔여 줌이 절반이 되는 시간 (초)"));
        settings.ApplyModifiedProperties();

        EditorGUILayout.Space();
        Label("플레이어 이동과 근접 공격", EditorStyles.boldLabel);
        var player = EditorApplication.isPlaying && GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (player != null)
            editPlayerDefaults = GUILayout.Toolbar(editPlayerDefaults ? 1 : 0, PlayerTargets, Style(GUI.skin.button)) == 1;
        if (player != null && !editPlayerDefaults)
        {
            DrawPlayerControls(player, ref playerSettings);
            DrawPlayerControls(player.GetComponent<MeleeCombatController>(), ref meleeSettings);
        }
        else DrawPlayerDefaults();
    }

    private SerializedObject playerSettings, meleeSettings;
    private bool editPlayerDefaults;
    private static readonly string[] PlayerTargets = { "현재 플레이어 · 시험값", "기본값 · DATA" };
    // DATA BALANCE/PLAYER의 파일과 키. 플레이어가 생성될 때 ProjectPlayerTuning이 프리팹 값을 이 값으로 덮어쓴다.
    private static readonly (string file, string field, string caption)[] PlayerFields =
    {
        ("movement.json", "clearedRoomSprint", "완료한 방 질주 배율"),
        ("movement.json", "hurtPushDistance", "피격 밀림 거리"),
        ("melee.json", "aimAssistDegrees", "조준 보정 최대 각도"),
        ("melee.json", "reachAssistDistance", "공격 거리 보정 한도"),
        ("melee.json", "attackWalkSpeedMultiplier", "공격 중 걷기 배율")
    };

    private void DrawPlayerControls(Component component, ref SerializedObject data)
    {
        if (component == null) return;
        if (data == null || data.targetObject != component) data = new SerializedObject(component);
        data.Update();
        foreach (var (_, field, caption) in PlayerFields)
        {
            var property = data.FindProperty(field);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(caption));
        }
        if (data.ApplyModifiedProperties()) EditorUtility.SetDirty(component);
    }

    private void DrawPlayerDefaults()
    {
        Help("플레이어가 생성될 때 DATA BALANCE/PLAYER 값이 프리팹 기본값을 덮어씁니다. 제작실 > 게임 콘텐츠 > 플레이어에서 편집하세요.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!WorkshopBuild.CanOpenInPlayMode()))
        {
            if (Button("제작실에서 플레이어 수치 편집")) WorkshopBuild.OpenInPlayMode();
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player Melee.prefab");
        if (prefab == null) return;
        var authored = new[] { new SerializedObject(prefab.GetComponent<PlayerController>()), new SerializedObject(prefab.GetComponent<MeleeCombatController>()) };
        var files = new Dictionary<string, JObject>();
        foreach (var (file, field, caption) in PlayerFields)
        {
            if (!files.TryGetValue(file, out var json)) files[file] = json = ReadPlayerData(file);
            var property = authored[0].FindProperty(field) ?? authored[1].FindProperty(field);
            string data = json?[field] is JValue { Type: JTokenType.Float or JTokenType.Integer } value ? ((float)value).ToString("0.###") : "없음";
            Label(caption, $"DATA {data} · 프리팹 {property?.floatValue.ToString("0.###")}");
        }
    }

    private static JObject ReadPlayerData(string file)
    {
        try { return JObject.Parse(File.ReadAllText(Path.Combine(ProjectContentStore.Root, "BALANCE", "PLAYER", file))); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void DrawSharedAsset(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        if (asset == null)
        {
            Help($"설정 에셋을 찾을 수 없습니다. {path}", MessageType.Error);
            return;
        }

        Editor.CreateCachedEditor(asset, null, ref assetEditor);
        assetEditor.OnInspectorGUI();
    }

    private void DrawArt()
    {
        Label("ARTIST 원본", EditorStyles.boldLabel);
        Label("동기화 대상", ArtistSourceSync.SourceCount.ToString());
        Label("대기 중", ArtistSourceSync.PendingCount.ToString());
        Help(ArtistSourceSync.Status, MessageType.Info);
        if (Button("도트 밀도와 배치 검사")) PixelDensityAudit.Show();
        if (Button("선택한 그림의 편집 원본 찾기")) ArtistSourceSync.RevealSelection();
        if (Button("ARTIST 폴더 열기")) ArtistSourceSync.OpenFolder();
        if (Button("모든 원본의 변경 확인")) ArtistSourceSync.CheckAll();
        if (Button("Aseprite 실행 파일 지정")) ArtistSourceSync.SelectAseprite();
        EditorGUILayout.Space();
        Label("벡터 UI 원본", EditorStyles.boldLabel);
        Help(VectorUiSourceSync.Status, MessageType.Info);
        if (Button("벡터 UI 다시 빌드")) VectorUiSourceSync.Schedule();
    }
}

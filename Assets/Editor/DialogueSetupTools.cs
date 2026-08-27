using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 대화 시스템 셋업 도구.
///
/// 1) 대화 UI 프리팹 만들기 — 계층/앵커/배선을 전부 코드로 만든다.
///    손으로 만들면 앵커 하나 어긋나도 티가 안 나고, 슬롯 5칸 × 3컴포넌트를
///    인스펙터로 끌어 넣다가 하나 빠뜨리면 런타임에 조용히 아무 일도 안 일어난다.
/// 2) 대사 테이블 검증 — CSV 파싱 결과와 흔한 저작 실수를 리포트로 뽑는다.
///
/// BoneMaster 툴과 같은 방식(메뉴 → Temp/*.txt 리포트)이다.
/// </summary>
public static class DialogueSetupTools
{
    private const string PREFAB_DIR  = "Assets/Prefabs/UI/Dialogue";
    private const string PREFAB_PATH = PREFAB_DIR + "/DialogueUI.prefab";
    private const string SO_DIR      = "Assets/SOData/Dialogue";
    private const string TABLE_PATH  = SO_DIR + "/DialogueTable.asset";
    private const string CAST_PATH   = SO_DIR + "/DialogueCast.asset";
    private const string CSV_PATH    = "Assets/Resources/Dialogue/dialogue_sample.csv";

    // 960x540 기준. 하단 1/3 이 대사창, 상단 2/3 가 초상화.
    private const float TEXTBOX_RATIO = 1f / 3f;

    /// <summary>칸의 가로 위치(화면 폭 비율). 순서는 화면에 보이는 대로 왼쪽 / 가운데 / 오른쪽 —
    /// DialogueUI.SLOT_FILL_ORDER 의 칸 번호와 같은 순서여야 한다.
    /// 양 끝을 더 벌리거나 좁히려면 여기 두 값만 만지고 1번 메뉴를 다시 돌린다.</summary>
    private static readonly float[] SLOT_X = { 0.22f, 0.5f, 0.78f };
    private static readonly string[] SLOT_NAMES = { "Slot0_Left", "Slot1_Center", "Slot2_Right" };

    /// <summary>칸 하나의 가로 폭(960 기준 픽셀). 양 끝(0.22)과 가운데(0.5) 간격이 269 라
    /// 이 값이 그보다 크면 초상화끼리 겹친다.</summary>
    private const float SLOT_W = 260f;
    private const float REF_W = 960f;
    private const float REF_H = 540f;

    // 샘플 CSV 가 쓰는 캐릭터 키들. 초상화 없이도 플레이스홀더로 보이게 미리 채워둔다.
    private static readonly (string key, string name, Color color)[] SAMPLE_CAST =
    {
        ("player",     "네크로맨서", new Color(0.72f, 0.86f, 1f)),
        ("bonemaster", "본 마스터",  new Color(1f, 0.55f, 0.45f)),
        ("merchant",   "상인",       new Color(1f, 0.90f, 0.55f)),
        ("enhancer",   "대장장이",   new Color(0.80f, 0.95f, 0.70f)),
        ("ally",       "얼음 마법사", new Color(0.70f, 0.90f, 1f)),
    };

    // ══════════════════════════════════════════════════════════════════
    [MenuItem("Tools/Dialogue/1. 대화 UI 프리팹 만들기")]
    public static void CreatePrefab()
    {
        var table = EnsureTable();
        var cast  = EnsureCast();

        GameObject root = BuildHierarchy(out DialogueUI ui, out List<GameObject> slotRoots);
        WireUI(ui, table, cast, root, slotRoots);

        Directory.CreateDirectory(PREFAB_DIR);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);

        Debug.Log($"<color=cyan>[DialogueSetup]</color> 프리팹 생성 완료: {PREFAB_PATH}\n" +
                  "씬의 Canvas 밑에 끌어다 놓고 비활성으로 저장하면 된다. " +
                  "(자기 Canvas 를 만들지 않으므로 씬 CanvasScaler 를 그대로 상속한다)");
    }

    // ── 계층 ───────────────────────────────────────────────────────
    private static GameObject BuildHierarchy(out DialogueUI ui, out List<GameObject> slotRoots)
    {
        GameObject root = NewRect("DialogueUI", null, stretch: true);
        ui = root.AddComponent<DialogueUI>();

        // 중첩 Canvas 한 겹. 씬 Canvas 가 m_PixelPerfect:1 이라 초상화 확대/축소가
        // 픽셀에 스냅돼 계단처럼 튄다. 여기서만 끈다. CanvasScaler 는 부모 것을 그대로 상속한다.
        GameObject canvasGo = NewRect("Canvas (pixelPerfect off)", root.transform, stretch: true);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.overrideSorting        = true;
        canvas.sortingOrder           = 200;   // 씬 Canvas 는 1, 페이드 커튼은 999
        canvas.overridePixelPerfect   = true;
        canvas.pixelPerfect           = false;

        GameObject panel = NewRect("Panel", canvasGo.transform, stretch: true);

        // ── 초상화 (상단 2/3) ──
        GameObject portraits = NewRect("Portraits", panel.transform, stretch: false);
        var pr = portraits.GetComponent<RectTransform>();
        pr.anchorMin = new Vector2(0f, TEXTBOX_RATIO);
        pr.anchorMax = new Vector2(1f, 1f);
        pr.offsetMin = Vector2.zero;
        pr.offsetMax = Vector2.zero;

        slotRoots = new List<GameObject>();
        float slotH = REF_H * (1f - TEXTBOX_RATIO);

        for (int i = 0; i < DialogueUI.SLOT_COUNT; i++)
        {
            GameObject slot = NewRect($"{SLOT_NAMES[i]}", portraits.transform, stretch: false);
            var sr = slot.GetComponent<RectTransform>();
            // 세로는 바닥. 피벗이 바닥이라 확대해도 발밑이 안 뜨고 위로만 자란다.
            sr.anchorMin = sr.anchorMax = new Vector2(SLOT_X[i], 0f);
            sr.pivot     = new Vector2(0.5f, 0f);
            sr.sizeDelta = new Vector2(SLOT_W, slotH);
            sr.anchoredPosition = Vector2.zero;

            // 초상화. 스프라이트가 null 이면 Image 가 단색 사각형을 그리는데,
            // 그게 그대로 '아직 그림 없음' 플레이스홀더가 된다.
            GameObject imgGo = NewRect("Portrait", slot.transform, stretch: true);
            var img = imgGo.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget  = false;

            GameObject labelGo = NewRect("PlaceholderName", slot.transform, stretch: true);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text          = "";
            label.fontSize      = 16;
            label.alignment     = TextAlignmentOptions.Center;
            label.color         = new Color(1f, 1f, 1f, 0.85f);
            label.raycastTarget = false;

            slotRoots.Add(slot);
        }

        // ── 대사창 (하단 1/3) ──
        GameObject textBox = NewRect("TextBox", panel.transform, stretch: false);
        var tr = textBox.GetComponent<RectTransform>();
        tr.anchorMin = new Vector2(0f, 0f);
        tr.anchorMax = new Vector2(1f, TEXTBOX_RATIO);
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;

        GameObject bg = NewRect("Background", textBox.transform, stretch: true);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.offsetMin = new Vector2(16f, 12f);
        bgRect.offsetMax = new Vector2(-16f, -12f);
        var bgImg = bg.AddComponent<Image>();
        // 단색 검정. 9-slice 패널 스프라이트(UI_New.png)를 나중에 여기 끼우면 톤이 맞는다.
        bgImg.color         = new Color(0.04f, 0.04f, 0.07f, 0.92f);
        bgImg.raycastTarget = false;

        GameObject nameBox = NewRect("NameBox", bg.transform, stretch: false);
        var nbRect = nameBox.GetComponent<RectTransform>();
        nbRect.anchorMin = nbRect.anchorMax = new Vector2(0f, 1f);
        nbRect.pivot     = new Vector2(0f, 1f);
        nbRect.sizeDelta = new Vector2(220f, 34f);
        nbRect.anchoredPosition = new Vector2(14f, 14f);   // 패널 위쪽으로 살짝 걸치게
        var nbImg = nameBox.AddComponent<Image>();
        nbImg.color         = new Color(0.10f, 0.10f, 0.16f, 0.98f);
        nbImg.raycastTarget = false;

        GameObject nameGo = NewRect("NameText", nameBox.transform, stretch: true);
        var nameText = nameGo.AddComponent<TextMeshProUGUI>();
        nameText.text          = "이름";
        nameText.fontSize      = 18;
        nameText.alignment     = TextAlignmentOptions.Center;
        nameText.raycastTarget = false;

        GameObject bodyGo = NewRect("BodyText", bg.transform, stretch: true);
        var bodyRect = bodyGo.GetComponent<RectTransform>();
        bodyRect.offsetMin = new Vector2(24f, 20f);
        bodyRect.offsetMax = new Vector2(-24f, -22f);
        var bodyText = bodyGo.AddComponent<TextMeshProUGUI>();
        bodyText.text          = "";
        bodyText.fontSize      = 18;
        bodyText.alignment     = TextAlignmentOptions.TopLeft;
        bodyText.raycastTarget = false;
        bodyText.enableWordWrapping = true;
        var effect = bodyGo.AddComponent<TMPTextEffectPlayer>();

        GameObject arrow = NewRect("NextArrow", bg.transform, stretch: false);
        var arRect = arrow.GetComponent<RectTransform>();
        arRect.anchorMin = arRect.anchorMax = new Vector2(1f, 0f);
        arRect.pivot     = new Vector2(1f, 0f);
        arRect.sizeDelta = new Vector2(28f, 24f);
        arRect.anchoredPosition = new Vector2(-14f, 8f);
        var arText = arrow.AddComponent<TextMeshProUGUI>();
        arText.text          = "▼";
        arText.fontSize      = 16;
        arText.alignment     = TextAlignmentOptions.Center;
        arText.raycastTarget = false;

        // 프리팹 기본은 꺼진 상태. 켜둔 채로 저장하면 에디터에서 대사창이 항상 화면을 가려
        // 다른 UI 작업이 불편해진다. 레이아웃을 볼 땐 4번 메뉴로 잠깐 켠다.
        panel.SetActive(false);

        // 참조를 넘기려고 잠깐 이름표를 붙여둔다.
        s_panel    = panel;
        s_nameBox  = nameBox;
        s_nameText = nameText;
        s_bodyText = bodyText;
        s_effect   = effect;
        s_arrow    = arrow;

        return root;
    }

    private static GameObject s_panel, s_nameBox, s_arrow;
    private static TextMeshProUGUI s_nameText, s_bodyText;
    private static TMPTextEffectPlayer s_effect;

    // ── 배선 ───────────────────────────────────────────────────────
    private static void WireUI(DialogueUI ui, DialogueTableSO table, DialogueCastSO cast,
                               GameObject root, List<GameObject> slotRoots)
    {
        var so = new SerializedObject(ui);
        so.FindProperty("table").objectReferenceValue      = table;
        so.FindProperty("cast").objectReferenceValue       = cast;
        so.FindProperty("panel").objectReferenceValue      = s_panel;
        so.FindProperty("nameBox").objectReferenceValue    = s_nameBox;
        so.FindProperty("nameText").objectReferenceValue   = s_nameText;
        so.FindProperty("bodyText").objectReferenceValue   = s_bodyText;
        so.FindProperty("bodyEffect").objectReferenceValue = s_effect;
        so.FindProperty("nextArrow").objectReferenceValue  = s_arrow;

        var slotsProp = so.FindProperty("slots");
        slotsProp.arraySize = slotRoots.Count;
        for (int i = 0; i < slotRoots.Count; i++)
        {
            var element = slotsProp.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("root").objectReferenceValue =
                slotRoots[i].GetComponent<RectTransform>();
            element.FindPropertyRelative("image").objectReferenceValue =
                slotRoots[i].transform.Find("Portrait").GetComponent<Image>();
            element.FindPropertyRelative("placeholderLabel").objectReferenceValue =
                slotRoots[i].transform.Find("PlaceholderName").GetComponent<TextMeshProUGUI>();
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject NewRect(string name, Transform parent, bool stretch)
    {
        var go = new GameObject(name, typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        if (stretch)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        return go;
    }

    // ── 에셋 ───────────────────────────────────────────────────────
    private static DialogueTableSO EnsureTable()
    {
        var existing = AssetDatabase.LoadAssetAtPath<DialogueTableSO>(TABLE_PATH);
        if (existing != null) return existing;

        Directory.CreateDirectory(SO_DIR);
        var table = ScriptableObject.CreateInstance<DialogueTableSO>();

        var csv = AssetDatabase.LoadAssetAtPath<TextAsset>(CSV_PATH);
        if (csv != null)
        {
            var so = new SerializedObject(table);
            var arr = so.FindProperty("csvFiles");
            arr.arraySize = 1;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = csv;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning($"<color=orange>[DialogueSetup]</color> {CSV_PATH} 를 못 찾았다. " +
                             "테이블에 CSV 를 직접 넣어야 한다.");
        }

        AssetDatabase.CreateAsset(table, TABLE_PATH);
        return table;
    }

    private static DialogueCastSO EnsureCast()
    {
        var existing = AssetDatabase.LoadAssetAtPath<DialogueCastSO>(CAST_PATH);
        if (existing != null) return existing;

        Directory.CreateDirectory(SO_DIR);
        var cast = ScriptableObject.CreateInstance<DialogueCastSO>();

        var so = new SerializedObject(cast);
        var entries = so.FindProperty("entries");
        entries.arraySize = SAMPLE_CAST.Length;
        for (int i = 0; i < SAMPLE_CAST.Length; i++)
        {
            var e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("key").stringValue         = SAMPLE_CAST[i].key;
            e.FindPropertyRelative("displayName").stringValue = SAMPLE_CAST[i].name;
            e.FindPropertyRelative("nameColor").colorValue    = SAMPLE_CAST[i].color;
            e.FindPropertyRelative("portraits").arraySize     = 0;   // 아트가 생기면 여기 채운다
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(cast, CAST_PATH);
        return cast;
    }

    // ══════════════════════════════════════════════════════════════════
    [MenuItem("Tools/Dialogue/2. 대사 테이블 검증")]
    public static void Validate()
    {
        var table = AssetDatabase.LoadAssetAtPath<DialogueTableSO>(TABLE_PATH);
        var cast  = AssetDatabase.LoadAssetAtPath<DialogueCastSO>(CAST_PATH);

        var sb = new StringBuilder();
        int problems = 0;

        if (table == null)
        {
            Debug.LogError($"<color=orange>[DialogueSetup]</color> {TABLE_PATH} 가 없다. 1번 메뉴부터 실행할 것.");
            return;
        }

        table.Invalidate();

        var ids = new List<string>(table.AllIds);
        ids.Sort();
        sb.AppendLine($"대화 {ids.Count} 개");
        sb.AppendLine(new string('-', 60));

        foreach (var id in ids)
        {
            var lines = table.Get(id);
            // 첫 줄을 같이 찍는다. 어느 대화인지 알아보기도 쉽고,
            // 따옴표 안의 쉼표가 제대로 한 칸으로 붙었는지 여기서 눈으로 확인된다.
            sb.AppendLine($"[{id}]  {lines.Count} 줄   | {lines[0].text}");

            var stage = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                string where = $"  {id} {i + 1}번째 줄";

                if (line.cast != null)
                {
                    stage.Clear();

                    // @자리 오타는 조용히 자동 배치로 떨어져서 눈으로는 절대 못 잡는다. 여기서 잡는다.
                    var claimed = new HashSet<int>();
                    foreach (var c in line.cast)
                    {
                        DialogueCastSO.SplitKey(c, out string ck, out _, out string slotName);
                        if (!string.IsNullOrEmpty(ck)) stage.Add(ck);
                        if (string.IsNullOrEmpty(slotName)) continue;

                        int idx = DialogueUI.SlotIndexOf(slotName);
                        if (idx < 0)
                        {
                            sb.AppendLine($"NG{where}: '{ck}' 의 자리 '{slotName}' 을 모르겠다 — 왼쪽/가운데/오른쪽 (left/center/right).");
                            problems++;
                        }
                        else if (!claimed.Add(idx))
                        {
                            sb.AppendLine($"NG{where}: 자리 '{slotName}' 을 둘 이상이 찍었다 — 뒤엣놈이 남는 칸으로 밀려난다.");
                            problems++;
                        }
                    }
                }

                if (i == 0 && line.cast == null)
                {
                    sb.AppendLine($"NG{where}: 첫 줄인데 cast 가 비었다 — 무대가 텅 빈 채로 시작한다.");
                    problems++;
                }

                if (stage.Count > DialogueUI.SLOT_COUNT)
                {
                    sb.AppendLine($"NG{where}: 무대에 {stage.Count} 명 — 슬롯은 {DialogueUI.SLOT_COUNT} 칸뿐이라 뒤가 잘린다.");
                    problems++;
                }

                if (string.IsNullOrEmpty(line.text))
                {
                    sb.AppendLine($"NG{where}: 대사가 비었다.");
                    problems++;
                }

                DialogueCastSO.SplitKey(line.speaker, out string sk, out string sexpr);
                if (!string.IsNullOrEmpty(sk))
                {
                    // 제일 흔한 저작 실수. 무대에 없는 캐릭터가 말하면 아무도 강조되지 않는다.
                    if (!stage.Contains(sk))
                    {
                        sb.AppendLine($"NG{where}: 화자 '{sk}' 가 무대(cast)에 없다 — 아무도 강조되지 않는다.");
                        problems++;
                    }
                    if (cast != null && cast.GetEntry(sk) == null)
                    {
                        sb.AppendLine($"NG{where}: 화자 키 '{sk}' 가 DialogueCast 명부에 없다.");
                        problems++;
                    }
                    if (cast != null && !string.IsNullOrEmpty(sexpr) &&
                        cast.GetEntry(sk) != null && cast.GetPortrait(sk, sexpr) == null)
                    {
                        sb.AppendLine($"주의{where}: '{sk}' 의 표정 '{sexpr}' 초상화가 없다 — 플레이스홀더로 뜬다.");
                    }
                }

                foreach (var key in stage)
                {
                    if (cast != null && cast.GetEntry(key) == null)
                    {
                        sb.AppendLine($"NG{where}: cast 키 '{key}' 가 DialogueCast 명부에 없다.");
                        problems++;
                    }
                }
            }
        }

        sb.AppendLine(new string('-', 60));
        sb.AppendLine(problems == 0 ? "문제 없음" : $"문제 {problems} 건");

        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/Dialogue_Validate.txt", sb.ToString(), Encoding.UTF8);

        if (problems == 0)
            Debug.Log($"<color=cyan>[DialogueSetup]</color> 검증 통과 — 대화 {ids.Count} 개.\n{sb}");
        else
            Debug.LogError($"<color=orange>[DialogueSetup]</color> 문제 {problems} 건.\n{sb}");
    }

    // ══════════════════════════════════════════════════════════════════
    /// <summary>
    /// 대화 UI 프리팹을 씬들의 Canvas 밑에 하나씩 넣는다.
    ///
    /// 이 프로젝트의 UI 는 런타임 Instantiate 가 아니라 씬에 미리 박아두는 관행이라
    /// (Resources 자동 생성 전례가 GroundItem 하나뿐이다) 씬에 없으면
    /// DialogueUI.Instance 가 영원히 null 이다. 그래서 배치까지가 셋업이다.
    ///
    /// 루트는 켠 채로 둔다 — Awake 가 돌아야 Instance 가 잡히고, 대사창 자체는
    /// OnAwake 에서 panel 을 꺼주므로 화면에는 아무것도 안 보인다.
    /// </summary>
    [MenuItem("Tools/Dialogue/3. 씬에 배치")]
    public static void PlaceInScenes()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"<color=orange>[DialogueSetup]</color> {PREFAB_PATH} 가 없다. 1번 메뉴부터 실행할 것.");
            return;
        }

        string opened = EditorSceneManager.GetActiveScene().path;
        if (!string.IsNullOrEmpty(opened) &&
            !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;   // 사용자가 저장을 취소했으면 씬을 갈아엎지 않는다
        }

        var sb = new StringBuilder();
        foreach (string path in TARGET_SCENES)
        {
            if (!File.Exists(path)) { sb.AppendLine($"건너뜀 {path} (없음)"); continue; }

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            Canvas canvas = null;
            DialogueUI already = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (already == null) already = go.GetComponentInChildren<DialogueUI>(true);
                if (canvas == null)
                {
                    foreach (var c in go.GetComponentsInChildren<Canvas>(true))
                    {
                        // 페이드 커튼(order 999, DontDestroyOnLoad)이 아니라 씬 본 캔버스를 찾는다.
                        if (c.transform.parent == null && c.renderMode == RenderMode.ScreenSpaceOverlay)
                        {
                            canvas = c;
                            break;
                        }
                    }
                }
            }

            if (already != null) { sb.AppendLine($"이미 있음 {path}"); continue; }
            if (canvas == null)  { sb.AppendLine($"NG {path}: 씬 Canvas 를 못 찾았다"); continue; }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
            instance.transform.SetAsLastSibling();   // 형제 순서 = 그리기 순서. 맨 위로.

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine($"배치 {path}  ->  {canvas.name}");
        }

        if (!string.IsNullOrEmpty(opened) && File.Exists(opened))
        {
            EditorSceneManager.OpenScene(opened, OpenSceneMode.Single);
        }

        Debug.Log($"<color=cyan>[DialogueSetup]</color> 씬 배치 결과\n{sb}");
    }

    private static readonly string[] TARGET_SCENES =
    {
        "Assets/Scenes/BattleScene.unity",
        "Assets/Scenes/VillageScene.unity",
        "Assets/Scenes/BossTestScene.unity",
        "Assets/Scenes/EliteTestScene.unity",
    };

    // ══════════════════════════════════════════════════════════════════
    /// <summary>
    /// 지금 열려 있는 씬의 대화 UI 를 켜서 Game 뷰에서 레이아웃을 보게 한다.
    /// 플레이 모드에 들어가지 않으므로 던전 로딩을 기다릴 필요가 없다.
    /// 한 번 더 누르면 꺼진다. (끄는 걸 잊고 씬을 저장해도 DialogueUI 가
    /// Awake 에서 패널을 꺼주므로 게임에는 영향이 없다)
    /// </summary>
    [MenuItem("Tools/Dialogue/4. 레이아웃 미리보기 토글")]
    public static void TogglePreview()
    {
        var ui = Object.FindFirstObjectByType<DialogueUI>(FindObjectsInactive.Include);
        if (ui == null)
        {
            Debug.LogError("<color=orange>[DialogueSetup]</color> 지금 씬에 DialogueUI 가 없다. " +
                           "3번 메뉴로 배치했는지, 그 씬을 열었는지 확인할 것.");
            return;
        }

        bool on = ui.EditorPreviewToggle();

        // 끌 때는 프리팹 오버라이드까지 되돌린다. 미리보기가 이름칸/대사/슬롯 색을 직접
        // 건드리기 때문에, 그냥 패널만 끄면 견본 문장("본 마스터" 등)이 씬 인스턴스의
        // 오버라이드로 눌러앉는다. 런타임엔 어차피 덮어써져서 티는 안 나지만
        // 오버라이드 목록이 지저분해지고, 나중에 프리팹을 고쳐도 안 따라온다.
        if (!on && PrefabUtility.IsPartOfPrefabInstance(ui.gameObject))
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(ui.gameObject);
            PrefabUtility.RevertPrefabInstance(root, InteractionMode.AutomatedAction);
        }

        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);

        Selection.activeGameObject = ui.gameObject;
        Debug.Log($"<color=cyan>[DialogueSetup]</color> 레이아웃 미리보기 {(on ? "켬" : "끔")} — " +
                  "Game 뷰에서 확인하고, 값은 DialogueUI 인스펙터에서 바로 조정하면 된다.");
    }
}

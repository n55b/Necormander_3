using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>1006 외형 이행/검수 전용. 프리팹의 기존 데이터·이벤트 참조를 보존한다.</summary>
public static class UI1006Setup
{
    private const string Explain = "Assets/Prefabs/UI/SkillExplainUI.prefab";
    private const string Pouch = "Assets/Prefabs/UI/PouchUI.prefab";
    private const string Picker = "Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab";
    private const string Hud = "Assets/Prefabs/UI/Player State/PlayerStateUI.prefab";
    private const string Map = "Assets/Prefabs/UI/MiniMap.prefab";
    private static readonly Color Wood = new Color32(94, 41, 51, 255);
    private static readonly Color Panel = new Color32(37, 24, 31, 245);
    private static readonly Color Teal = new Color32(57, 124, 123, 255);
    private static readonly Color Inset = new Color32(22, 48, 52, 245);
    private static readonly Color Ink = new Color32(240, 233, 223, 255);
    private static readonly Color Accent = new Color32(112, 211, 202, 255);
    private static TMP_FontAsset _font;

    [MenuItem("Tools/UI/1006/Apply placeholder layout (resets layout)")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        _font = AssetDatabase.LoadAssetAtPath<GameObject>(Explain).GetComponentInChildren<TextMeshProUGUI>(true).font;
        Edit(Explain, LayoutExplain);
        Edit(Pouch, LayoutPouch);
        Edit(Picker, LayoutPicker);
        Edit(Hud, LayoutHud);
        Edit(Map, LayoutMap);
        // SaveAsPrefabAsset로 대상만 저장한다. 검수 중 갱신된 동적 폰트 등은 함께 저장하지 않는다.
        Validate();
    }

    [MenuItem("Tools/UI/1006/Apply C V interaction layout (resets C V layout)")]
    public static void ApplyCV()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        _font = AssetDatabase.LoadAssetAtPath<GameObject>(Explain).GetComponentInChildren<TextMeshProUGUI>(true).font;
        Edit(Explain, LayoutExplain);
        Edit(Pouch, LayoutPouch);
        Validate();
    }

    public static void ApplyCVBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity", OpenSceneMode.Single);
            ApplyCV(); CheckDynamic(); InventoryUICheck.CheckScenes(); InventoryUICheck.Run(); Preview();
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // C는 스탯만 먼저 표시한다. 상세 정보는 화살표로 열며 모든 배치는 프리팹에 저작한다.
    private static void LayoutExplain(GameObject root)
    {
        var panel = At(root.transform, "LoadoutPanel");
        Stretch(panel, 0);
        var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
        panelImage.enabled = false; panelImage.raycastTarget = false;
        foreach (string name in new[] { "Frame1006", "PixelFrame", "Heading", "ColumnDivider" })
            if (panel.Find(name) != null) panel.Find(name).gameObject.SetActive(false);

        var sidebar = Rect(panel, "StatsPanel", -350, 0, 220, 480);
        Frame(sidebar, 14); sidebar.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        var header = Rect(sidebar, "Summary", 0, 183, 188, 78); Paint(header, Inset);
        var summary = Text(header, "Heading", "플레이어 정보  [C]     소환수 0", 0, 24, 178, 22, 10);
        var icons = new UnityEngine.UI.Image[5];
        for (int i = 0; i < icons.Length; i++)
        {
            var backing = Rect(header, "IconSlot" + i, -74 + i * 34, -11, 30, 32);
            Paint(backing, Teal);
            icons[i] = Paint(Rect(backing, "Icon", 0, 0, 26, 28), Color.white);
            icons[i].preserveAspect = true; icons[i].enabled = false;
        }
        var oldStats = panel.Find("PlayerStats");
        if (oldStats != null) oldStats.SetParent(sidebar, false);
        var stats = Rect(sidebar, "PlayerStats", 0, -40, 188, 356);
        Paint(stats, Inset);
        if (stats.Find("Heading") != null) stats.Find("Heading").gameObject.SetActive(false);
        var body = Text(stats, "Values", "플레이어 정보 준비 중", 0, 0, 172, 344, 10);
        body.color = Accent; body.alignment = TextAlignmentOptions.TopLeft;
        body.text = "<line-height=19>플레이어 정보 준비 중";
        body.lineSpacing = 0;
        // 행별 설명은 기존 공용 툴팁을 재사용한다. 히트 영역도 에디터에서 조절할 수 있다.
        string[] names = { "체력", "가드", "회피 횟수", "물리 공격력", "마법 공격력", "방어력", "회피율", "공격 속도", "치명타 확률", "치명타 피해량", "적중률", "기본 공격 배율", "이동 속도", "스킬 쿨타임 감소", "대시 쿨타임 감소", "물리 피해 증폭", "마법 피해 증폭" };
        string[] descriptions = { "현재 체력 / 최대 체력. 체력이 0이 되면 사망합니다.", "현재 가드 게이지 / 최대 게이지. 공격을 막으면 소모됩니다.", "남은 대시 횟수 / 최대 횟수.", "기본 공격과 물리 공격의 피해 기준값입니다.", "마법 공격의 피해 기준값입니다.", "받는 피해의 감소율입니다.", "적의 공격을 회피할 확률입니다.", "초당 공격 속도입니다.", "공격이 치명타가 될 확률입니다.", "치명타로 적중했을 때 적용되는 피해 배율입니다.", "공격이 대상에게 적중할 확률입니다.", "기본 공격에 적용되는 배율입니다.", "플레이어가 이동하는 속도입니다.", "미니언 스킬의 재사용 대기시간 감소율입니다.", "대시 충전 대기시간 감소율입니다.", "물리 피해량에 적용되는 증가율입니다.", "마법 피해량에 적용되는 증가율입니다." };
        for (int i = 0; i < names.Length; i++)
        {
            int line = i < 3 ? i : i + 1;
            var row = Rect(stats, "StatHover" + i, 0, 172 - line * 19 - 9.5f, 172, 19);
            Paint(row, Color.clear).raycastTarget = true;
            var tip = row.GetComponent<SkillTooltipTrigger>() ?? row.gameObject.AddComponent<SkillTooltipTrigger>();
            var tipData = new SerializedObject(tip);
            tipData.FindProperty("title").stringValue = names[i];
            tipData.FindProperty("description").stringValue = descriptions[i];
            tipData.ApplyModifiedPropertiesWithoutUndo();
        }

        var details = Rect(panel, "DetailsPanel", 130, 0, 660, 480);
        Frame(details, 14); details.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        var detailsColor = Panel; detailsColor.a = 1f;
        details.GetComponent<UnityEngine.UI.Image>().color = detailsColor;
        // 기존 슬롯은 이동만 한다. 스크립트 참조와 fileID는 유지된다.
        foreach (string name in new[] { "Equipment", "Guard", "Minion", "SpaceSkill", "Finisher", "Dash" })
            if (panel.Find(name) != null) panel.Find(name).SetParent(details, false);
        LayoutSlot(details.Find("Equipment").GetComponent<SkillExplainSlotUI>(), -157, 90, 300, 250);
        LayoutSlot(details.Find("Guard").GetComponent<SkillExplainSlotUI>(), -157, -130, 300, 174);
        LayoutSlot(details.Find("Minion").GetComponent<SkillExplainSlotUI>(), 157, 185, 300, 62);
        LayoutSlot(details.Find("SpaceSkill").GetComponent<SkillExplainSlotUI>(), 157, 88, 300, 120);
        LayoutSlot(details.Find("Finisher").GetComponent<SkillExplainSlotUI>(), 157, -41, 300, 126);
        LayoutSlot(details.Find("Dash").GetComponent<SkillExplainSlotUI>(), 157, -165, 300, 112);

        var arrow = Rect(sidebar, "ExpandButton", 96, 182, 22, 40);
        var arrowImage = Paint(arrow, Teal); arrowImage.raycastTarget = true;
        var button = arrow.GetComponent<UnityEngine.UI.Button>() ?? arrow.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = arrowImage;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var arrowLabel = Text(arrow, "Label", ">", 0, 0, 20, 36, 20);
        var ui = root.GetComponent<SkillExplainUI>();
        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, ui.ToggleDetails);
        Set(ui, "statsPanel", sidebar); Set(ui, "detailsPanel", details); Set(ui, "expandLabel", arrowLabel);
        Set(ui, "playerStatsText", body); Set(ui, "summaryText", summary);
        var uiData = new SerializedObject(ui);
        var iconsData = uiData.FindProperty("summaryIcons"); iconsData.arraySize = icons.Length;
        for (int i = 0; i < icons.Length; i++) iconsData.GetArrayElementAtIndex(i).objectReferenceValue = icons[i];
        uiData.ApplyModifiedPropertiesWithoutUndo();
        details.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
    }

    private static void LayoutSlot(SkillExplainSlotUI slot, float x, float y, float w, float h)
    {
        var rect = (RectTransform)slot.transform;
        Place(rect, x, y, w, h);
        Paint(rect, Inset);
        var so = new SerializedObject(slot);
        var icon = (UnityEngine.UI.Image)so.FindProperty("iconImage").objectReferenceValue;
        var title = (TextMeshProUGUI)so.FindProperty("titleText").objectReferenceValue;
        var desc = (TextMeshProUGUI)so.FindProperty("descriptionText").objectReferenceValue;
        Place(icon.rectTransform, -w / 2 + 23, h / 2 - 22, 28, 28);
        icon.preserveAspect = true; icon.raycastTarget = false;
        Label(title, title.text, 18, h / 2 - 22, w - 60, 28, 14);
        title.alignment = TextAlignmentOptions.Left;
        Label(desc, desc.text, 0, -20, w - 22, Mathf.Max(0, h - 47), 12);
        desc.alignment = TextAlignmentOptions.TopLeft;
        desc.enableAutoSizing = true; desc.fontSizeMin = 10; desc.fontSizeMax = 12;
        desc.overflowMode = TextOverflowModes.Truncate;
    }

    private static void LayoutPouch(GameObject root)
    {
        var window = At(root.transform, "PouchWindow");
        Stretch(window, 0);
        var panel = At(window, "InventoryPanel");
        Place(panel, 0, 0, 360, 375); Frame(panel, 30);
        panel.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        Label(panel.Find("Heading").GetComponent<TextMeshProUGUI>(), "아이템 주머니  [V]", 0, 172.5f, 290, 23, 16);
        var inner = Rect(panel, "SlotBacking", 0, 0, 300, 315);
        Paint(inner, Inset); inner.SetSiblingIndex(1);
        for (int i = 0; i < 9; i++)
        {
            var slot = At(panel, "Slot " + i);
            Place(slot, (i % 3 - 1) * 100, 100 - i / 3 * 100, 90, 90);
            var so = new SerializedObject(slot.GetComponent<PouchSlotUI>());
            var frame = (UnityEngine.UI.Image)so.FindProperty("frameImage").objectReferenceValue;
            frame.sprite = null; frame.type = UnityEngine.UI.Image.Type.Simple;
            so.FindProperty("emptyFrameColor").colorValue = Teal;
            so.FindProperty("lockedFrameColor").colorValue = new Color32(66, 66, 73, 255);
            so.ApplyModifiedPropertiesWithoutUndo();
            var backdrop = Rect(slot, "Inset1006", 0, 0, 70, 70);
            Paint(backdrop, Inset); backdrop.SetAsFirstSibling();
            var icon = (UnityEngine.UI.Image)so.FindProperty("iconImage").objectReferenceValue;
            Place(icon.rectTransform, 0, 0, 70, 70); icon.preserveAspect = true;
            var locked = (GameObject)so.FindProperty("lockedOverlay").objectReferenceValue;
            Place((RectTransform)locked.transform, 0, 0, 70, 70);
            Paint((RectTransform)locked.transform, new Color(0, 0, 0, .5f));
        }
        var effects = At(window, "SetEffects_NotImplemented");
        Place(effects, 306, 67.5f, 180, 240); Frame(effects, 14);
        effects.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        Label(effects.Find("Heading").GetComponent<TextMeshProUGUI>(), "세트 효과", 0, 92, 144, 24, 15);
        var effectsBody = Text(effects, "Description", "세트 효과가\n여기에 표시됩니다.\n\n(준비 중)", 0, -10, 144, 154, 12);
        effectsBody.color = Accent; effectsBody.alignment = TextAlignmentOptions.TopLeft;
        effects.gameObject.SetActive(true);
        Set(root.GetComponent<PouchUI>(), "setEffectsPanel", effects);
        window.gameObject.SetActive(false);
    }

    private static void LayoutPicker(GameObject root)
    {
        var panel = At(root.transform, "MinionSelection");
        // 비교 카드의 기존 동작/그림은 보존하고, 문서에서 확정된 버튼만 바꾼다.
        foreach (string name in new[] { "Replace", "Skip" })
        {
            var button = At(panel, name);
            Place(button, name == "Replace" ? -160 : 160, -210, 300, 60);
            Frame(button, 15); button.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var inner = Rect(button, "ButtonFace", 0, 0, 270, 30);
            Paint(inner, new Color32(47, 63, 76, 255)); inner.SetSiblingIndex(1);
            var fill = button.Find("HoldProgress").GetComponent<UnityEngine.UI.Image>();
            fill.sprite = null; fill.type = UnityEngine.UI.Image.Type.Simple;
            Stretch(fill.rectTransform, 3); fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.color = new Color(Teal.r, Teal.g, Teal.b, .85f); fill.raycastTarget = false; fill.enabled = false;
            Label(button.Find("Label").GetComponent<TextMeshProUGUI>(), name == "Replace" ? "교체 · 1초간 누르기" : "스킵 · 1초간 누르기", 0, 0, 268, 28, 16);
            button.Find("Label").SetAsLastSibling();
        }
        var compare = At(panel, "CompareHover");
        Frame(compare, 4); compare.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        panel.gameObject.SetActive(false);
    }

    private static void LayoutHud(GameObject root)
    {
        var ui = root.GetComponent<PlayerStateUI>();
        var so = new SerializedObject(ui);
        var hp = (UnityEngine.UI.Image)so.FindProperty("hpSprite").objectReferenceValue;
        var shield = (UnityEngine.UI.Image)so.FindProperty("shieldSprite").objectReferenceValue;
        var guard = (UnityEngine.UI.Image)so.FindProperty("guardGaugeFill").objectReferenceValue;
        var hpText = (TextMeshProUGUI)so.FindProperty("hpText").objectReferenceValue;
        var guardText = (TextMeshProUGUI)so.FindProperty("guardGaugeText").objectReferenceValue;
        Bar(hp, shield, hpText, new Color32(234, 64, 91, 255), 4);
        Bar(guard, null, guardText, new Color32(32, 193, 232, 255), 2);
        so.FindProperty("guardReadyColor").colorValue = new Color32(32, 193, 232, 255);
        so.FindProperty("guardBrokenColor").colorValue = new Color32(130, 137, 148, 255);
        so.ApplyModifiedPropertiesWithoutUndo();
        var dash = (DashCooldownUI)so.FindProperty("dashCooldownUI").objectReferenceValue;
        var pips = Rect(root.transform, "WorldDashPips", 0, -25, 42, 8);
        var group = pips.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() ?? pips.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        group.childAlignment = TextAnchor.MiddleCenter; group.spacing = 3;
        group.childControlWidth = group.childControlHeight = false;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
        var fills = new UnityEngine.UI.Image[4];
        for (int i = 0; i < fills.Length; i++)
        {
            var pip = Rect(pips, "Charge" + (i + 1), 0, 0, 12, 6);
            Paint(pip, new Color32(21, 28, 34, 225));
            var track = Rect(pip, "Track", 0, 0, 10, 4);
            var fill = Rect(track, "Fill", 0, 0, 0, 0); Stretch(fill, 0);
            fills[i] = Paint(fill, Accent);
            pip.gameObject.SetActive(i < 2);
        }
        var dashSo = new SerializedObject(dash);
        dashSo.FindProperty("worldPips").objectReferenceValue = pips;
        var array = dashSo.FindProperty("worldPipFills"); array.arraySize = fills.Length;
        for (int i = 0; i < fills.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = fills[i];
        // 기존 HUD의 대시 아이콘/쿨타임은 유지, 중복 횟수 핀만 발밑 표시로 옮긴다.
        var oldPips = dashSo.FindProperty("dashCount");
        for (int i = 0; i < oldPips.arraySize; i++) ((GameObject)oldPips.GetArrayElementAtIndex(i).objectReferenceValue)?.SetActive(false);
        oldPips.arraySize = 0;
        dashSo.ApplyModifiedPropertiesWithoutUndo();
        pips.gameObject.SetActive(false);
    }

    private static void Bar(UnityEngine.UI.Image fill, UnityEngine.UI.Image shield, TextMeshProUGUI label, Color color, float border)
    {
        var bg = fill.transform.parent.name == "Track1006" ? (RectTransform)fill.transform.parent.parent : (RectTransform)fill.transform.parent;
        Frame(bg, border);
        var track = Rect(bg, "Track1006", 0, 0, 0, 0); Stretch(track, border);
        Paint(track, new Color32(15, 22, 28, 255));
        foreach (var image in new[] { shield, fill })
        {
            if (image == null) continue;
            image.transform.SetParent(track, false); Stretch(image.rectTransform, 0);
            image.sprite = null; image.type = UnityEngine.UI.Image.Type.Simple;
            image.color = image == fill ? color : Color.white;
            image.raycastTarget = false;
        }
        Stretch(label.rectTransform, 0); label.fontSize = 8; label.color = Ink;
        label.transform.SetAsLastSibling();
    }

    private static void LayoutMap(GameObject root)
    {
        Stretch((RectTransform)root.transform, 0);
        var hud = At(root.transform, "Image_MiniMap");
        Place(hud, -12, -12, 120, 100);
        hud.anchorMin = hud.anchorMax = hud.pivot = Vector2.one;
        if (hud.GetComponent<UnityEngine.UI.RectMask2D>() == null) hud.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var background = At(hud, "Background");
        Stretch(background, 0); Frame(background, 3.5f);
        background.GetComponent<UnityEngine.UI.Image>().color = new Color(.10f, .12f, .14f, .65f);
        var so = new SerializedObject(root.GetComponent<UIBasedMiniMap>());
        so.FindProperty("fullMapContainer").objectReferenceValue = root.transform.Find("MiniMapUI");
        so.FindProperty("hudMapContainer").objectReferenceValue = hud;
        so.FindProperty("useTerrainShadow").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Frame(RectTransform rect, float width)
    {
        Paint(rect, Panel);
        if (rect.Find("PixelFrame") != null) rect.Find("PixelFrame").gameObject.SetActive(false);
        var frame = Rect(rect, "Frame1006", 0, 0, 0, 0); Stretch(frame, 0); frame.SetAsFirstSibling();
        foreach (string name in new[] { "Top", "Bottom", "Left", "Right" })
        {
            var edge = Rect(frame, name, 0, 0, 0, 0); Stretch(edge, 0);
            if (name == "Top") { edge.anchorMin = Vector2.up; edge.offsetMin = new Vector2(0, -width); }
            if (name == "Bottom") { edge.anchorMax = Vector2.right; edge.offsetMax = new Vector2(0, width); }
            if (name == "Left") { edge.anchorMax = Vector2.up; edge.offsetMax = new Vector2(width, 0); }
            if (name == "Right") { edge.anchorMin = Vector2.right; edge.offsetMin = new Vector2(-width, 0); }
            Paint(edge, Wood);
        }
        foreach (var corner in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one })
        {
            var cap = Rect(frame, "Corner" + corner.x + corner.y, 0, 0, width, width);
            cap.anchorMin = cap.anchorMax = cap.pivot = corner;
            Paint(cap, Teal);
        }
    }

    private static RectTransform At(Transform parent, string name)
        => parent.Find(name) as RectTransform ?? throw new InvalidOperationException("기존 UI 누락: " + parent.name + "/" + name);
    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var rect = parent.Find(name) as RectTransform;
        if (rect == null)
        {
            rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
        }
        Place(rect, x, y, w, h); return rect;
    }
    private static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(w, h); rect.localScale = Vector3.one;
    }
    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = Vector2.one * .5f;
        rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset; rect.localScale = Vector3.one;
    }
    private static UnityEngine.UI.Image Paint(RectTransform rect, Color color)
    {
        var image = rect.GetComponent<UnityEngine.UI.Image>() ?? rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = null; image.type = UnityEngine.UI.Image.Type.Simple; image.color = color; image.raycastTarget = false;
        return image;
    }
    private static void RectImage(Transform parent, string name, float x, float y, float w, float h, Color color) => Paint(Rect(parent, name, x, y, w, h), color);
    private static TextMeshProUGUI Text(Transform parent, string name, string value, float x, float y, float w, float h, float size)
    {
        var rect = Rect(parent, name, x, y, w, h);
        var text = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = _font; Label(text, value, x, y, w, h, size); return text;
    }
    private static void Label(TextMeshProUGUI text, string value, float x, float y, float w, float h, float size)
    {
        Place(text.rectTransform, x, y, w, h); text.text = value; text.fontSize = size;
        text.enableAutoSizing = false; text.color = Ink; text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.Normal;
    }
    private static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void Edit(string path, Action<GameObject> action)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try { action(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/UI/1006/Validate layout")]
    public static void Validate()
    {
        InventoryUISetup.Validate();
        foreach (var path in new[] { Explain, Pouch, Picker, Hud, Map })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(root.GetComponentsInChildren<MonoBehaviour>(true).All(c => c != null), "Missing script: " + path);
            foreach (var frame in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Frame1006"))
                Check(frame.GetComponentsInChildren<UnityEngine.UI.Image>().All(i => i.sprite == null && !i.raycastTarget), "기본 도형/클릭 통과");
        }
        var info = AssetDatabase.LoadAssetAtPath<GameObject>(Explain);
        Check(At(info.transform, "LoadoutPanel/StatsPanel").sizeDelta == new Vector2(220, 480), "C 왼쪽 스탯 치수");
        Check(!At(info.transform, "LoadoutPanel/DetailsPanel").gameObject.activeSelf, "C 상세는 기본 접힘");
        Check(At(info.transform, "LoadoutPanel/StatsPanel/ExpandButton").GetComponent<UnityEngine.UI.Button>().onClick.GetPersistentEventCount() == 1, "화살표 배선");
        Check(new SerializedObject(info.GetComponent<SkillExplainUI>()).FindProperty("playerStatsText").objectReferenceValue != null, "스탯 배선");
        var pouch = AssetDatabase.LoadAssetAtPath<GameObject>(Pouch);
        Check(At(pouch.transform, "PouchWindow/InventoryPanel").sizeDelta == new Vector2(360, 375), "V 치수");
        Check(pouch.GetComponentsInChildren<PouchSlotUI>(true).Length == 9, "9칸 보존");
        Check(At(pouch.transform, "PouchWindow/SetEffects_NotImplemented").gameObject.activeSelf, "오른쪽 세트 효과 확인란");
        var picker = AssetDatabase.LoadAssetAtPath<GameObject>(Picker);
        Check(At(picker.transform, "MinionSelection/Replace").sizeDelta == new Vector2(300, 60), "버튼 치수");
        Check(picker.GetComponent<HandSlotSelectionUI>().ConfirmHoldSeconds == 1f, "1초 홀드 보존");
        var map = AssetDatabase.LoadAssetAtPath<GameObject>(Map);
        Check(At(map.transform, "Image_MiniMap").sizeDelta == new Vector2(120, 100), "미니맵 치수");
        Debug.Log("[UI1006] VALIDATE PASS — authored placeholders, C/V/buttons/HUD/map dimensions and references.");
    }

    public static void ApplyBatch()
    {
        try { Apply(); InventoryUICheck.CheckScenes(); InventoryUICheck.CheckNearbyDrop(); InventoryUICheck.Run(); Preview(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void ValidateBatch()
    {
        try
        {
            // 배치 시작의 이름 없는 씬은 Additive 검사 씬을 열 수 없다.
            if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity", OpenSceneMode.Single);
            Validate(); CheckDynamic(); MiniMapCheck.Run();
            InventoryUICheck.CheckScenes(); InventoryUICheck.CheckNearbyDrop(); InventoryUICheck.Run();
            Preview(); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/UI/1006/Check dynamic HUD and text")]
    public static void CheckDynamic()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 검사하세요.");
        var scene = EditorSceneManager.NewPreviewScene();
        var canvasGo = new GameObject("HUD test", typeof(RectTransform), typeof(Canvas));
        var playerGo = new GameObject("HUD test player", typeof(PlayerController), typeof(MeleeDodgeController));
        var cameraGo = new GameObject("HUD test camera", typeof(Camera));
        foreach (var go in new[] { canvasGo, playerGo, cameraGo }) SceneManager.MoveGameObjectToScene(go, scene);
        var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var camera = cameraGo.GetComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Hud), canvas.transform);
            var dash = hud.GetComponentInChildren<DashCooldownUI>(true);
            var player = playerGo.GetComponent<PlayerController>();
            var dodge = playerGo.GetComponent<MeleeDodgeController>();
            dash.Initialize(player);
            typeof(DashCooldownUI).GetField("_worldCamera", flags).SetValue(dash, camera);
            var fills = (UnityEngine.UI.Image[])typeof(DashCooldownUI).GetField("worldPipFills", flags).GetValue(dash);
            var pips = (RectTransform)typeof(DashCooldownUI).GetField("worldPips", flags).GetValue(dash);
            foreach (int maximum in new[] { 2, 4, 1 })
            {
                typeof(MeleeDodgeController).GetField("maxCharges", flags).SetValue(dodge, maximum);
                typeof(MeleeDodgeController).GetField("_currentCharges", flags).SetValue(dodge, 1);
                typeof(MeleeDodgeController).GetField("_rechargeTimer", flags).SetValue(dodge, dodge.RechargeTime * .5f);
                typeof(DashCooldownUI).GetMethod("LateUpdate", flags).Invoke(dash, null);
                for (int i = 0; i < fills.Length; i++)
                {
                    Check(fills[i].transform.parent.parent.gameObject.activeSelf == (i < maximum), "대시 최대 횟수 칸 표시");
                    float expected = i == 0 ? 1 : i == 1 ? dodge.RechargeProgress : 0;
                    Check(Mathf.Abs(fills[i].rectTransform.anchorMax.x - expected) < .001f, "대시 사용/충전 표시");
                }
            }
            var worldOffset = (Vector3)typeof(DashCooldownUI).GetField("worldOffset", flags).GetValue(dash);
            foreach (float scale in new[] { 1f, 2f })
            {
                canvas.scaleFactor = scale;
                Canvas.ForceUpdateCanvases();
                Check(((RectTransform)pips.parent).rect.width > 0f, "발밑 좌표 검사 부모 너비");
                foreach (var position in new[] { Vector3.zero, new Vector3(1, 1, 0), new Vector3(-2, -1, 0) })
                {
                    playerGo.transform.position = position;
                    typeof(DashCooldownUI).GetMethod("LateUpdate", flags).Invoke(dash, null);
                    Vector2 expected = camera.WorldToScreenPoint(position + worldOffset);
                    Vector2 actual = RectTransformUtility.WorldToScreenPoint(null, pips.position);
                    Check(Vector2.Distance(expected, actual) < .1f,
                        $"발밑 위치 일치: scale={scale}, expected={expected}, actual={actual}");
                }
            }
            Vector2 before = pips.anchoredPosition;
            playerGo.transform.position = new Vector3(1, 1, 0);
            typeof(DashCooldownUI).GetMethod("LateUpdate", flags).Invoke(dash, null);
            Check(Vector2.Distance(before, pips.anchoredPosition) > 1, "발밑 표시가 플레이어를 따라감");
            Object.DestroyImmediate(playerGo);
            typeof(DashCooldownUI).GetMethod("LateUpdate", flags).Invoke(dash, null);
            Check(!pips.gameObject.activeSelf, "플레이어 파괴 후 표시 해제");
            foreach (var name in new[] { "hpSprite", "guardGaugeFill" })
            {
                var fill = (UnityEngine.UI.Image)new SerializedObject(hud.GetComponent<PlayerStateUI>()).FindProperty(name).objectReferenceValue;
                foreach (float fraction in new[] { 0f, .5f, 1f })
                {
                    typeof(PlayerStateUI).GetMethod("SetFill", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { fill, fraction });
                    Check(Mathf.Abs(fill.rectTransform.anchorMax.x - fraction) < .001f, "도형 게이지 0/50/100% 너비");
                }
            }
            var info = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Explain), canvas.transform);
            At(info.transform, "LoadoutPanel").gameObject.SetActive(true);
            At(info.transform, "LoadoutPanel/DetailsPanel").gameObject.SetActive(true);
            foreach (var equipment in AssetDatabase.FindAssets("t:EquipmentSO").Select(g => AssetDatabase.LoadAssetAtPath<EquipmentSO>(AssetDatabase.GUIDToAssetPath(g))))
                CheckText(info, "Equipment", equipment.description + "\n\n강화 +0/2", equipment.name);
            foreach (var guard in AssetDatabase.FindAssets("t:RightClickDataSO").Select(g => AssetDatabase.LoadAssetAtPath<RightClickDataSO>(AssetDatabase.GUIDToAssetPath(g))))
                CheckText(info, "Guard", guard.ResolveDescription(), guard.name);
            foreach (var main in AssetDatabase.FindAssets("t:MainMinionDataSO").Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g))))
            {
                CheckText(info, "Finisher", main.finisher.Describe(), main.name);
                CheckText(info, "Dash", main.dashModifier.Describe(), main.name);
                if (main.minionSkill != null) CheckText(info, "SpaceSkill", main.minionSkill.description, main.name);
            }
            Debug.Log("[UI1006] DYNAMIC PASS — 대시 횟수/충전/추적/파괴, 도형 게이지 너비, 실제 데이터 설명 검사.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void CheckText(GameObject root, string slotName, string value, string source)
    {
        var slot = root.transform.Find("LoadoutPanel/DetailsPanel/" + slotName).GetComponent<SkillExplainSlotUI>();
        var label = (TextMeshProUGUI)new SerializedObject(slot).FindProperty("descriptionText").objectReferenceValue;
        label.enableAutoSizing = false; label.fontSize = 10;
        var preferred = label.GetPreferredValues(value, label.rectTransform.rect.width, Mathf.Infinity);
        Check(preferred.y <= label.rectTransform.rect.height + 1,
            $"TEXT OVERFLOW: {slotName}/{source} needs {preferred.y:0}, available {label.rectTransform.rect.height:0}");
    }

    [MenuItem("Tools/UI/1006/Render previews")]
    public static void Preview()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var cameraGo = new GameObject("UI1006 preview camera", typeof(Camera));
        var canvasGo = new GameObject("UI1006 preview canvas", typeof(RectTransform), typeof(Canvas));
        SceneManager.MoveGameObjectToScene(cameraGo, scene); SceneManager.MoveGameObjectToScene(canvasGo, scene);
        var camera = cameraGo.GetComponent<Camera>(); camera.scene = scene;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .16f, .16f);
        camera.orthographic = true;
        var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        var target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
        var oldTarget = RenderTexture.active;
        try
        {
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            // EditMode에서는 CanvasScaler.Update가 자동 실행되지 않는다.
            typeof(UnityEngine.UI.CanvasScaler).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(scaler, null);
            foreach (string path in new[] { Explain, Pouch, Picker, Hud, Map })
            {
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), canvas.transform);
                if (path == Explain)
                {
                    At(go.transform, "LoadoutPanel").gameObject.SetActive(true);
                    var slots = go.GetComponentsInChildren<SkillExplainSlotUI>(true);
                    var main = AssetDatabase.FindAssets("t:MainMinionDataSO").Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g))).First(m => m.minionSkill != null);
                    foreach (var slot in slots)
                    {
                        switch (slot.name)
                        {
                            case "Equipment": slot.SetData(null, "시작형 건틀릿 - MK0", "기본 공격 3연타 · 1 / 1 / 1.5\n보통 공격 속도 / 경직 0.2초\n기본 공격의 마지막 타격은 미니언과 연계합니다."); break;
                            case "Guard": slot.SetData(null, "가드", "우클릭으로 가드를 올리고 내립니다.\n가드 선 바깥에서 오는 막을 수 있는 공격을 방어합니다.\n퍼펙트 가드 시 게이지 소모량이 절반으로 감소합니다."); break;
                            case "Minion": slot.SetData(main.minionIcon, main.minionName, ""); break;
                            case "SpaceSkill": slot.SetData(main.minionSkill.icon, "Space · " + main.minionSkill.skillName, main.minionSkill.description); break;
                            case "Finisher": slot.SetData(main.finisher.uiIcon, "기본 공격 마무리", main.finisher.Describe()); break;
                            case "Dash": slot.SetData(main.dashModifier.uiIcon, "대쉬 공격", main.dashModifier.Describe()); break;
                        }
                    }
                    At(go.transform, "LoadoutPanel/StatsPanel/PlayerStats/Values").GetComponent<TextMeshProUGUI>().text = "<line-height=19>체력<pos=112>100/100\n가드<pos=112>50/50\n회피 횟수<pos=112>2/2\n\n물리 공격력<pos=112>10\n마법 공격력<pos=112>0\n방어력<pos=112>0%\n회피율<pos=112>0%\n공격 속도<pos=112>1\n치명타 확률<pos=112>0%\n치명타 피해량<pos=112>150%\n적중률<pos=112>100%\n기본 공격 배율<pos=112>1\n이동 속도<pos=112>5\n스킬 쿨타임 감소<pos=112>0%\n대시 쿨타임 감소<pos=112>0%\n물리 피해 증폭<pos=112>0%\n마법 피해 증폭<pos=112>0%";
                }
                if (path == Pouch)
                {
                    At(go.transform, "PouchWindow").gameObject.SetActive(true);
                    var items = AssetDatabase.FindAssets("t:ItemSO").Select(g => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
                    var slots = go.GetComponentsInChildren<PouchSlotUI>(true);
                    for (int i = 0; i < slots.Length; i++) slots[i].SetItem(i < 4 ? items[i % items.Length] : null, true);
                }
                if (path == Picker)
                {
                    At(go.transform, "MinionSelection").gameObject.SetActive(true);
                    var minions = AssetDatabase.FindAssets("t:MainMinionDataSO").Take(2).Select(g => AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
                    var pickerData = new SerializedObject(go.GetComponent<HandSlotSelectionUI>());
                    ((SkillExplainSlotUI)pickerData.FindProperty("currentCard").objectReferenceValue).SetData(minions[0].minionIcon, minions[0].minionName, "현재 장착");
                    ((SkillExplainSlotUI)pickerData.FindProperty("candidateCard").objectReferenceValue).SetData(minions[1].minionIcon, minions[1].minionName, "교체 후보");
                    foreach (var name in new[] { "Replace", "Skip" })
                    {
                        var fill = At(go.transform, "MinionSelection/" + name + "/HoldProgress").GetComponent<UnityEngine.UI.Image>();
                        fill.enabled = true; fill.rectTransform.anchorMax = new Vector2(.5f, 1);
                    }
                }
                if (path == Hud) At(go.transform, "WorldDashPips").gameObject.SetActive(true);
                CapturePreview(camera, target, go.name.Replace("(Clone)", ""));
                if (path == Explain)
                {
                    var bag = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Pouch), canvas.transform);
                    At(bag.transform, "PouchWindow").gameObject.SetActive(true);
                    foreach (var slot in bag.GetComponentsInChildren<PouchSlotUI>(true)) slot.SetItem(null, true);
                    go.transform.SetAsLastSibling();
                    CapturePreview(camera, target, "C-V");
                    At(go.transform, "LoadoutPanel/DetailsPanel").gameObject.SetActive(true);
                    At(go.transform, "LoadoutPanel/StatsPanel/ExpandButton/Label").GetComponent<TextMeshProUGUI>().text = "<";
                    CapturePreview(camera, target, "C-expanded-V");
                    Object.DestroyImmediate(bag);
                    CapturePreview(camera, target, "SkillExplainUI-expanded");
                }
                Object.DestroyImmediate(go);
            }
            Debug.Log("[UI1006] PREVIEW PASS — Logs/UI1006/*.png");
        }
        finally
        {
            RenderTexture.active = oldTarget; camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CapturePreview(Camera camera, RenderTexture target, string name)
    {
        Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
        System.IO.Directory.CreateDirectory("Logs/UI1006");
        System.IO.File.WriteAllBytes("Logs/UI1006/" + name + ".png", pixels.EncodeToPNG());
        Object.DestroyImmediate(pixels);
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("[UI1006] " + message); }
}

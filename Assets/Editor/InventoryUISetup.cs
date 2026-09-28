using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>0928 C/V UI 에셋 저작 도구. 실행 중 생성하지 않고 프리팹에 구조와 참조를 저장한다.</summary>
public static class InventoryUISetup
{
    private const string Explain = "Assets/Prefabs/UI/SkillExplainUI.prefab";
    private const string Pouch = "Assets/Prefabs/UI/PouchUI.prefab";
    private const string Picker = "Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab";
    private const string Card = "Assets/Prefabs/UI/Reward Selection/RewardCard 1.prefab";
    private const string Ground = "Assets/Prefabs/Resources/GroundItem.prefab";
    private static TMP_FontAsset _font;
    private static Sprite _panelSprite;
    private static Color _panelColor;

    [MenuItem("Tools/UI/0928/Apply authored inventory UI")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(Explain);
        _font = original.GetComponentInChildren<TextMeshProUGUI>(true).font;
        var style = original.GetComponentsInChildren<Image>(true).First(i => i.sprite != null);
        _panelSprite = style.sprite;
        _panelColor = new Color(0.10f, 0.12f, 0.15f, 0.96f);
        Edit(Explain, BuildExplain);
        Edit(Pouch, BuildPouch);
        Edit(Picker, BuildPicker);
        Edit(Ground, root =>
        {
            var pickup = root.GetComponent<GroundItem>();
            var canvas = root.GetComponentInChildren<Canvas>(true);
            var info = canvas.GetComponentInChildren<Tooltip>(true);
            if (info == null)
            {
                string path = AssetDatabase.GUIDToAssetPath("61d55bbb2041b96408cede075e20cb56");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), canvas.transform);
                info = go.GetComponent<Tooltip>();
            }
            Set(pickup, "info", info);
            canvas.gameObject.SetActive(false);
        });
        var groundRoot = PrefabUtility.LoadPrefabContents(Ground);
        try { groundRoot.name = "GroundMinion"; PrefabUtility.SaveAsPrefabAsset(groundRoot, "Assets/Prefabs/Resources/GroundMinion.prefab"); }
        finally { PrefabUtility.UnloadPrefabContents(groundRoot); }
        AssetDatabase.SaveAssets();
        Validate();
        Debug.Log("[InventoryUI] APPLY PASS — C/V/미니언 픽업 프리팹 저장 완료.");
    }

    private static void BuildExplain(GameObject root)
    {
        var ui = root.GetComponent<SkillExplainUI>();
        ClearChildren(root.transform);
        RemoveLayout(root);
        if (root.TryGetComponent<Image>(out var oldImage)) oldImage.enabled = false;
        FullScreen((RectTransform)root.transform);
        var panel = Background(root.transform, "LoadoutPanel", Vector2.zero, new Vector2(820, 440));
        Text(panel, "Heading", "장착 정보  [C]", new Vector2(0, 196), new Vector2(760, 28), 20);
        var equipment = Slot(panel, "Equipment", new Vector2(-205, 74), new Vector2(374, 184));
        var guard = Slot(panel, "Guard", new Vector2(-205, -125), new Vector2(374, 168));
        var header = Slot(panel, "Minion", new Vector2(202, 142), new Vector2(370, 44));
        var skill = Slot(panel, "SpaceSkill", new Vector2(202, 62), new Vector2(370, 106));
        var finisher = Slot(panel, "Finisher", new Vector2(202, -53), new Vector2(370, 106));
        var dash = Slot(panel, "Dash", new Vector2(202, -165), new Vector2(370, 106));
        Set(ui, "panelRoot", panel.gameObject);
        Array(ui, "equipmentSlots", equipment);
        Array(ui, "linkedSkillSlots", finisher, dash, skill);
        Set(ui, "guardSlot", guard);
        Set(ui, "minionHeader", header);
        panel.gameObject.SetActive(false);
        root.SetActive(true);
    }

    private static void BuildPouch(GameObject root)
    {
        var ui = root.GetComponent<PouchUI>();
        var serialized = new SerializedObject(ui);
        var existing = root.GetComponentsInChildren<PouchSlotUI>(true).Where(s => s.name.StartsWith("Slot ")).OrderBy(s => s.name).ToArray();
        if (existing.Length != 9) throw new InvalidOperationException("Pouch의 기존 9칸을 확인하세요.");
        // 기존 슬롯/아이콘은 보존한다.
        var keep = new GameObject("Keep", typeof(RectTransform));
        foreach (var slot in existing) slot.transform.SetParent(keep.transform, false);
        var ghost = (Image)serialized.FindProperty("dragGhost").objectReferenceValue;
        ghost.transform.SetParent(keep.transform, false);
        ClearChildren(root.transform);
        var window = Rect(root.transform, "PouchWindow", Vector2.zero, new Vector2(740, 460));
        var main = Background(window, "InventoryPanel", new Vector2(0, 35), new Vector2(360, 350));
        Text(main, "Heading", "아이템 주머니  [V]", new Vector2(0, 153), new Vector2(330, 28), 18);
        for (int i = 0; i < 9; i++)
        {
            existing[i].transform.SetParent(main, false);
            Place((RectTransform)existing[i].transform, new Vector2((i % 3 - 1) * 106, 95 - i / 3 * 106), new Vector2(92, 92));
        }
        var synergy = Background(window, "SetEffects_NotImplemented", new Vector2(-300, 30), new Vector2(200, 340));
        Text(synergy, "Heading", "세트 효과", new Vector2(0, 140), new Vector2(170, 24), 16);
        synergy.gameObject.SetActive(false);
        ghost.transform.SetParent(root.transform, false);
        ghost.raycastTarget = false;
        Object.DestroyImmediate(keep);
        Set(ui, "panelRoot", window.gameObject);
        Set(ui, "panelRect", main);
        Array(ui, "slots", existing.Cast<Object>().ToArray());
        AddDropOutline(root);
        window.gameObject.SetActive(false);
        ghost.gameObject.SetActive(false);
        // 네 플레이 씬에 이미 배치된 CommonTooltipUI 하나를 그대로 재사용한다.
        root.SetActive(true);
    }

    private static void BuildPicker(GameObject root)
    {
        var ui = root.GetComponent<HandSlotSelectionUI>();
        ClearChildren(root.transform);
        RemoveLayout(root);
        FullScreen((RectTransform)root.transform);
        var panel = Rect(root.transform, "MinionSelection", Vector2.zero, new Vector2(900, 500));
        var current = MinionCard(panel, "Current", new Vector2(-160, 20));
        var candidate = MinionCard(panel, "Candidate", new Vector2(160, 20));
        var leftDetails = Slot(panel, "CurrentDetails", new Vector2(-245, 20), new Vector2(440, 370));
        var rightDetails = Slot(panel, "CandidateDetails", new Vector2(245, 20), new Vector2(440, 370));
        foreach (var detail in new[] { leftDetails, rightDetails })
        {
            var image = detail.gameObject.AddComponent<Image>(); image.sprite = _panelSprite; image.type = Image.Type.Sliced; image.color = _panelColor;
            foreach (var graphic in detail.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
        }
        var compare = Background(panel, "CompareHover", new Vector2(0, 15), new Vector2(65, 70));
        Text(compare, "Label", "비교", Vector2.zero, new Vector2(62, 35), 16);
        Hook(current.gameObject, ui, HandSlotSelectionItem.ActionKind.Current);
        Hook(candidate.gameObject, ui, HandSlotSelectionItem.ActionKind.Candidate);
        Hook(compare.gameObject, ui, HandSlotSelectionItem.ActionKind.Compare);
        var replace = HoldButton(panel, ui, "Replace", "교체 · 3초간 누르기", new Vector2(-150, -221), HandSlotSelectionItem.ActionKind.Replace);
        HoldButton(panel, ui, "Skip", "스킵 · 3초간 누르기", new Vector2(150, -221), HandSlotSelectionItem.ActionKind.Skip);
        var singleAnchor = Rect(panel, "SingleCardAnchor", new Vector2(0, 20), Vector2.zero);
        var candidateAnchor = Rect(panel, "CandidateAnchor", new Vector2(160, 20), Vector2.zero);
        Set(ui, "panel", panel.gameObject);
        Set(ui, "currentCard", current); Set(ui, "candidateCard", candidate);
        Set(ui, "currentDetails", leftDetails); Set(ui, "candidateDetails", rightDetails);
        Set(ui, "comparisonTarget", compare.gameObject); Set(ui, "replaceButton", replace);
        Set(ui, "singleCardAnchor", singleAnchor); Set(ui, "candidateAnchor", candidateAnchor);
        leftDetails.gameObject.SetActive(false); rightDetails.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
        root.SetActive(true);
    }

    private static SkillExplainSlotUI MinionCard(Transform parent, string name, Vector2 pos)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Card), parent);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        Place((RectTransform)go.transform, pos, new Vector2(250, 365));
        var reward = go.GetComponent<RewardCard>();
        var data = new SerializedObject(reward);
        var icon = (Image)data.FindProperty("iconImage").objectReferenceValue;
        var title = (TextMeshProUGUI)data.FindProperty("nameText").objectReferenceValue;
        var desc = (TextMeshProUGUI)data.FindProperty("descText").objectReferenceValue;
        Object.DestroyImmediate(reward);
        if (go.TryGetComponent<Button>(out var button)) Object.DestroyImmediate(button);
        RemoveLayout(go);
        Place(icon.rectTransform, new Vector2(0, 100), new Vector2(72, 72));
        Place(title.rectTransform, new Vector2(0, 34), new Vector2(210, 42));
        Place(desc.rectTransform, new Vector2(0, -85), new Vector2(205, 140));
        title.fontSize = 17; desc.fontSize = 15;
        title.alignment = TextAlignmentOptions.Center; desc.alignment = TextAlignmentOptions.Top;
        foreach (var graphic in go.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
        go.GetComponent<Image>().raycastTarget = true;
        var slot = go.AddComponent<SkillExplainSlotUI>();
        Set(slot, "iconImage", icon); Set(slot, "titleText", title); Set(slot, "descriptionText", desc);
        return slot;
    }

    private static GameObject HoldButton(Transform parent, HandSlotSelectionUI owner, string name, string label, Vector2 pos, HandSlotSelectionItem.ActionKind action)
    {
        var rect = Background(parent, name, pos, new Vector2(265, 34));
        var fill = Background(rect, "HoldProgress", Vector2.zero, new Vector2(265, 34)).GetComponent<Image>();
        fill.sprite = null; // 기본 UISprite의 둥근 모양 대신 바 전체를 채운다.
        fill.type = Image.Type.Simple; FullScreen(fill.rectTransform);
        fill.rectTransform.anchorMax = new Vector2(0, 1); fill.enabled = false;
        fill.color = new Color(0.2f, 0.6f, 0.85f, 0.75f); fill.raycastTarget = false;
        Text(rect, "Label", label, Vector2.zero, new Vector2(255, 30), 15);
        var hook = Hook(rect.gameObject, owner, action); Set(hook, "holdFill", fill);
        return rect.gameObject;
    }
    private static HandSlotSelectionItem Hook(GameObject go, HandSlotSelectionUI owner, HandSlotSelectionItem.ActionKind action)
    {
        var hook = go.AddComponent<HandSlotSelectionItem>(); Set(hook, "owner", owner);
        var so = new SerializedObject(hook); so.FindProperty("action").enumValueIndex = (int)action; so.ApplyModifiedPropertiesWithoutUndo();
        return hook;
    }
    private static SkillExplainSlotUI Slot(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var rect = Rect(parent, name, pos, size);
        var icon = Rect(rect, "Icon", new Vector2(-size.x / 2 + 26, size.y / 2 - 26), new Vector2(36, 36)).gameObject.AddComponent<Image>();
        icon.preserveAspect = true; icon.raycastTarget = false;
        var title = Text(rect, "Title", name, new Vector2(22, size.y / 2 - 24), new Vector2(size.x - 66, 40), 16);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        var desc = Text(rect, "Description", "", new Vector2(0, -22), new Vector2(size.x - 20, Mathf.Max(0, size.y - 54)), 13);
        desc.alignment = TextAlignmentOptions.TopLeft;
        var slot = rect.gameObject.AddComponent<SkillExplainSlotUI>();
        Set(slot, "iconImage", icon); Set(slot, "titleText", title); Set(slot, "descriptionText", desc);
        return slot;
    }
    private static RectTransform Background(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var rect = Rect(parent, name, pos, size);
        var image = rect.gameObject.AddComponent<Image>(); image.sprite = _panelSprite; image.type = Image.Type.Sliced; image.color = _panelColor;
        return rect;
    }
    private static TextMeshProUGUI Text(Transform parent, string name, string value, Vector2 pos, Vector2 size, float fontSize)
    {
        var text = Rect(parent, name, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = _font; text.fontSize = fontSize; text.text = value; text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }
    private static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); Place(rect, pos, size); return rect;
    }
    private static void Place(RectTransform rect, Vector2 pos, Vector2 size)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = pos; rect.sizeDelta = size; rect.localScale = Vector3.one; }
    private static void FullScreen(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
    private static void RemoveLayout(GameObject go)
    { foreach (var layout in go.GetComponentsInChildren<LayoutGroup>(true)) Object.DestroyImmediate(layout); foreach (var fitter in go.GetComponentsInChildren<ContentSizeFitter>(true)) Object.DestroyImmediate(fitter); }
    private static void ClearChildren(Transform root) { for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject); }
    private static void Set(Object target, string field, Object value)
    { var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Array(Object target, string field, params Object[] values)
    { var so = new SerializedObject(target); var p = so.FindProperty(field); p.arraySize = values.Length; for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Edit(string path, Action<GameObject> change)
    { var root = PrefabUtility.LoadPrefabContents(path); try { change(root); PrefabUtility.SaveAsPrefabAsset(root, path); } finally { PrefabUtility.UnloadPrefabContents(root); } }

    [MenuItem("Tools/UI/0928/Apply pointer drop and existing artwork")]
    public static void ApplyArtwork()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        // 구조 전체를 재생성하지 않는다. 현재 슬롯/참조/배치는 유지하고 표시용 자식만 저작한다.
        Edit(Pouch, root =>
        {
            var old = root.transform.Find("PouchWindow/DropStagingArea");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            AddDropOutline(root);
            var panel = root.transform.Find("PouchWindow/InventoryPanel") as RectTransform;
            Frame(panel);
            foreach (var slot in root.GetComponentsInChildren<PouchSlotUI>(true))
            {
                var frame = (Image)new SerializedObject(slot).FindProperty("frameImage").objectReferenceValue;
                Skin(frame, "Assets/Resources/Sprites/UI/UI.png", "UI_SkillFrame");
                var settings = new SerializedObject(slot);
                settings.FindProperty("emptyFrameColor").colorValue = Color.white;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            var ghost = (Image)new SerializedObject(root.GetComponent<PouchUI>()).FindProperty("dragGhost").objectReferenceValue;
            ghost.transform.SetAsLastSibling(); ghost.preserveAspect = true;
        });
        Edit(Explain, root => Frame(root.transform.Find("LoadoutPanel") as RectTransform));
        Edit(Picker, root =>
        {
            var panel = root.transform.Find("MinionSelection");
            foreach (string name in new[] { "CurrentDetails", "CandidateDetails" }) Frame(panel.Find(name) as RectTransform);
            foreach (string name in new[] { "Replace", "Skip", "CompareHover" })
                Skin(panel.Find(name).GetComponent<Image>(), "Assets/Resources/Sprites/UI/UI.png", "UI_SkillFrame");
            foreach (string name in new[] { "Replace", "Skip" })
            {
                var button = panel.Find(name) as RectTransform;
                var fill = button.Find("HoldProgress").GetComponent<Image>();
                fill.sprite = null; fill.type = Image.Type.Simple;
                FullScreen(fill.rectTransform);
                fill.rectTransform.offsetMin = new Vector2(3, 3); fill.rectTransform.offsetMax = new Vector2(-3, -3);
                fill.rectTransform.anchorMax = new Vector2(0, 1); fill.enabled = false;
                fill.color = new Color(0.2f, 0.65f, 1f, 0.8f); fill.raycastTarget = false;
            }
        });
        AssetDatabase.SaveAssets(); Validate();
        Debug.Log("[InventoryUI] ARTWORK PASS — 기존 픽셀 프레임/직사각 홀드 바/화면 드랍 테두리 저장.");
    }

    private static void Skin(Image image, string path, string spriteName)
    {
        image.sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single(s => s.name == spriteName);
        image.type = Image.Type.Sliced; image.color = Color.white;
        // 원본 픽셀 1개 = 기준 Canvas 좌표 1개. 32 PPU / Canvas 100 PPU 배율 보정.
        image.pixelsPerUnitMultiplier = 100f / image.sprite.pixelsPerUnit;
    }

    private static void Frame(RectTransform panel)
    {
        var background = panel.GetComponent<Image>();
        background.sprite = null; background.color = new Color(0.22f, 0.075f, 0.09f, 0.97f);
        var frame = panel.Find("PixelFrame") as RectTransform;
        if (frame == null) frame = Rect(panel, "PixelFrame", Vector2.zero, Vector2.zero);
        FullScreen(frame); frame.SetAsFirstSibling();
        // UI_New의 기존 3조각 프레임을 이어 쓴다. 가운데 투명 영역은 부모 배경으로 채운다.
        string[] names = { "left", "middle", "right" };
        for (int i = 0; i < names.Length; i++)
        {
            var part = frame.Find(names[i]) as RectTransform;
            if (part == null) part = Rect(frame, names[i], Vector2.zero, Vector2.zero);
            FullScreen(part);
            if (i == 0) { part.anchorMax = new Vector2(0, 1); part.offsetMax = new Vector2(28, 0); }
            if (i == 1) { part.offsetMin = new Vector2(28, 0); part.offsetMax = new Vector2(-28, 0); }
            if (i == 2) { part.anchorMin = new Vector2(1, 0); part.offsetMin = new Vector2(-28, 0); }
            var image = part.GetComponent<Image>() ?? part.gameObject.AddComponent<Image>();
            Skin(image, "Assets/Resources/Sprites/UI/UI_New.png", names[i]); image.raycastTarget = false;
        }
    }

    private static void AddDropOutline(GameObject root)
    {
        FullScreen((RectTransform)root.transform);
        var outline = root.transform.Find("DropOutline") as RectTransform;
        if (outline == null) outline = Rect(root.transform, "DropOutline", Vector2.zero, Vector2.zero);
        FullScreen(outline); outline.offsetMin = new Vector2(8, 8); outline.offsetMax = new Vector2(-8, -8);
        foreach (string name in new[] { "Top", "Bottom", "Left", "Right" })
        {
            var edge = outline.Find(name) as RectTransform;
            if (edge == null) edge = Rect(outline, name, Vector2.zero, Vector2.zero);
            FullScreen(edge);
            switch (name)
            {
                case "Top": edge.anchorMin = new Vector2(0, 1); edge.offsetMin = new Vector2(0, -2); break;
                case "Bottom": edge.anchorMax = new Vector2(1, 0); edge.offsetMax = new Vector2(0, 2); break;
                case "Left": edge.anchorMax = new Vector2(0, 1); edge.offsetMax = new Vector2(2, 0); break;
                case "Right": edge.anchorMin = new Vector2(1, 0); edge.offsetMin = new Vector2(-2, 0); break;
            }
            var image = edge.GetComponent<Image>() ?? edge.gameObject.AddComponent<Image>();
            image.color = new Color(0.95f, 0.2f, 0.23f, 0.9f); image.raycastTarget = false;
        }
        Set(root.GetComponent<PouchUI>(), "dropOutline", outline.gameObject);
        outline.gameObject.SetActive(false);
    }

    [MenuItem("Tools/UI/0928/Validate inventory UI")]
    public static void Validate()
    {
        foreach (string path in new[] { Explain, Pouch, Picker, Ground, "Assets/Prefabs/Resources/GroundMinion.prefab" })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) throw new Exception("누락: " + path);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) throw new Exception("Missing Script: " + path);
                if (!(behaviour is PouchUI || behaviour is HandSlotSelectionUI || behaviour is SkillExplainSlotUI || behaviour is GroundItem)) continue;
                var so = new SerializedObject(behaviour); var p = so.GetIterator();
                while (p.NextVisible(true))
                    if (p.propertyType == SerializedPropertyType.ObjectReference && p.name != "m_Script" && p.objectReferenceValue == null)
                        throw new Exception($"누락된 참조: {path}/{behaviour.name}/{p.propertyPath}");
            }
        }
        Debug.Log("[InventoryUI] VALIDATE PASS — 프리팹 실제 자식/참조 확인.");
    }
}

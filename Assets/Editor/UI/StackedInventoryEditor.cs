using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>아이템 주머니 프리팹/데이터 저작. 런타임 UI 생성기가 아니다.</summary>
public static class StackedInventoryEditor
{
    public const string PouchPath = "Assets/Prefabs/UI/PouchUI.prefab";
    public const string SlotPath = "Assets/Prefabs/UI/PouchItemSlot.prefab";
    private static readonly Color Burgundy = new Color32(89, 35, 45, 255);
    private static readonly Color Teal = new Color32(24, 53, 58, 255);
    private static readonly Color Border = new Color32(60, 126, 125, 255);
    private static readonly Color Cream = new Color32(239, 233, 213, 255);
    private static TMP_FontAsset _font;

    public static void Validate()
    {
        if (Application.isBatchMode)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity");
        InventoryUICheck.Run();
        CriticalItemsCheck.Run();
        InventoryUICheck.CheckScenes();
    }

    [MenuItem("Tools/Items/Apply stacked pouch assets")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        UpdateItems();
        var root = PrefabUtility.LoadPrefabContents(PouchPath);
        try { BuildPouch(root); PrefabUtility.SaveAsPrefabAsset(root, PouchPath); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        // 기본 2 + 계약서 9개의 3회 추가 충전을 실제 HUD에도 표시한다.
        const string hudPath = "Assets/Prefabs/UI/Player State/PlayerStateUI.prefab";
        root = PrefabUtility.LoadPrefabContents(hudPath);
        try
        {
            var ui = root.GetComponentInChildren<DashCooldownUI>(true);
            var serialized = new SerializedObject(ui);
            var pips = serialized.FindProperty("worldPipFills");
            if (pips.arraySize == 0) throw new Exception("대시 표시 원본 없음");
            while (pips.arraySize < 5)
            {
                var original = (Image)pips.GetArrayElementAtIndex(0).objectReferenceValue;
                var charge = original.transform.parent.parent;
                var clone = Object.Instantiate(charge.gameObject, charge.parent);
                clone.name = "Charge " + (pips.arraySize + 1);
                var fill = clone.GetComponentsInChildren<Image>(true).Single(i => i.name == original.name);
                pips.arraySize++;
                pips.GetArrayElementAtIndex(pips.arraySize - 1).objectReferenceValue = fill;
                clone.SetActive(false);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, hudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("[StackedInventory] AUTHOR PASS — 아이템 9종, V창/슬롯/HUD 프리팹 저장.");
    }

    private static void UpdateItems()
    {
        var growth = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>("Assets/SOData/Registry/Growth Reward Registry.asset");
        var shop = AssetDatabase.LoadAssetAtPath<ShopRegistrySO>("Assets/SOData/Registry/Shop Registry.asset");
        if (growth == null || shop == null) throw new Exception("레지스트리 없음");
        ItemStatEffect Stat(StatType type, float value, bool percent = false) => new ItemStatEffect { statType = type, value = value, isPercent = percent };
        void Upsert(string id, string title, string description, int limit, params ItemEffect[] effects)
        {
            string path = "Assets/SOData/Items/" + id + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
            if (item == null) { item = ScriptableObject.CreateInstance<ItemSO>(); AssetDatabase.CreateAsset(item, path); }
            // 에셋 GUID와 기존 아이콘은 보존한다. 숫돌 파편도 구 CrackedWhetstone 세이브와 호환된다.
            item.itemName = title; item.description = description; item.maxStack = limit;
            item.effects = effects.ToList();
            if (!growth.items.Contains(item)) growth.items.Add(item);
            if (!shop.itemPool.Contains(item)) shop.itemPool.Add(item);
            EditorUtility.SetDirty(item);
        }
        Upsert("Whetstone", "숫돌", "치명타 확률 +7%. 소지 중 총 치명타 확률이 100%를 넘으면, 초과한 10%마다 치명타 피해가 10% 증가합니다. 변환 효과는 여러 개 소지해도 중복 적용되지 않습니다.", 10,
            Stat(StatType.CritChance, 7), new ItemCritOverflowEffect());
        Upsert("CrackedWhetstone", "숫돌 파편", "치명타 피해가 15% 증가합니다.", 10, Stat(StatType.CritDamage, 15));
        Upsert("ClothArmor", "천갑옷", "방어력이 4 증가합니다.", 10, Stat(StatType.Defense, 4));
        Upsert("LightBoots", "가벼운 장화", "이동 속도가 4% 증가합니다.", 10, Stat(StatType.MoveSpeed, .04f, true));
        Upsert("WindFeather", "바람깃털", "회피율이 2% 증가합니다.", 10, Stat(StatType.Evasion, .02f));
        Upsert("PortableShield", "휴대용 방어막 장치", "방에 진입하면 보호막을 6 획득합니다. 이 아이템의 남은 보호막은 진입 시 갱신되며 누적되지 않습니다.", 10, new ItemRoomEntryShieldEffect());
        Upsert("GaleDashRecovery", "질풍대시 - 회복", "대시 충전 속도가 5% 증가합니다. 10개면 충전 속도 +50%로, 충전 시간은 원래의 2/3이 됩니다.", 10, Stat(StatType.DashRechargeSpeed, .05f, true));
        Upsert("GaleDashLeap", "질풍대시 - 도약", "대시 거리가 3% 증가합니다. 벽과 착지 지점의 안전 검사는 그대로 적용됩니다.", 10, Stat(StatType.DashDistance, .03f, true));
        Upsert("HermesContract", "헤르메스와의 계약서류", "3개마다 최대 대시 횟수가 1 증가합니다. 3 / 6 / 9개일 때 각각 +1 / +2 / +3회.", 9, new ItemDashChargeEffect());
        EditorUtility.SetDirty(growth); EditorUtility.SetDirty(shop);
    }

    public static void BuildPouch(GameObject root)
    {
        _font = root.GetComponentInChildren<TMP_Text>(true)?.font;
        if (_font == null) _font = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/SkillExplainUI.prefab").GetComponentInChildren<TMP_Text>(true).font;
        var ui = root.GetComponent<PouchUI>();
        // V창 내부만 교체한다. 상위 Canvas, C창, 씬 배치는 손대지 않는다.
        for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        // C 통계 패널의 오른쪽(-240)과 16 간격. 런타임에서는 프리팹 배치를 그대로 사용한다.
        var window = Box(root.transform, "PouchWindow", new Vector2(118, 0), new Vector2(684, 480), Burgundy);
        Label(window, "Heading", "아이템 주머니 [V]", new Vector2(-188, 210), new Vector2(260, 30), 24);
        var totals = Label(window, "Totals", "보유 0개   종류 0종", new Vector2(192, 210), new Vector2(254, 26), 16);
        totals.alignment = TextAlignmentOptions.Right;
        var gridPanel = Box(window, "InventoryPanel", new Vector2(-132, -20), new Vector2(396, 414), Teal);
        var details = Box(window, "ItemDetails", new Vector2(200, -20), new Vector2(244, 414), Teal);
        var buttons = new Button[3];
        UnityAction[] actions = { ui.SortByAcquired, ui.SortByCount, ui.SortByTier };
        string[] labels = { "획득순", "개수순", "등급순" };
        for (int i = 0; i < 3; i++)
        {
            var frame = Box(gridPanel, "Sort" + i, new Vector2(-150 + i * 78, 179), new Vector2(72, 32), Border);
            var face = Box(frame, "Face", Vector2.zero, new Vector2(68, 28), Teal);
            var button = face.gameObject.AddComponent<Button>(); button.targetGraphic = face.GetComponent<Image>();
            Label(face, "Label", labels[i], Vector2.zero, new Vector2(68, 28), 15).alignment = TextAlignmentOptions.Center;
            UnityEventTools.AddPersistentListener(button.onClick, actions[i]);
            buttons[i] = button;
        }
        Label(gridPanel, "Hint", "클릭해서 상세 보기", new Vector2(124, 178), new Vector2(126, 25), 12).color = new Color32(167, 195, 195, 255);
        var listScroll = Scroll(gridPanel, "ItemScroll", new Vector2(0, -25), new Vector2(380, 350), out var content);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(56, 56); grid.spacing = new Vector2(7, 7);
        grid.padding = new RectOffset(3, 3, 3, 3);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 6;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var empty = Label(gridPanel, "Empty", "아직 보유한 아이템이 없습니다", new Vector2(0, 30), new Vector2(374, 42), 17);
        empty.alignment = TextAlignmentOptions.Center;

        var iconFrame = Box(details, "IconFrame", new Vector2(-73, 158), new Vector2(76, 76), Border);
        Box(iconFrame, "Backing", Vector2.zero, new Vector2(68, 68), Teal).GetComponent<Image>().raycastTarget = false;
        var icon = Box(iconFrame, "Icon", Vector2.zero, new Vector2(50, 50), Color.white).GetComponent<Image>(); icon.preserveAspect = true;
        icon.raycastTarget = false;
        var title = Label(details, "Title", "아이템을 선택하세요", new Vector2(44, 168), new Vector2(136, 46), 19);
        var tier = Label(details, "TierAndCount", "", new Vector2(43, 132), new Vector2(134, 26), 13);
        tier.color = new Color32(167, 195, 195, 255);
        Box(details, "Divider", new Vector2(0, 107), new Vector2(220, 2), Border).GetComponent<Image>().raycastTarget = false;
        var detailScroll = Scroll(details, "DetailScroll", new Vector2(0, -47), new Vector2(220, 290), out var detailContent);
        var layout = detailContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        layout.spacing = 16; layout.padding = new RectOffset(3, 6, 8, 12);
        detailContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        TMP_Text Detail(string name, int size, Color color)
        {
            var label = Label(detailContent, name, "", Vector2.zero, new Vector2(210, 30), size);
            label.color = color; return label;
        }
        var perCopy = Detail("PerCopy", 15, Cream);
        var stack = Detail("StackCount", 15, Cream);
        var total = Detail("TotalEffect", 18, new Color32(250, 201, 76, 255));
        var next = Detail("NextCopy", 14, new Color32(167, 195, 195, 255));
        var description = Detail("Description", 15, Cream);

        Set(ui, "panelRoot", window.gameObject); Set(ui, "content", content); Set(ui, "slotPrefab", MakeSlot());
        Set(ui, "itemScroll", listScroll); Set(ui, "detailScroll", detailScroll); Set(ui, "totalsText", totals); Set(ui, "emptyText", empty);
        Set(ui, "detailIcon", icon); Set(ui, "detailTitle", title); Set(ui, "detailTier", tier); Set(ui, "perCopyText", perCopy);
        Set(ui, "stackText", stack); Set(ui, "totalEffectText", total); Set(ui, "nextEffectText", next); Set(ui, "descriptionText", description);
        var serialized = new SerializedObject(ui); var array = serialized.FindProperty("sortButtons"); array.arraySize = 3;
        for (int i = 0; i < 3; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        window.gameObject.SetActive(false);
    }

    private static PouchSlotUI MakeSlot()
    {
        var root = Box(null, "PouchItemSlot", Vector2.zero, new Vector2(56, 56), Border);
        try
        {
            var slot = root.gameObject.AddComponent<PouchSlotUI>();
            root.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var backing = Box(root, "Backing", Vector2.zero, Vector2.zero, Teal);
            Stretch(backing); backing.offsetMin = Vector2.one * 4; backing.offsetMax = -Vector2.one * 4;
            backing.GetComponent<Image>().raycastTarget = false;
            var icon = Box(root, "Icon", new Vector2(0, 3), new Vector2(34, 34), Color.white).GetComponent<Image>();
            icon.preserveAspect = true; icon.raycastTarget = false;
            var count = Label(root, "Count", "×1", new Vector2(-4, 0), new Vector2(42, 20), 15); count.alignment = TextAlignmentOptions.Right;
            count.rectTransform.anchorMin = count.rectTransform.anchorMax = count.rectTransform.pivot = new Vector2(1, 0);
            var selection = Rect(root, "Selection", Vector2.zero, Vector2.zero);
            Stretch(selection); selection.offsetMin = -Vector2.one * 3; selection.offsetMax = Vector2.one * 3;
            foreach (bool horizontal in new[] { true, false }) foreach (int sign in new[] { -1, 1 })
            {
                var edge = Box(selection, (horizontal ? "Horizontal" : "Vertical") + sign, Vector2.zero, Vector2.zero, new Color32(250, 201, 76, 255));
                float side = sign > 0 ? 1 : 0;
                edge.anchorMin = horizontal ? new Vector2(0, side) : new Vector2(side, 0);
                edge.anchorMax = horizontal ? new Vector2(1, side) : new Vector2(side, 1);
                edge.pivot = horizontal ? new Vector2(.5f, side) : new Vector2(side, .5f);
                edge.sizeDelta = horizontal ? new Vector2(0, 2) : new Vector2(2, 0);
                edge.GetComponent<Image>().raycastTarget = false;
            }
            Set(slot, "iconImage", icon); Set(slot, "frameImage", root.GetComponent<Image>()); Set(slot, "countText", count); Set(slot, "selectionFrame", selection.gameObject);
            selection.gameObject.SetActive(false);
            return PrefabUtility.SaveAsPrefabAsset(root.gameObject, SlotPath).GetComponent<PouchSlotUI>();
        }
        finally { Object.DestroyImmediate(root.gameObject); }
    }

    private static ScrollRect Scroll(Transform parent, string name, Vector2 pos, Vector2 size, out RectTransform content)
    {
        var root = Rect(parent, name, pos, size);
        var viewport = Box(root, "Viewport", Vector2.zero, size, Teal); Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        content = Rect(viewport, "Content", Vector2.zero, new Vector2(0, 0));
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        var scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
        return scroll;
    }
    private static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.gameObject.layer = LayerMask.NameToLayer("UI");
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = pos; rect.sizeDelta = size; return rect;
    }
    private static RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    { var rect = Rect(parent, name, pos, size); rect.gameObject.AddComponent<Image>().color = color; return rect; }
    private static TMP_Text Label(Transform parent, string name, string value, Vector2 pos, Vector2 size, int fontSize)
    {
        var label = Rect(parent, name, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = _font; label.fontSize = fontSize; label.color = Cream; label.text = value; label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.Normal; label.raycastTarget = false; return label;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void Set(Object target, string field, Object value)
    { var data = new SerializedObject(target); data.FindProperty(field).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo(); }
}

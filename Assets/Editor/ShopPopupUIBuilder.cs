#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Resources/UI/ShopPopupUI.prefab 를 생성/재생성하고, ShopNPC 프리팹을 '말 걸어서 여는 상점' 구조로 정리한다.
/// 생성 후에는 프리팹을 직접 꾸며도 된다(다시 실행하면 팝업 프리팹을 덮어쓰니 꾸민 뒤에는 누르지 말 것).
/// </summary>
public static class ShopPopupUIBuilder
{
    private const string PrefabPath = "Assets/Resources/UI/ShopPopupUI.prefab";
    private const string FontSourcePrefab = "Assets/Prefabs/UI/Hand Slot Selection/CommonTooltipUI.prefab";
    private const string ShopNpcPrefab = "Assets/Prefabs/Interactables/ShopNPC.prefab";
    private const string EnhanceNpcPrefab = "Assets/Prefabs/Interactables/EnhanceShopNPC.prefab";

    private static TMP_FontAsset _font;
    private static Sprite _sprite;

    private static readonly Color WindowColor = new Color(0.11f, 0.09f, 0.08f, 0.97f);
    private static readonly Color CardColor = new Color(0.20f, 0.17f, 0.15f, 1f);
    private static readonly Color ButtonColor = new Color(0.36f, 0.27f, 0.13f, 1f);
    private static readonly Color SubText = new Color(0.80f, 0.78f, 0.74f, 1f);

    [MenuItem("Tools/Shop/Build Shop Popup Prefab")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");

        _font = FindFont();
        _sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        // ── Root Canvas ──
        var root = new GameObject("ShopPopupUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var ui = root.AddComponent<ShopPopupUI>();

        // ── Panel (전체 화면 dim) ──
        var panel = NewRect("Panel", root.transform);
        Stretch(panel);
        var dim = panel.gameObject.AddComponent<Image>();
        dim.color = new Color(0, 0, 0, 0.6f);

        var window = NewRect("Window", panel);
        window.sizeDelta = new Vector2(1520, 680);
        AddImage(window, WindowColor);

        var title = NewText("Title", window, "상점", 46, TextAlignmentOptions.MidlineLeft, Color.white);
        Place(title.rectTransform, new Vector2(0, 1), new Vector2(40, -28), new Vector2(500, 64));
        title.fontStyle = FontStyles.Bold;

        var gold = NewText("GoldText", window, "보유 골드  0 G", 38, TextAlignmentOptions.MidlineRight, Color.white);
        Place(gold.rectTransform, new Vector2(1, 1), new Vector2(-124, -28), new Vector2(620, 64));

        var close = NewButton("CloseButton", window, "X", 34, new Color(0.45f, 0.18f, 0.18f, 1f), out _);
        Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(-32, -28), new Vector2(64, 64));

        var slots = NewRect("Slots", window);
        slots.anchorMin = Vector2.zero; slots.anchorMax = Vector2.one; slots.pivot = new Vector2(0.5f, 0.5f);
        slots.offsetMin = new Vector2(40, 80); slots.offsetMax = new Vector2(-40, -112);
        var hlg = slots.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 24;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        var empty = NewText("EmptyLabel", window, "진열된 상품이 없습니다", 34, TextAlignmentOptions.Center, SubText);
        Place(empty.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 80));
        empty.gameObject.SetActive(false);

        var hint = NewText("Hint", window, "F / ESC : 닫기", 24, TextAlignmentOptions.Center, SubText);
        Place(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(600, 40));

        var slot = BuildSlot(slots);

        // ── 참조 연결 ──
        var so = new SerializedObject(ui);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("goldText").objectReferenceValue = gold;
        so.FindProperty("closeButton").objectReferenceValue = close;
        so.FindProperty("slotContainer").objectReferenceValue = slots;
        so.FindProperty("slotTemplate").objectReferenceValue = slot;
        so.FindProperty("emptyLabel").objectReferenceValue = empty.gameObject;
        so.ApplyModifiedPropertiesWithoutUndo();

        panel.gameObject.SetActive(false);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        SetupShopNpc();
        AssetDatabase.SaveAssets();
        Debug.Log($"[ShopPopupUIBuilder] Built {PrefabPath} (font: {(_font != null ? _font.name : "default")})");
    }

    // ─── 메뉴 명령 ───────────────────────────────────────────────────


    // ── ShopNPC 프리팹 정리: 바닥 진열품 제거 + 말 걸기용 콜라이더 ──
    [MenuItem("Tools/Shop/Setup ShopNPC (remove floor items)")]
    public static void SetupShopNpc()
    {
        var root = PrefabUtility.LoadPrefabContents(ShopNpcPrefab);
        try
        {
            int removed = 0;
            foreach (var s in root.GetComponentsInChildren<SellItem>(true))
            {
                Object.DestroyImmediate(s.gameObject);
                removed++;
            }

            string colInfo = "기존 콜라이더 유지";
            if (root.GetComponent<Collider2D>() == null)
            {
                var reference = AssetDatabase.LoadAssetAtPath<GameObject>(EnhanceNpcPrefab);
                var refCol = reference != null ? reference.GetComponent<CircleCollider2D>() : null;
                var col = root.AddComponent<CircleCollider2D>();
                if (refCol != null)
                {
                    col.isTrigger = refCol.isTrigger;
                    col.radius = refCol.radius;
                    col.offset = refCol.offset;
                    root.layer = reference.layer;
                }
                else { col.isTrigger = true; col.radius = 0.9f; }
                colInfo = $"CircleCollider2D 추가 (layer={LayerMask.LayerToName(root.layer)}, r={col.radius})";
            }

            PrefabUtility.SaveAsPrefabAsset(root, ShopNpcPrefab);
            Debug.Log($"[ShopPopupUIBuilder] ShopNPC: 바닥 진열품 {removed}개 제거, {colInfo}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── helpers ──
    private static TMP_FontAsset FindFont()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(FontSourcePrefab);
        if (src != null)
        {
            var t = src.GetComponentInChildren<TextMeshProUGUI>(true);
            if (t != null && t.font != null) return t.font;
        }
        return TMP_Settings.defaultFontAsset;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static Image AddImage(RectTransform rt, Color color)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = _sprite;
        img.type = Image.Type.Sliced;
        img.color = color;
        return img;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, TextAlignmentOptions align, Color color)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    private static Button NewButton(string name, Transform parent, string label, float size, Color color, out TextMeshProUGUI labelText)
    {
        var rt = NewRect(name, parent);
        var img = AddImage(rt, color);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        btn.colors = colors;
        labelText = NewText("Label", rt, label, size, TextAlignmentOptions.Center, Color.white);
        Stretch(labelText.rectTransform);
        return btn;
    }


    // 상품 카드 템플릿(아이콘/이름/설명/가격/구매 버튼)을 만들고 ShopPopupSlot 참조를 연결한다.
    // 흔들기/반투명은 안쪽 Content 에 건다(레이아웃 그룹이 카드 위치를 잡으므로).
    private static ShopPopupSlot BuildSlot(RectTransform parent)
    {
        var card = NewRect("SlotTemplate", parent);
        card.sizeDelta = new Vector2(264, 460);
        var slot = card.gameObject.AddComponent<ShopPopupSlot>();

        var content = NewRect("Content", card);
        Stretch(content);
        AddImage(content, CardColor);
        var group = content.gameObject.AddComponent<CanvasGroup>();

        var iconRt = NewRect("Icon", content);
        Place(iconRt, new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(128, 128));
        var icon = iconRt.gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        var name = NewText("Name", content, "Item Name", 28, TextAlignmentOptions.Center, Color.white);
        Place(name.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -164), new Vector2(236, 44));
        name.fontStyle = FontStyles.Bold;
        name.overflowMode = TextOverflowModes.Ellipsis;

        var desc = NewText("Description", content, "Description", 20, TextAlignmentOptions.Top, SubText);
        Place(desc.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -214), new Vector2(236, 132));
        desc.textWrappingMode = TextWrappingModes.Normal;
        desc.overflowMode = TextOverflowModes.Ellipsis;

        var price = NewText("Price", content, "0 G", 32, TextAlignmentOptions.Center, ShopPopupUI.GoldColor);
        Place(price.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 92), new Vector2(236, 44));
        price.fontStyle = FontStyles.Bold;

        var buy = NewButton("BuyButton", content, "구매", 28, ButtonColor, out var buyLabel);
        Place((RectTransform)buy.transform, new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(208, 58));

        var so = new SerializedObject(slot);
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("nameText").objectReferenceValue = name;
        so.FindProperty("descText").objectReferenceValue = desc;
        so.FindProperty("priceText").objectReferenceValue = price;
        so.FindProperty("buyButton").objectReferenceValue = buy;
        so.FindProperty("buyLabel").objectReferenceValue = buyLabel;
        so.FindProperty("canvasGroup").objectReferenceValue = group;
        so.ApplyModifiedPropertiesWithoutUndo();

        card.gameObject.SetActive(false);
        return slot;
    }
}
#endif

using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// OptionScene(일시정지 메뉴 + 설정창)에 UI_Panel.png 스킨을 입히는 도구. (메뉴: Tools > UI 스킨)
/// 버튼 이벤트, 슬라이더 연결, 다국어 연결은 건드리지 않고 모양과 배치만 바꾼다. 여러 번 눌러도 안전하다.
/// </summary>
public static class OptionSceneSkinTool
{
    private const string ScenePath = "Assets/Scenes/OptionScene.unity";
    private static readonly Color TextLight = new Color32(0xEF, 0xE9, 0xD5, 0xFF);
    private static readonly Color TextDark = new Color32(0x2A, 0x20, 0x30, 0xFF);
    private static readonly Color FillRed = new Color32(0xEA, 0x40, 0x5B, 0xFF);
    private static readonly Color FieldTint = Color.white;

    private static Dictionary<string, Sprite> _sheet;

    [MenuItem("Tools/UI 스킨/옵션 씬에 스킨 붙이기", priority = 2)]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[UI 스킨] 플레이 중에는 씬을 고칠 수 없습니다. 플레이를 멈추고 다시 눌러 주세요.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        _sheet = AssetDatabase.LoadAllAssetsAtPath(UISkinApplyTool.SheetPath).OfType<Sprite>()
            .GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());

        Transform root = scene.GetRootGameObjects().First(g => g.name == "Canvas_Pause").transform;
        Transform background = root.Find("Panel_BackGround");
        SkinPauseMenu(background.Find("Panel_SelectWork"));
        SkinSettings(background.Find("Image_Option"));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        UISkinPart.ApplyAll();
        Debug.Log("[UI 스킨] OptionScene 적용 완료");
    }

    // ─── 일시정지 메뉴 ─────────────────────────────────────────────
    private static void SkinPauseMenu(Transform panel)
    {
        Skin(panel, UISkinRole.WindowFrame);
        Place(panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(220f, 190f));
        Comp<UIOpenMotion>(panel).kind = UIOpenMotion.Kind.Window;

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(30, 30, 24, 24);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        foreach (string name in new[] { "Button_Continue", "Button_Option", "Button_Exit" })
        {
            Transform button = panel.Find(name);
            Skin(button, UISkinRole.SmallButton);
            ((RectTransform)button).sizeDelta = new Vector2(160f, 32f);
            Comp<UIHoverMotion>(button);
            Style(button.GetComponentInChildren<TMP_Text>(true), 14f, TextLight, TextAlignmentOptions.Center);
        }
    }

    // ─── 설정창 ───────────────────────────────────────────────────
    private static void SkinSettings(Transform window)
    {
        Skin(window, UISkinRole.WindowFrame);
        Place(window, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(460f, 290f));
        Comp<UIOpenMotion>(window).kind = UIOpenMotion.Kind.Window;

        // 제목: 창 위쪽 테두리에 걸치는 빨간 띠
        Transform title = window.Find("Text_NameThisImage");
        Image banner = Child(window, "Skin_TitleBanner");
        banner.transform.SetAsFirstSibling(); // 제목 글자보다 항상 뒤에 그려지게 한다
        Paint(banner, "UI_Panel_Red_Tab", Image.Type.Sliced);
        Place(banner.transform, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -15f), new Vector2(180f, 30f));
        Place(title, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -15f), new Vector2(170f, 30f));
        Style(title.GetComponent<TMP_Text>(), 18f, TextLight, TextAlignmentOptions.Center);

        // 목록: 항목이 세 줄뿐이라 스크롤바 없이 고정한다.
        Transform scroll = window.Find("Scroll View_Option");
        Skin(scroll, UISkinRole.InnerPanel);
        Stretch(scroll, 30f, 30f, 50f, 52f);
        var scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.verticalScrollbar = null;
        scrollRect.horizontal = false;
        scrollRect.vertical = false;
        scroll.Find("Scrollbar Vertical").gameObject.SetActive(false);

        Transform viewport = scroll.Find("Viewport");
        Stretch(viewport, 0f, 0f, 0f, 0f);
        Transform content = viewport.Find("Content");
        Place(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(.5f, 1f), Vector2.zero, new Vector2(0f, 180f));
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        SkinRow(content.Find("Panel_BGMOption"));
        SkinRow(content.Find("Panel_SFXOption"));
        SkinRow(content.Find("Panel_LanguageOption"));
        SkinSlider(content.Find("Panel_BGMOption"));
        SkinSlider(content.Find("Panel_SFXOption"));
        SkinDropdown(content.Find("Panel_LanguageOption/Dropdown"));

        // 닫기 버튼
        Transform exit = window.Find("Button_Exit");
        Skin(exit, UISkinRole.SmallButton);
        Place(exit, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(.5f, .5f), new Vector2(0f, 33f), new Vector2(110f, 28f));
        Comp<UIHoverMotion>(exit);
        Style(exit.GetComponentInChildren<TMP_Text>(true), 14f, TextLight, TextAlignmentOptions.Center);
    }

    private static void SkinRow(Transform row)
    {
        ((RectTransform)row).sizeDelta = new Vector2(((RectTransform)row).sizeDelta.x, 34f);
        var image = row.GetComponent<Image>();
        Paint(image, "UI_Panel_22", Image.Type.Sliced);

        var bullet = row.Find("Skin_Bullet");
        if (bullet != null) bullet.gameObject.SetActive(false);

        Transform name = row.Find("Text_OptionName");
        Place(name, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, .5f), new Vector2(24f, 0f), new Vector2(120f, 0f));
        Style(name.GetComponent<TMP_Text>(), 14f, TextLight, TextAlignmentOptions.MidlineLeft);

        Transform line = row.Find("Skin_Divider");
        if (line != null) line.gameObject.SetActive(false);
    }

    private static void SkinSlider(Transform row)
    {
        Transform t = row.Find("Slider");
        var slider = t.GetComponent<Slider>();
        Place(t, new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-44f, 0f), new Vector2(180f, 20f));

        Transform back = t.Find("Background");
        // The transparent background retains the full pointer area at both ends of the rail.
        Flat(back, Color.clear).raycastTarget = true;
        Stretch(back, 0, 0, 0, 0);
        var track = Child(back, "Skin_Track");
        Flat(track.transform, new Color32(87, 74, 86, 255)).raycastTarget = false;
        Place(track.transform, new Vector2(0,.5f), new Vector2(1,.5f), Vector2.one * .5f, Vector2.zero, new Vector2(-10,5));
        var area = t.Find("Fill Area");
        Place(area, new Vector2(0,.5f), new Vector2(1,.5f), Vector2.one * .5f, Vector2.zero, new Vector2(-10,5));
        var fill = slider.fillRect;
        fill.sizeDelta = Vector2.zero;
        Flat(fill, new Color32(181, 115, 131, 255)).raycastTarget = false;
        Stretch(slider.handleRect.parent, 5, 5, 0, 0);
        slider.handleRect.sizeDelta = new Vector2(4, -6);
        Flat(slider.handleRect, TextLight);
        // 수치 표시
        Transform old = row.Find("Skin_Value");
        GameObject valueObject = old != null ? old.gameObject : new GameObject("Skin_Value", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        valueObject.layer = row.gameObject.layer;
        valueObject.transform.SetParent(row, false);
        Place(valueObject.transform, new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-6f, 0f), new Vector2(34f, 20f));
        var value = valueObject.GetComponent<TMP_Text>();
        value.raycastTarget = false;
        value.text = Mathf.RoundToInt(slider.normalizedValue * 100f).ToString();
        Style(value, 13f, TextLight, TextAlignmentOptions.MidlineRight);

        var percent = Comp<SliderPercentText>(t);
        percent.slider = slider;
        percent.label = value;
    }

    private static void SkinDropdown(Transform dropdown)
    {
        Place(dropdown, new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-6f, 0f), new Vector2(218f, 25f));
        Paint(dropdown.GetComponent<Image>(), "UI_Panel_22", Image.Type.Sliced);
        dropdown.GetComponent<Image>().color = FieldTint;

        Transform label = dropdown.Find("Label");
        Stretch(label, 8f, 24f, 0f, 0f);
        Style(label.GetComponent<TMP_Text>(), 13f, TextLight, TextAlignmentOptions.MidlineLeft);

        Transform arrow = dropdown.Find("Arrow");
        Place(arrow, new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(.5f, .5f), new Vector2(-13f, 0f), new Vector2(9f, 6f));
        Paint(arrow.GetComponent<Image>(), "UI_Panel_34", Image.Type.Simple);

        Transform template = dropdown.Find("Template");
        Place(template, Vector2.up, Vector2.one, new Vector2(.5f, 0f), new Vector2(0, 2), new Vector2(0, 70));
        Paint(template.GetComponent<Image>(), "UI_Panel_22", Image.Type.Sliced);
        template.GetComponent<Image>().color = FieldTint;
        var templateScroll = template.GetComponent<ScrollRect>();
        templateScroll.verticalScrollbar = null;
        template.Find("Scrollbar").gameObject.SetActive(false);

        Transform viewport = template.Find("Viewport");
        Stretch(viewport, 3f, 3f, 3f, 3f);
        Transform item = viewport.Find("Content/Item");
        ((RectTransform)item).sizeDelta = new Vector2(0f, 22f);

        Transform itemBack = item.Find("Item Background");
        var itemImage = itemBack.GetComponent<Image>();
        itemImage.sprite = null;
        itemImage.color = Color.white;
        var toggle = item.GetComponent<Toggle>();
        ColorBlock colors = toggle.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = colors.normalColor;
        colors.pressedColor = colors.normalColor;
        colors.selectedColor = colors.normalColor;
        toggle.colors = colors;
        Comp<UIHoverMotion>(item);

        Transform check = item.Find("Item Checkmark");
        Place(check, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(.5f, .5f), new Vector2(12f, 0f), new Vector2(8f, 8f));
        Paint(check.GetComponent<Image>(), "UI_Panel_20", Image.Type.Simple);

        Transform itemLabel = item.Find("Item Label");
        Stretch(itemLabel, 24f, 6f, 0f, 0f);
        Style(itemLabel.GetComponent<TMP_Text>(), 13f, TextLight, TextAlignmentOptions.MidlineLeft);
    }

    // ─── 공용 도우미 ─────────────────────────────────────────────
    private static T Comp<T>(Transform t) where T : Component
    {
        var c = t.GetComponent<T>();
        return c != null ? c : t.gameObject.AddComponent<T>();
    }

    private static Image Flat(Transform target, Color color)
    {
        if (target.TryGetComponent<UISkinPart>(out var skin)) Object.DestroyImmediate(skin);
        var image = target.GetComponent<Image>();
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = color;
        return image;
    }

    private static void Skin(Transform t, UISkinRole role, bool keepColor = false)
    {
        var part = Comp<UISkinPart>(t);
        part.role = role;
        part.keepOwnColor = keepColor;
        part.Apply();
    }

    private static Image Child(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        GameObject go;
        if (t != null) go = t.gameObject;
        else
        {
            go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectorUiImage));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
        }
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static void Paint(Image image, string sprite, Image.Type type)
    {
        if (!_sheet.TryGetValue(sprite, out Sprite s)) { Debug.LogWarning($"[UI 스킨] UI_Panel.png 에서 '{sprite}' 조각을 찾지 못했습니다."); return; }
        image.sprite = s;
        image.type = type;
        image.color = Color.white;
        image.pixelsPerUnitMultiplier = 1f;
    }

    private static void Style(TMP_Text text, float size, Color color, TextAlignmentOptions alignment)
    {
        if (text == null) return;
        text.enableAutoSizing = false;
        text.fontSize = Mathf.Round(size * .8f);
        PixelText.Configure(text, text.name == "Skin_Value" ? 10 : 12, bold: false);
        text.color = color;
        text.alignment = alignment;
    }

    private static void Place(Transform t, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var rt = (RectTransform)t;
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = new Vector2(Mathf.Round(position.x * .8f), Mathf.Round(position.y * .8f));
        rt.sizeDelta = new Vector2(Mathf.Round(size.x * .8f), Mathf.Round(size.y * .8f));
        rt.localScale = Vector3.one;
    }

    private static void Stretch(Transform t, float left, float right, float top, float bottom)
    {
        var rt = (RectTransform)t;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(.5f, .5f);
        rt.offsetMin = new Vector2(Mathf.Round(left * .8f), Mathf.Round(bottom * .8f));
        rt.offsetMax = new Vector2(-Mathf.Round(right * .8f), -Mathf.Round(top * .8f));
        rt.localScale = Vector3.one;
    }
}

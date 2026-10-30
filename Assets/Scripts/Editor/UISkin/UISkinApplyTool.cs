using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SkinEntry = UISkinSettings.SkinEntry;

/// <summary>
/// UI_Panel.png 스킨을 인게임 UI 프리팹에 붙이는 도구. (메뉴: Tools > UI 스킨)
///
/// 적용 대상: 플레이어 HUD(체력·보호막·가드·스킬 칸), 미니맵, 아이템 주머니, 장착 정보, 공용 툴팁.
/// 소환수 선택(HandSlotSelectionUI)·보상 선택(RewardSelectionUI)·증강 선택은 기존 UI 그대로 두고 건드리지 않는다.
///
/// · 여러 번 눌러도 안전하다(이미 붙어 있으면 갱신만 한다).
/// · 원래 있던 단색 테두리(Frame1006 등)는 지우지 않고 꺼 둔다. 되돌리려면 Git 으로 프리팹을 되돌리면 된다.
/// </summary>
public static class UISkinApplyTool
{
    public const string SheetPath    = "Assets/Resources/Sprites/UI/UI_Panel.png";
    public const string SettingsPath = "Assets/Resources/UI/UISkinSettings.asset";
    private const string P = "Assets/Prefabs/UI/";

    private static readonly StringBuilder Log = new StringBuilder();

    // ─── 메뉴 ───────────────────────────────────────────────────────
    [MenuItem("Tools/UI 스킨/스킨 · 연출 설정 열기", priority = 0)]
    public static void OpenSettings()
    {
        var s = EnsureSettings();
        Selection.activeObject = s;
        EditorGUIUtility.PingObject(s);
    }

    [MenuItem("Tools/UI 스킨/인게임 UI 프리팹에 스킨 붙이기", priority = 1)]
    public static void ApplyAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[UI 스킨] 플레이 중에는 프리팹을 고칠 수 없습니다. 플레이를 멈추고 다시 눌러 주세요.");
            return;
        }
        EnsureSettings();
        Log.Clear();
        Edit(P + "Player State/PlayerStateUI.prefab", SkinPlayerState);
        Edit(P + "MiniMap.prefab", SkinMiniMap);
        Edit(P + "PouchUI.prefab", SkinPouch);
        Edit(P + "PouchItemSlot.prefab", SkinPouchSlot);
        Edit(P + "SkillExplainUI.prefab", SkinSkillExplain);
        Edit(P + "Hand Slot Selection/CommonTooltipUI.prefab", SkinTooltip);
        AssetDatabase.SaveAssets();
        UISkinPart.ApplyAll();
        Debug.Log("[UI 스킨] 적용 완료\n" + Log);
    }

    // ─── 설정 파일 ─────────────────────────────────────────────────
    public static UISkinSettings EnsureSettings()
    {
        var s = AssetDatabase.LoadAssetAtPath<UISkinSettings>(SettingsPath);
        if (s != null) return s;
        s = ScriptableObject.CreateInstance<UISkinSettings>();
        FillDefaultSkins(s);
        FillDefaultMotions(s);
        AssetDatabase.CreateAsset(s, SettingsPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[UI 스킨] 설정 파일을 만들었습니다: {SettingsPath}");
        return s;
    }

    private static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

    /// <summary>①~③ 모양을 UI_Panel.png 기본 조합으로 채운다.</summary>
    public static void FillDefaultSkins(UISkinSettings s)
    {
        var sheet = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>()
            .GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.First());
        SkinEntry E(string sprite, Color? color = null)
        {
            Sprite sp = null;
            if (sprite != null && !sheet.TryGetValue(sprite, out sp))
                Debug.LogWarning($"[UI 스킨] UI_Panel.png 에서 '{sprite}' 조각을 찾지 못했습니다.");
            return new SkinEntry { sprite = sp, color = color ?? Color.white, drawMode = SkinEntry.DrawMode.Sliced, pixelScale = 1f };
        }

        s.windowFrame     = E("UI_Panel_Red_Big");                       // 빨간 기둥 장식 창
        s.innerPanel      = E("UI_Panel_Black_Big");                     // 어두운 둥근 패널
        s.infoPanel       = E("UI_Panel_6");                             // C·V 흰 장식 테두리, 바깥 창의 보라색 바탕 유지
        s.itemListBack    = E("UI_Panel_Red_Half");                      // 정렬 탭 아래 목록 바탕
        s.tooltipBack     = E("UI_Panel_Black_Big", new Color(1f, 1f, 1f, 0.97f));
        s.tabOuter        = E("UI_Panel_Red_Half");                      // 아래가 열린 탭 모양
        s.tabFace         = E("UI_Panel_White_Half");                    // 밝은 탭 → 선택색이 그대로 입혀짐
        s.smallButton     = E("UI_Panel_31");                            // 작은 빨간 버튼
        s.divider         = E(null, Hex("A63245"));                      // 빨간 단색 선
        s.hpBarFrame      = E("UI_Panel_14");                            // 빨간 게이지 틀
        s.gaugeFrame      = E("UI_Panel_7");                             // 얇은 빨간 틀
        s.gaugeTrack      = E(null, Hex("201825"));                      // 빈 칸 = 아주 어두운 보라
        s.gaugeFill       = E("UI_Panel_16");                            // 흰 하이라이트 바 (색은 각 바가 정함)
        s.skillSlotBack   = E("UI_Panel_Black_Big");
        s.skillSlotFrame  = E("UI_Panel_7");
        s.miniMapFrame    = E("UI_Panel_4");                             // 매듭 장식 빨간 틀 (속 빔)
        s.itemSlot        = E("UI_Panel_12");                            // 밝은 매듭 틀 + 어두운 속 (등급색이 입혀짐)
        s.itemDetailFrame = E("UI_Panel_11");                            // 빨간 매듭 틀 + 어두운 속
        s.selectionFrame  = E("UI_Panel_6", Hex("FAC94C"));              // 밝은 매듭 틀 (속 빔) 금색
        s.smallIconSlot   = E("UI_Panel_7");
        EditorUtility.SetDirty(s);
    }

    /// <summary>④~⑧ 연출 값을 기본값으로.</summary>
    public static void FillDefaultMotions(UISkinSettings s)
    {
        s.motionEnabled = true;
        s.useUnscaledTime = true;
        s.punchCurve = MotionCurves.Punch();
        s.windowOpen = OpenMotion.Window();
        s.popupOpen = OpenMotion.Popup();
        s.hpBar = BarMotion.Hp();
        s.guardBar = BarMotion.Guard();
        s.skill = new SkillMotion();
        s.hover = new HoverMotion();
        EditorUtility.SetDirty(s);
    }

    // ─── 프리팹별 적용 ────────────────────────────────────────────
    private static void SkinPlayerState(GameObject root)
    {
        // 스킬 칸: 어두운 바탕 + 아이콘 위에 얇은 빨간 테두리 + 준비 완료 연출
        var skills = Find(root, "Panel_PlayerState/Panel_Skill");
        if (skills != null)
        {
            foreach (var name in new[] { "Icon_Dash_Skill", "Icon_Guard", "Icon_MinionSkill" })
            {
                var slot = skills.Find(name);
                if (slot == null) { Log.AppendLine("    - 못 찾음: " + name); continue; }
                Skin(slot, UISkinRole.SkillSlotBack);
                var icon = slot.Find("Image_SkillIcon");
                var frame = Child(slot, "Skin_Frame", 0f);
                frame.transform.SetSiblingIndex(icon != null ? icon.GetSiblingIndex() + 1 : slot.childCount - 1);
                Skin(frame.transform, UISkinRole.SkillSlotFrame);

                var motion = Comp<UISkillSlotMotion>(slot);
                var cool = icon != null ? icon.Find("Image_CoolTime") : null;
                motion.cooldown = cool != null ? cool.GetComponent<Image>() : null;
                motion.frame = frame;
                motion.punchTarget = (RectTransform)slot;
            }
        }

        SkinBar(Find(root, "Panel_PlayerState/Panel_HP/BG_HPBar"), UIBarMotion.Kind.Hp, UISkinRole.HpBarFrame, 5f,
                "Field_HPBar", "Field_ShieldBar", Hex("EA405B"));
        SkinBar(Find(root, "Panel_PlayerState/Panel_HP/BG_GuardGauge"), UIBarMotion.Kind.Guard, UISkinRole.GaugeFrame, 2f,
                "Field_GuardGauge", null, Hex("20C1E8"));
    }

    /// <summary>게이지 하나: 바깥=틀 스프라이트, 안쪽=어두운 트랙, 채움=하이라이트 바, 그 뒤에 피해 잔상 바.</summary>
    private static void SkinBar(Transform bg, UIBarMotion.Kind kind, UISkinRole frameRole, float inset, string fillName, string shieldName, Color fillColor)
    {
        if (bg == null) return;
        HideOld(bg);
        var frame = Skin(bg, frameRole);

        var track = bg.Find("Track1006");
        if (track == null) { Log.AppendLine($"    - 못 찾음: {bg.name}/Track1006"); return; }
        Stretch(track, inset);                         // 틀의 빈 창에 정확히 맞춘다
        Skin(track, UISkinRole.GaugeTrack);

        var fill = track.Find(fillName);
        var shield = shieldName != null ? track.Find(shieldName) : null;
        if (fill == null) { Log.AppendLine($"    - 못 찾음: {fillName}"); return; }
        FillStretch(fill); Skin(fill, UISkinRole.GaugeFill, keepColor: true);
        fill.GetComponent<Image>().color = fillColor;   // 체력 빨강 / 가드 파랑 (가드는 런타임에 PlayerStateUI 의 색으로 덮인다)
        if (shield != null) { FillStretch(shield); Skin(shield, UISkinRole.GaugeFill, keepColor: true); shield.GetComponent<Image>().color = Color.white; }

        var trail = Child(track, "Skin_DamageTrail", 0f);
        Skin(trail.transform, UISkinRole.GaugeFill, keepColor: true);
        trail.color = kind == UIBarMotion.Kind.Hp ? BarMotion.Hp().trailColor : BarMotion.Guard().trailColor;
        trail.enabled = false;                         // 피해를 입을 때만 연출이 켠다
        // 그리는 순서: 보호막(맨 뒤) → 잔상 → 채움(맨 앞)
        int i = 0;
        if (shield != null) shield.SetSiblingIndex(i++);
        trail.transform.SetSiblingIndex(i++);
        fill.SetSiblingIndex(i);

        var m = Comp<UIBarMotion>(bg);
        m.kind = kind;
        m.fill = fill.GetComponent<Image>();
        m.trail = trail;
        m.shield = shield != null ? shield.GetComponent<Image>() : null;
        m.frame = frame != null ? frame.GetComponent<Image>() : null;
        m.punchTarget = (RectTransform)bg;
    }

    private static void SkinMiniMap(GameObject root)
    {
        var hud = Find(root, "Image_MiniMap");
        if (hud != null)
        {
            HideOld(hud.Find("Background"));
            var frame = Child(hud, "Skin_Frame", 0f);
            frame.transform.SetAsLastSibling();
            Skin(frame.transform, UISkinRole.MiniMapFrame, onTop: true); // 방 아이콘이 나중에 생겨도 틀이 위에
        }
        var full = Find(root, "MiniMapUI");
        if (full != null) Comp<UIOpenMotion>(full).kind = UIOpenMotion.Kind.Window;
    }

    private static void SkinPouch(GameObject root)
    {
        var win = Find(root, "PouchWindow");
        if (win == null) return;
        Skin(win, UISkinRole.WindowFrame);
        Comp<UIOpenMotion>(win).kind = UIOpenMotion.Kind.Window;

        Skin(Find(root, "PouchWindow/InventoryPanel"), UISkinRole.InfoPanel);
        Skin(Find(root, "PouchWindow/ItemDetails"), UISkinRole.InfoPanel);
        Skin(win.Find("InventoryPanel/ItemArea"), UISkinRole.ItemListBack);
        for (int i = 0; i < 3; i++)
        {
            var sort = Find(root, $"PouchWindow/InventoryPanel/Sort{i}");
            if (sort == null) continue;
            Skin(sort, UISkinRole.TabOuter);
            var face = sort.Find("Face");
            if (face == null) continue;
            Skin(face, UISkinRole.TabFace, keepColor: true); // 선택/비선택 색은 PouchUI 가 칠한다
            Comp<UIHoverMotion>(face).target = (RectTransform)sort;
        }
        Clear(Find(root, "PouchWindow/InventoryPanel/ItemScroll/Viewport"));
        Clear(Find(root, "PouchWindow/ItemDetails/DetailScroll/Viewport"));
        Skin(Find(root, "PouchWindow/ItemDetails/IconFrame"), UISkinRole.ItemDetailFrame);
        Clear(Find(root, "PouchWindow/ItemDetails/IconFrame/Backing"));
        Skin(Find(root, "PouchWindow/ItemDetails/Divider"), UISkinRole.Divider);

        var pouch = root.GetComponent<PouchUI>();
        if (pouch != null)
        {
            SetColor(pouch, "activeSortColor", new Color(1f, 0.62f, 0.66f, 1f));   // 선택된 탭: 밝은 붉은색
            SetColor(pouch, "inactiveSortColor", new Color(0.42f, 0.37f, 0.5f, 1f)); // 나머지 탭: 어두운 보라
        }
    }

    private static void SkinPouchSlot(GameObject root)
    {
        var t = root.transform;
        Skin(t, UISkinRole.ItemSlot, keepColor: true); // 등급색은 PouchSlotUI 가 칠한다
        Clear(t.Find("Backing"));

        var slotUI = root.GetComponent<PouchSlotUI>();
        if (slotUI != null)
        {
            var so = new SerializedObject(slotUI);
            var frameProp = so.FindProperty("frameImage");
            if (frameProp != null && frameProp.objectReferenceValue != root.GetComponent<Image>())
            {
                frameProp.objectReferenceValue = root.GetComponent<Image>(); // 등급색이 새 틀에 입혀지도록
                Log.AppendLine("    · PouchSlotUI.frameImage → 바깥 틀로 연결");
            }
            var common = so.FindProperty("commonColor");
            if (common != null) common.colorValue = new Color(0.8f, 0.76f, 0.86f, 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var sel = t.Find("Selection");
        if (sel != null)
        {
            foreach (Transform line in sel) line.gameObject.SetActive(false);
            var p = Skin(sel, UISkinRole.SelectionFrame);
            if (p != null) p.GetComponent<Image>().raycastTarget = false;
        }
        Comp<UIHoverMotion>(t);
    }

    private static void SkinSkillExplain(GameObject root)
    {
        const string L = "LoadoutPanel/";
        var stats = Find(root, L + "StatsPanel");
        if (stats != null)
        {
            HideOld(stats);
            Skin(stats, UISkinRole.WindowFrame);
            Comp<UIOpenMotion>(stats).kind = UIOpenMotion.Kind.Window;
            foreach (var n in new[] { "Summary", "PlayerStats" }) Skin(stats.Find(n), UISkinRole.InfoPanel);
            var summary = stats.Find("Summary");
            if (summary != null)
                for (int i = 0; i < 5; i++) Skin(summary.Find("Icons/IconSlot" + i), UISkinRole.SmallIconSlot);
            var expand = stats.Find("ExpandButton");
            if (expand != null) { Skin(expand, UISkinRole.SmallButton); Comp<UIHoverMotion>(expand); }
        }

        var details = Find(root, L + "DetailsPanel");
        if (details != null)
        {
            HideOld(details);
            Skin(details, UISkinRole.WindowFrame);
            Comp<UIOpenMotion>(details).kind = UIOpenMotion.Kind.Popup;
            foreach (var n in new[] { "Equipment", "Guard", "MinionPanel" })
                Skin(details.Find(n), UISkinRole.InfoPanel);
        }
    }

    private static void SkinTooltip(GameObject root)
    {
        var t = root.transform;
        var tip = root.GetComponent<CommonTooltipUI>();
        if (tip != null)
        {
            var prop = new SerializedObject(tip).FindProperty("tooltipPanel");
            if (prop != null && prop.objectReferenceValue is Component c) t = c.transform;
        }
        Skin(t, UISkinRole.TooltipBack);
        var layout = t.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            var p = layout.padding; // 둥근 틀 안쪽으로 글자가 들어오게 여백 확보
            layout.padding = new RectOffset(Mathf.Max(p.left, 9), Mathf.Max(p.right, 9), Mathf.Max(p.top, 8), Mathf.Max(p.bottom, 8));
        }
        Comp<UIOpenMotion>(t).kind = UIOpenMotion.Kind.Popup;
    }

    // ─── 공용 도우미 ─────────────────────────────────────────────
    private static void Edit(string path, System.Action<GameObject> apply)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Log.AppendLine("  (없음) " + path); return; }
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Log.AppendLine("  ✔ " + path);
            apply(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        catch (System.Exception e) { Debug.LogError($"[UI 스킨] {path} 적용 실패: {e}"); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static Transform Find(GameObject root, string path)
    {
        var t = root.transform.Find(path);
        if (t == null) Log.AppendLine("    - 못 찾음: " + path);
        return t;
    }

    private static T Comp<T>(Transform t) where T : Component
    {
        var c = t.GetComponent<T>();
        if (c == null) c = t.gameObject.AddComponent<T>();
        return c;
    }

    private static UISkinPart Skin(Transform t, UISkinRole role, bool keepColor = false, bool onTop = false)
    {
        if (t == null) return null;
        var img = Comp<Image>(t);
        var ownColor = img.color;            // 컴포넌트를 붙이는 순간 OnEnable 이 먼저 칠하므로 원래 색을 기억해 둔다
        var part = Comp<UISkinPart>(t);
        part.role = role;
        part.keepOwnColor = keepColor;
        if (onTop) Comp<UIKeepOnTop>(t);
        if (keepColor) img.color = ownColor;
        part.Apply();
        return part;
    }

    /// <summary>꽉 차게 늘어나는 장식용 자식 이미지(마우스 클릭은 통과).</summary>
    private static Image Child(Transform parent, string name, float inset)
    {
        var t = parent.Find(name);
        GameObject go;
        if (t == null)
        {
            go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
        }
        else go = t.gameObject;
        Stretch(go.transform, inset);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private static void Stretch(Transform t, float inset)
    {
        var rt = (RectTransform)t;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    /// <summary>게이지 채움: 왼쪽에 붙어서 anchorMax.x 로 길이가 정해지도록.</summary>
    private static void FillStretch(Transform t)
    {
        var rt = (RectTransform)t;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(Mathf.Clamp01(rt.anchorMax.x <= 0f ? 1f : rt.anchorMax.x), 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    /// <summary>예전 단색 테두리 묶음(Frame1006)을 지우지 않고 끈다.</summary>
    private static void HideOld(Transform t)
    {
        if (t == null) return;
        var f = t.Find("Frame1006");
        if (f != null && f.gameObject.activeSelf) f.gameObject.SetActive(false);
    }

    /// <summary>보이지 않게 하되 클릭/스크롤 영역은 그대로 둔다.</summary>
    private static void Clear(Transform t)
    {
        if (t == null) return;
        var img = t.GetComponent<Image>();
        if (img == null) return;
        var c = img.color; c.a = 0f; img.color = c;
    }

    private static void SetColor(Object target, string field, Color color)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Log.AppendLine($"    - 필드 없음: {target.GetType().Name}.{field}"); return; }
        p.colorValue = color;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}

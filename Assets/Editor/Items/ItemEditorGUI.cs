using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 아이템(ItemSO) 편집 화면 공용 그리기 코드. 아이템 인스펙터(ItemSOEditor)와
/// 아이템 편집기 창(ItemDatabaseWindow)이 같은 화면을 쓰도록 한곳에 모았다.
///
/// 원칙
///  · 값은 SerializedProperty 로만 바꾼다 → Undo / 저장 표시(dirty)가 그대로 동작한다.
///  · 그리기만 해서는 값이 절대 바뀌지 않는다(DesignerInspectorCheck 의 '표시 중 값 변경' 검사).
///  · 효과 수치는 기획자가 보는 단위(%)로 입력받고 저장은 원래 단위(0.04)로 한다.
/// </summary>
public static class ItemEditorGUI
{
    // ── 효과 종류 ─────────────────────────────────────────────────────
    private static List<Type> effectTypes;

    /// <summary>추가 가능한 효과 종류 전체. 새 ItemEffect 하위 클래스를 만들면 자동으로 포함된다.</summary>
    public static IReadOnlyList<Type> EffectTypes => effectTypes ??= TypeCache.GetTypesDerivedFrom<ItemEffect>()
        .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
        .OrderBy(TypeName, StringComparer.Ordinal).ToList();

    private static ItemEffectInfoAttribute Info(Type t)
        => t == null ? null : (ItemEffectInfoAttribute)Attribute.GetCustomAttribute(t, typeof(ItemEffectInfoAttribute));

    public static string TypeName(Type t) => Info(t)?.Name ?? (t == null ? "비어 있음" : ObjectNames.NicifyVariableName(t.Name));
    public static string TypeHelp(Type t) => Info(t)?.Help ?? "";

    /// <summary>관리 참조 칸에 실제로 들어 있는 효과 종류. 비어 있으면 null.</summary>
    public static Type TypeOf(SerializedProperty element)
    {
        string full = element.managedReferenceFullTypename; // "Assembly-CSharp ItemStatEffect"
        if (string.IsNullOrEmpty(full)) return null;
        string name = full.Substring(full.IndexOf(' ') + 1);
        return EffectTypes.FirstOrDefault(t => t.FullName == name);
    }

    // ── 스타일 ────────────────────────────────────────────────────────
    private static GUIStyle wrapMini, wrapLabel, sectionStyle, wrapArea;
    public static GUIStyle WrapMini => wrapMini ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = false };
    public static GUIStyle WrapLabel => wrapLabel ??= new GUIStyle(EditorStyles.label) { wordWrap = true };
    public static GUIStyle WrapArea => wrapArea ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };
    private static GUIStyle Section => sectionStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };

    public static void Header(string text)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(text, Section);
        var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.35f));
        EditorGUILayout.Space(2);
    }

    private static void Field(SerializedObject so, string name, string label, string tip)
    {
        var p = so.FindProperty(name);
        if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label, tip), true);
    }

    /// <summary>
    /// 편집 중인 글 칸을 닫는다. IMGUI 는 입력 중인 글을 '화면 위 칸 번호'로 기억해서,
    /// 같은 자리에 다른 아이템/효과가 그려지면 입력 중이던 글이 그쪽으로 옮겨 붙는다.
    /// 보여주는 대상이 바뀔 때마다 OnGUI 안에서 부른다. 입력값은 타자마다 이미 저장되므로 잃지 않는다.
    /// </summary>
    public static void EndTextEditing()
    {
        if (Event.current != null) GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;
        EditorGUIUtility.editingTextField = false;
    }

    
// ── 아이템 전체 ───────────────────────────────────────────────────
    /// <summary>아이템 한 개의 전체 편집 화면.</summary>
    public static void DrawItem(SerializedObject so, bool showRegistry = true)
    {
        var item = so.targetObject as ItemSO;
        if (item == null) return;
        so.Update();

        if (Application.isPlaying)
            EditorGUILayout.HelpBox("플레이 중입니다. 아이템 에셋 수정은 플레이를 꺼도 그대로 남습니다.", MessageType.Warning);

        Header("표시");
        Field(so, "itemName", "이름", "게임에 표시되는 이름. 비우면 에셋 파일 이름을 쓴다.");
        Field(so, "description", "설명", "툴팁 · 보상 카드 · 주머니 · 능력창에 표시된다. 앞에 [N티어] 가 자동으로 붙는다.");
        Field(so, "shopDescription", "상점 카드 설명", "비우면 위 '설명'을 그대로 쓴다.\n카드 폭이 좁아 긴 설명이 가격과 겹칠 때만 짧게 채운다.");
        Field(so, "icon", "아이콘", "비어 있으면 바닥 픽업 프리팹의 기본 아이콘을 쓴다.");

        Header("등급 · 중첩 · 등장");
        Field(so, "tier", "등급", "1이 가장 좋고 4가 가장 흔하다. 가격과 색은 등급에서 자동으로 정해진다.");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(new GUIContent("가격 · 색 (자동)", "등급별 가격/색은 ItemTierRules 표 한 곳에서 정한다(코드)."));
            var r = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14));
            r.y += 2; EditorGUI.DrawRect(r, item.TierColor);
            GUILayout.Label($"{item.Price} 골드  ·  {ItemTierRules.Label(item.tier)}");
        }
        Field(so, "maxStack", "최대 보유 수", "같은 아이템을 최대 몇 개까지 가질 수 있는지.");
        Field(so, "availableAsReward", "상점 · 방 보상에 등장", "끄면 무작위 보상/상점에 나오지 않는다(기본 지급 전용 아이템 등).");

        Header("효과");
        EditorGUILayout.LabelField("주머니에 들어 있는 동안 적용된다. 아래 '표시 문구'가 주머니의 효과 문구가 된다(비우면 자동 문구).", WrapMini);
        DrawEffects(so.FindProperty("effects"), item, false);

        so.ApplyModifiedProperties();

        Header("게임 표시 미리보기");
        DrawPreview(item);

        var warnings = Warnings(item);
        if (warnings.Count > 0)
        {
            Header("확인 필요");
            foreach (var w in warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
        }

        if (showRegistry) DrawRegistry(item);
    }

    // ── 효과 목록 ─────────────────────────────────────────────────────
    /// <summary>효과 목록. compact=true 면 표 보기용으로 메모/설명을 생략한다.</summary>
    public static void DrawEffects(SerializedProperty effects, ItemSO item, bool compact)
    {
        if (effects == null) return;
        int remove = -1, moveFrom = -1, moveTo = -1;
        float prevLabel = EditorGUIUtility.labelWidth;
        if (compact) EditorGUIUtility.labelWidth = 92;
        try
        {
            for (int i = 0; i < effects.arraySize; i++)
            {
                var e = effects.GetArrayElementAtIndex(i);
                var type = TypeOf(e);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"{i + 1}. {TypeName(type)}", EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(i == 0))
                            if (GUILayout.Button(new GUIContent("▲", "위로"), EditorStyles.miniButtonLeft, GUILayout.Width(22))) { moveFrom = i; moveTo = i - 1; }
                        using (new EditorGUI.DisabledScope(i == effects.arraySize - 1))
                            if (GUILayout.Button(new GUIContent("▼", "아래로"), EditorStyles.miniButtonMid, GUILayout.Width(22))) { moveFrom = i; moveTo = i + 1; }
                        if (GUILayout.Button(new GUIContent("삭제", "이 효과를 지운다(Ctrl+Z 로 되돌리기 가능)"), EditorStyles.miniButtonRight, GUILayout.Width(36))) remove = i;
                    }

                    if (type == null)
                    {
                        EditorGUILayout.LabelField("종류가 정해지지 않은 빈 칸입니다. 게임에서는 무시됩니다.", WrapMini);
                        if (GUILayout.Button("종류 선택…")) ShowTypeMenu(effects, i);
                        continue;
                    }
                    if (!compact && TypeHelp(type).Length > 0) EditorGUILayout.LabelField(TypeHelp(type), WrapMini);

                    DrawEffectFields(e, type, compact);

                    var live = item != null && item.effects != null && i < item.effects.Count ? item.effects[i] : null;
                    if (live != null)
                    {
                        EditorGUILayout.LabelField(new GUIContent("→ 게임 표시 (1개): " + live.Describe(1), "주머니 '개당 효과' 에 나오는 문구"), WrapMini);
                        if (!compact) EditorGUILayout.LabelField("치환어: " + live.TokenHelp, WrapMini);
                    }
                }
            }
        }
        finally { EditorGUIUtility.labelWidth = prevLabel; }

        // 칸 순서가 바뀌면 입력 중이던 글이 옆 효과로 옮겨 붙으므로 먼저 편집을 닫는다.
        if (moveFrom >= 0 || remove >= 0) EndTextEditing();
        if (moveFrom >= 0) effects.MoveArrayElement(moveFrom, moveTo);
        if (remove >= 0)
        {
            int before = effects.arraySize;
            effects.DeleteArrayElementAtIndex(remove);
            if (effects.arraySize == before) effects.DeleteArrayElementAtIndex(remove);
        }
        if (moveFrom >= 0 || remove >= 0) effects.serializedObject.ApplyModifiedProperties();

        if (GUILayout.Button(compact ? "+ 효과" : "+ 효과 추가", compact ? EditorStyles.miniButton : GUI.skin.button)) ShowTypeMenu(effects, -1);
    }

    /// <summary>효과 종류 선택 메뉴. index&lt;0 이면 맨 뒤에 새 칸을 만든다.</summary>
    public static void ShowTypeMenu(SerializedProperty effects, int index)
    {
        var target = effects.serializedObject.targetObject;
        string path = effects.propertyPath;
        var menu = new GenericMenu();
        foreach (var t in EffectTypes)
        {
            var type = t;
            menu.AddItem(new GUIContent(TypeName(type), TypeHelp(type)), false, () =>
            {
                if (target == null) return;
                // 메뉴는 다음 프레임에 실행된다 — 그 사이 인스펙터가 바뀌어도 안전하게 새로 연다.
                var so = new SerializedObject(target);
                var arr = so.FindProperty(path);
                int idx = index;
                if (idx < 0 || idx >= arr.arraySize) { idx = arr.arraySize; arr.arraySize++; }
                arr.GetArrayElementAtIndex(idx).managedReferenceValue = Activator.CreateInstance(type);
                so.ApplyModifiedProperties();
                so.Dispose();
            });
        }
        menu.ShowAsContext();
    }

    /// <summary>효과 한 칸의 필드들. 이름은 한글로, 수치는 % 단위로 보여준다.</summary>
    private static void DrawEffectFields(SerializedProperty e, Type type, bool compact)
    {
        SerializedProperty displayText = null;
        var it = e.Copy();
        var end = e.GetEndProperty();
        bool enter = true;
        while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
        {
            enter = false;
            var p = it.Copy();
            switch (p.name)
            {
                case "displayText": displayText = p; continue;
                case "label":
                    if (compact) continue;
                    EditorGUILayout.PropertyField(p, new GUIContent("메모 (게임에 안 보임)", "기획 메모. 자동 문구가 없는 효과 종류에서만 표시 문구 대신 쓰인다."));
                    continue;
                case "value":
                    DrawValue(e, p, type);
                    continue;
                case "statType":
                    EditorGUILayout.PropertyField(p, new GUIContent("능력치", "올리거나 내릴 플레이어 능력치."));
                    continue;
                case "isPercent":
                    EditorGUILayout.PropertyField(p, type == typeof(ItemRoomClearHealEffect)
                        ? new GUIContent("최대 체력 비율로", "켜면 회복량이 최대 체력의 % 가 된다. 끄면 고정 회복량.")
                        : new GUIContent("비율(%) 보정", "켜면 능력치를 % 로 올린다(합연산). 끄면 고정치를 더한다.\n켜고 끌 때 수치 의미가 바뀌니 아래 미리보기를 확인하세요."));
                    continue;
                case "amount":
                    EditorGUILayout.PropertyField(p, new GUIContent("보호막 (1개당)", "아이템 1개당 방 입장 시 얻는 보호막 양."));
                    continue;
                case "copiesPerCharge":
                    EditorGUILayout.PropertyField(p, new GUIContent("몇 개마다 +1", "이 개수만큼 모일 때마다 최대 대시 횟수 +1."));
                    continue;
                default:
                    EditorGUILayout.PropertyField(p, new GUIContent(DesignerInspectorLabels.Translate(p.name), p.tooltip), true);
                    continue;
            }
        }

        if (displayText != null)
        {
            EditorGUILayout.LabelField(new GUIContent("표시 문구 (비우면 자동)", displayText.tooltip), compact ? EditorStyles.miniLabel : EditorStyles.label);
            EditorGUI.BeginChangeCheck();
            string text = EditorGUILayout.TextArea(displayText.stringValue ?? "", WrapArea, GUILayout.MinHeight(compact ? 18 : 34));
            if (EditorGUI.EndChangeCheck()) displayText.stringValue = text;
        }
    }

    /// <summary>
    /// 'value' 필드. 저장값이 0~1 비율인 경우(비율 보정, 회피율, 최대 체력 비율 회복)는 % 로 바꿔 입력받는다.
    /// 예: 저장 0.04 ↔ 표시 4 (%)
    /// </summary>
    private static void DrawValue(SerializedProperty effect, SerializedProperty value, Type type)
    {
        bool percentScale = false;
        string label = "수치", tip = value.tooltip;
        var pct = effect.FindPropertyRelative("isPercent");

        if (type == typeof(ItemStatEffect))
        {
            var stat = (StatType)effect.FindPropertyRelative("statType").intValue;
            percentScale = (pct != null && pct.boolValue) || stat == StatType.Evasion;
            if (percentScale) { label = "수치 (%)"; tip = "1개당 % 수치. 5 를 넣으면 +5%. (내부 저장값은 0.05)"; }
            else if (stat == StatType.CritChance || stat == StatType.CritDamage) { label = "수치 (%p)"; tip = "1개당 수치. 치명타 확률/피해는 7 = +7%p."; }
            else { label = "수치 (고정)"; tip = "1개당 더하는 고정 수치."; }
        }
        else if (type == typeof(ItemRoomClearHealEffect))
        {
            percentScale = pct != null && pct.boolValue;
            label = percentScale ? "회복량 (최대 체력 %)" : "회복량";
            tip = percentScale ? "1개당 최대 체력의 몇 % 를 회복하는지. (내부 저장값 0.1 = 10%)" : "1개당 회복하는 고정 체력.";
        }

        if (!percentScale) { EditorGUILayout.PropertyField(value, new GUIContent(label, tip)); return; }

        EditorGUI.BeginChangeCheck();
        float shown = (float)Math.Round(value.floatValue * 100.0, 4);
        float input = EditorGUILayout.FloatField(new GUIContent(label, tip), shown);
        if (EditorGUI.EndChangeCheck()) value.floatValue = input / 100f;
    }

    // ── 미리보기 ──────────────────────────────────────────────────────
    public static void DrawPreview(ItemSO item)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            Row("툴팁 · 보상 카드", item.TooltipBody);
            Row("상점 카드", item.ShopBody);
            Row("주머니 · 개당 효과", item.EffectSummary(1));
            if (item.MaxStack > 1) Row($"주머니 · {item.MaxStack}개(최대)일 때", item.EffectSummary(item.MaxStack));
        }
    }

    private static void Row(string title, string body)
    {
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField(string.IsNullOrEmpty(body) ? "(없음)" : body, WrapLabel);
        EditorGUILayout.Space(2);
    }

    // ── 검사 ──────────────────────────────────────────────────────────
    private static readonly Regex Numbers = new Regex(@"\d+(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly Regex Token = new Regex(@"\{[^{}\s]+\}", RegexOptions.Compiled);

    private static string Norm(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>설명 문구와 실제 효과 수치 비교에 쓰는 '1개당 대표 수치'.</summary>
    private static IEnumerable<double> KeyNumbers(ItemEffect e)
    {
        switch (e)
        {
            case ItemStatEffect s: yield return Math.Abs(s.DisplayAmount(1)); break;
            case ItemRoomEntryShieldEffect sh: yield return sh.amount; break;
            case ItemDashChargeEffect d: yield return d.copiesPerCharge; break;
            case ItemRoomClearHealEffect h: yield return h.value * (h.isPercent ? 100.0 : 1.0); break;
        }
    }

    /// <summary>기획자가 확인해야 할 문제 목록. 비어 있으면 문제 없음.</summary>
    public static List<string> Warnings(ItemSO item)
    {
        var list = new List<string>();
        if (item == null) return list;
        var growth = Growth;
        if (growth != null && growth.items != null && !growth.items.Contains(item))
            list.Add($"아이템 목록({growth.name})에 등록되지 않아 게임 보상으로 나오지 않고 세이브에서도 불러오지 못합니다. 아래 '목록 등록'에서 켜세요.");
        if (item.effects == null || item.effects.Count == 0)
        {
            list.Add("효과가 없습니다. 주머니에 넣어도 아무 일도 일어나지 않습니다.");
            return list;
        }
        if (item.effects.Any(e => e == null)) list.Add("종류가 정해지지 않은 빈 효과 칸이 있습니다.");

        var descNums = new HashSet<string>(Numbers.Matches(item.description ?? "").Cast<Match>()
            .Select(m => Norm(double.Parse(m.Value, CultureInfo.InvariantCulture))));
        foreach (var e in item.effects.Where(e => e != null))
        {
            foreach (var n in KeyNumbers(e))
                if (!descNums.Contains(Norm(n)))
                    list.Add($"설명에 효과 수치 '{Norm(n)}' 이(가) 없습니다. 설명이 실제 효과와 맞는지 확인하세요.\n실제 효과: {e.AutoDescribe(1)}");

            if (!string.IsNullOrWhiteSpace(e.displayText))
                foreach (Match m in Token.Matches(e.Describe(1)))
                    list.Add($"표시 문구에 알 수 없는 치환어 {m.Value} 가 있습니다. 쓸 수 있는 치환어: {e.TokenHelp}");
        }
        return list;
    }

    // ── 목록 등록 상태 ────────────────────────────────────────────────
    private static GrowthRegistrySO growthCache;
    private static ShopRegistrySO shopCache;
    private static bool registryLoaded;

    [InitializeOnLoadMethod]
    private static void HookProjectChanged() => EditorApplication.projectChanged += () => registryLoaded = false;

    private static T FirstAsset<T>() where T : UnityEngine.Object
    {
        var guid = AssetDatabase.FindAssets("t:" + typeof(T).Name).FirstOrDefault();
        return guid == null ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
    }

    public static GrowthRegistrySO Growth { get { LoadRegistries(); return growthCache; } }
    public static ShopRegistrySO Shop { get { LoadRegistries(); return shopCache; } }

    private static void LoadRegistries()
    {
        if (registryLoaded && growthCache != null && shopCache != null) return;
        growthCache = FirstAsset<GrowthRegistrySO>();
        shopCache = FirstAsset<ShopRegistrySO>();
        registryLoaded = true;
    }

    public static void DrawRegistry(ItemSO item)
    {
        Header("목록 등록");
        var growth = Growth;
        var shop = Shop;

        if (growth != null)
        {
            bool inGrowth = growth.items != null && growth.items.Contains(item);
            bool set = EditorGUILayout.ToggleLeft(new GUIContent($"아이템 목록에 포함 ({growth.name})",
                "보상 추첨과 세이브 불러오기에 쓰는 전체 아이템 목록. 빠져 있으면 보상으로 안 나오고 불러오기에서 사라진다."), inGrowth);
            if (set != inGrowth) Toggle(growth, growth.items, item, set);
            if (growth.startingItem == item) EditorGUILayout.LabelField("  · 새 게임 시작 시 기본 지급되는 아이템입니다.", WrapMini);
        }
        else EditorGUILayout.HelpBox("GrowthRegistrySO 에셋을 찾지 못했습니다.", MessageType.None);

        if (shop != null)
        {
            bool inShop = shop.itemPool != null && shop.itemPool.Contains(item);
            bool set = EditorGUILayout.ToggleLeft(new GUIContent($"상점 진열 후보에 포함 ({shop.name})",
                "던전 상점에 진열될 수 있는 아이템 목록."), inShop);
            if (set != inShop) Toggle(shop, shop.itemPool, item, set);
            if (inShop && !item.availableAsReward)
                EditorGUILayout.HelpBox("'상점 · 방 보상에 등장'이 꺼져 있어 상점 후보에 있어도 진열되지 않습니다(상점 목록 갱신 시 빠집니다).", MessageType.Info);
        }
    }

    /// <summary>목록 데이터(registry)의 아이템 리스트에 넣거나 뺀다. Undo 가능.</summary>
    public static void Toggle(UnityEngine.Object registry, List<ItemSO> list, ItemSO item, bool include)
    {
        if (list == null) return;
        Undo.RecordObject(registry, include ? "아이템 목록에 추가" : "아이템 목록에서 제외");
        if (include) { if (!list.Contains(item)) list.Add(item); }
        else list.RemoveAll(x => x == item);
        EditorUtility.SetDirty(registry);
    }
}

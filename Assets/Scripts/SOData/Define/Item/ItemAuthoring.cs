using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

// 아이템 편집 규칙: 확인할 것(경고), 능력치·보정 방식 조합, 수치 표시 배율, 효과 종류 목록.
// 제작실 아이템 화면이 쓴다. 런타임이라 플레이어 빌드의 제작판에서도 돈다.
public static class ItemAuthoring
{
    // field: 화면이 그 줄로 가는 DATA 경로(효과는 "effects/@<id>/…", 상점 진열은 "상점 진열", 빈 문자열은 줄 없음).
    // title: 미리보기 열 한 줄(Micro 8, 150px · 한글 18자 이내). text: 설명 칸 전문.
    public readonly struct Warning
    {
        public readonly string field, title, text;
        public Warning(string field, string title, string text) { this.field = field; this.title = title; this.text = text; }
    }

    private static readonly Regex Numbers = new Regex(@"\d+(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly Regex Token = new Regex(@"\{[^{}\s]+\}", RegexOptions.Compiled);
    private static string Norm(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    public static string EffectPath(ItemEffect e) => "effects/@" + Uri.EscapeDataString(e.id ?? "");

    // 설명 문구와 실제 효과 수치 비교에 쓰는 '1개당 대표 수치'.
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

    // ItemStatEffect 의 보정 방식(비율/고정) 중 게임이 실제로 읽는 쪽. CharacterStat 게터를 손으로 옮긴 표라
    // 게터가 바뀌면 같이 고칠 것 (Assets/Scripts/Entities/CharacterStat.cs, 줄 번호는 작성 시점 기준).
    //  · 고정치만 읽음: PHYS_AMP :97 · MAGIC_AMP :100 · CRIT_CHANCE :154 · CRIT_OVERFLOW_DAMAGE :158 · CRIT_DAMAGE :160 ·
    //    EVASION :163 · ACCURACY :166 · SKILL_CDR :195 · DASH_CDR :198 · BASIC_ATK_MULT :201 · DashCharges(MeleeDodgeController.cs:389)
    //  · 비율만 읽음: DASH_DISTANCE_MULTIPLIER :207 · ApplyDashCooldown :208-209(DashRechargeSpeed)
    //  · 둘 다 읽음(표에 없음): ATK :82-90 · MAGIC :93 · MAXHP :102-116 · ATKSPD :125-134 · DEF :144-151 · LUCK :155 · MOVESPEED :168-178
    private static readonly HashSet<StatType> FlatOnlyStats = new HashSet<StatType>
    {
        StatType.PhysDamageAmp, StatType.MagicDamageAmp, StatType.CritChance, StatType.CritOverflowConversion,
        StatType.CritDamage, StatType.Evasion, StatType.Accuracy, StatType.SkillCooldownReduction,
        StatType.DashCooldownReduction, StatType.BasicAttackMultiplier, StatType.DashCharges,
    };
    private static readonly HashSet<StatType> PercentOnlyStats = new HashSet<StatType> { StatType.DashDistance, StatType.DashRechargeSpeed };

    // 게임이 읽지 않는 능력치·보정 방식 조합의 이유. 읽으면 null.
    public static string StatReason(StatType stat, bool percent)
    {
        string title = new ItemStatEffect { statType = stat }.StatTitle;
        if (percent && FlatOnlyStats.Contains(stat)) return $"'{title}'은(는) 게임이 고정치만 읽습니다. 비율 보정을 끄세요.";
        if (!percent && PercentOnlyStats.Contains(stat)) return $"'{title}'은(는) 게임이 비율만 읽습니다. 비율 보정을 켜세요.";
        return null;
    }

    // 수치 칸의 표시 배율과 단위: 저장 0.04 ↔ 표시 4. 게임 문구(ItemStatEffect.AutoText)와 같은 판정이다.
    public static (float scale, string unit) StatUnit(ItemStatEffect e) =>
        (e.ScalesToPercent ? 100f : 1f, e.ShowsPercentSign ? (e.isPercent ? "%" : "%p") : null);

    private static IReadOnlyList<(Type type, string name, string help)> effectTypes;
    // [ItemEffectInfo]가 있는 비추상 파생 타입, 이름순. 새 효과 클래스는 속성만 달면 '효과 추가' 목록에 나온다.
    public static IReadOnlyList<(Type type, string name, string help)> EffectTypes => effectTypes ??= typeof(ItemEffect).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(ItemEffect).IsAssignableFrom(t))
        .Select(t => (type: t, info: t.GetCustomAttribute<ItemEffectInfoAttribute>()))
        .Where(x => x.info != null)
        .Select(x => (x.type, x.info.Name, x.info.Help))
        .OrderBy(x => x.Name, StringComparer.Ordinal).ToList();

    // growth·shop은 호출자가 넘긴다(편집 모드는 에셋, 실행 중은 DATA가 적용된 살아 있는 레지스트리).
    public static List<Warning> Warnings(ItemSO item, GrowthRegistrySO growth, ShopRegistrySO shop)
    {
        var list = new List<Warning>();
        if (item == null) return list;
        bool retired = growth != null && growth.retired.Contains(item);   // 삭제 표시: Rebuild가 목록에서 뺐다
        if (growth != null && growth.items != null && !growth.items.Contains(item) && !retired)
            list.Add(new Warning("", "아이템 목록에 없음",
                $"아이템 목록({growth.name})에 없어 보상으로 나오지 않고 세이브에서도 불러오지 못합니다. Unity의 Tools > 기획 > 레지스트리 새로고침으로 붙이세요."));
        bool inShop = shop != null && shop.itemPool != null && shop.itemPool.Contains(item);
        if (inShop && !item.availableAsReward)
            list.Add(new Warning("availableAsReward", "상점 진열인데 등장 꺼짐", "보상·상점 등장이 꺼져 있어 상점 진열 후보여도 진열되지 않습니다."));
        if (growth != null && growth.startingItem == item)
        {
            if (item.availableAsReward) list.Add(new Warning("availableAsReward", "시작 아이템인데 등장 켜짐", "시작 아이템은 보상·상점에 나오지 않아야 합니다. 등장을 끄세요."));
            if (inShop) list.Add(new Warning("상점 진열", "시작 아이템인데 상점 진열", "시작 아이템은 상점에 진열하지 않습니다. 상점 진열을 끄세요."));
            if (item.MaxStack > 1) list.Add(new Warning("maxStack", "시작 아이템 최대 보유 > 1", "시작 아이템은 최대 보유 1개입니다."));
        }
        if (item.effects == null || item.effects.Count == 0)
        {
            list.Add(new Warning("effects", "효과 없음", "효과가 없습니다. 주머니에 넣어도 아무 일도 일어나지 않습니다."));
            if (item.availableAsReward)
                list.Add(new Warning("availableAsReward", "효과 없이 등장 켜짐", "효과가 없는데 보상·상점 등장이 켜져 있습니다. 빈 아이템이 보상·상점에 나옵니다. 효과를 넣기 전까지는 끄세요."));
            return list;
        }
        if (item.effects.Any(e => e == null)) list.Add(new Warning("effects", "빈 효과 칸", "종류가 정해지지 않은 빈 효과 칸이 있습니다. 게임에서는 무시됩니다(Unity에서 정리)."));

        HashSet<string> NumbersIn(string text) => new HashSet<string>(Numbers.Matches(text ?? "").Cast<Match>()
            .Select(m => Norm(double.Parse(m.Value, CultureInfo.InvariantCulture))));
        var descNums = NumbersIn(item.description);
        // 상점 카드는 shopDescription 이 있으면 그것을 쓴다(ItemSO.ShopBody) — 따로 검사한다.
        var shopNums = string.IsNullOrEmpty(item.shopDescription) ? null : NumbersIn(item.shopDescription);
        foreach (var e in item.effects.Where(e => e != null))
        {
            foreach (var n in KeyNumbers(e))
            {
                if (!descNums.Contains(Norm(n)))
                    list.Add(new Warning("description", "설명에 수치 " + Norm(n) + " 없음", $"설명에 효과 수치 '{Norm(n)}'이(가) 없습니다. 설명이 실제 효과와 맞는지 확인하세요.\n실제 효과: {e.AutoDescribe(1)}"));
                if (shopNums != null && !shopNums.Contains(Norm(n)))
                    list.Add(new Warning("shopDescription", "상점 설명에 수치 " + Norm(n) + " 없음", $"상점 설명에 효과 수치 '{Norm(n)}'이(가) 없습니다.\n실제 효과: {e.AutoDescribe(1)}"));
            }
            if (e is ItemStatEffect s && StatReason(s.statType, s.isPercent) is string reason)
                list.Add(new Warning(EffectPath(e) + "/isPercent", "보정 방식이 무시됨", reason));
            if (!string.IsNullOrWhiteSpace(e.displayText))
                foreach (Match m in Token.Matches(e.Describe(1)))
                    list.Add(new Warning(EffectPath(e) + "/displayText", "모르는 치환어 " + m.Value, $"표시 문구에 알 수 없는 치환어 {m.Value}가 있습니다. 쓸 수 있는 치환어: {e.TokenHelp}"));
        }
        return list;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 에디터(아이템 인스펙터 / 아이템 편집기)에서 효과 종류를 고를 때 보여줄 한글 이름과 설명.
/// 새 효과 클래스를 만들면 이 속성만 달아 주면 "+ 효과 추가" 메뉴에 자동으로 나온다.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
public sealed class ItemEffectInfoAttribute : System.Attribute
{
    public readonly string Name;
    public readonly string Help;
    public ItemEffectInfoAttribute(string name, string help = "") { Name = name; Help = help; }
}

/// <summary>
/// 아이템 효과 한 조각. ItemSO.effects 에 [SerializeReference] 로 담기므로 아이템 하나가
/// 여러 개를 가질 수 있고, 새 종류를 추가할 때 ItemSO 의 필드를 늘리지 않아도 된다.
///
/// 지금 있는 종류:
///   · ItemStatEffect            — 주머니에 있는 동안 상시 플레이어 스탯 보정 (최대 체력 +10 등)
///   · ItemRoomClearHealEffect   — 방 클리어 시 회복 (고정치 또는 최대 체력 비율)
///   · ItemRoomEntryShieldEffect — 방 입장 시 보호막
///   · ItemDashChargeEffect      — N개마다 최대 대시 횟수 +1
///   · ItemCritOverflowEffect    — 치명타 확률 초과분을 치명타 피해로 변환
///
/// [표시 문구] 주머니 UI 의 '개당 효과 / 현재 총 효과 / 1개 더 획득 시' 문구는 Describe() 가 만든다.
/// displayText 가 비어 있으면 종류별 자동 문구(AutoText)를, 채워져 있으면 그 문구에 {값} 같은
/// 치환어를 끼워 넣어 쓴다 — 기획자가 코드 수정 없이 효과 문구를 고칠 수 있게 하기 위함.
///
/// 강화/성장 개념은 없다 — 아이템 효과는 고정이다(장비의 enhanceLevel 과 다른 점).
/// </summary>
[System.Serializable]
public abstract class ItemEffect
{
    [Tooltip("기획 메모(게임에 표시되지 않음). 자동 문구가 없는 효과 종류에서만 표시 문구 대신 쓰인다.")]
    public string label;

    [Tooltip("주머니 UI(개당 효과 / 현재 총 효과 / 1개 더 획득 시)에 나올 문구. 비우면 자동 문구를 쓴다.\n" +
             "치환어: {값} = 보유 개수가 반영된 수치, {개수} = 보유 개수, {자동} = 자동 문구.\n" +
             "효과 종류별 추가 치환어는 아이템 인스펙터의 효과 칸 아래에 표시된다.")]
    [TextArea(1, 3)] public string displayText;

    /// <summary>보유 개수(count)를 반영한 표시 문구. 표시 문구가 비어 있으면 자동 문구.</summary>
    public string Describe(int count)
    {
        string auto = AutoText(count);
        if (string.IsNullOrWhiteSpace(displayText)) return auto;

        var tokens = new Dictionary<string, string>
        {
            ["개수"] = count.ToString(),
            ["자동"] = auto ?? "",
        };
        FillTokens(count, tokens);
        string text = displayText;
        foreach (var t in tokens) text = text.Replace("{" + t.Key + "}", t.Value);
        return text;
    }

    /// <summary>표시 문구를 무시한 자동 문구(에디터 미리보기용).</summary>
    public string AutoDescribe(int count) => AutoText(count);

    /// <summary>종류별 자동 문구. 기본은 메모(label) — 비어 있으면 ItemSO 가 아이템 설명으로 대신한다.</summary>
    protected virtual string AutoText(int count) => label;

    /// <summary>표시 문구에서 쓸 수 있는 종류별 치환어를 채운다. {개수}/{자동} 은 공통으로 들어간다.</summary>
    protected virtual void FillTokens(int count, Dictionary<string, string> tokens) { }

    /// <summary>에디터에 보여줄 치환어 안내.</summary>
    public virtual string TokenHelp => "{개수} 보유 개수 · {자동} 자동 문구";

    protected static string Num(float v) => v.ToString("0.##");
}

[ItemEffectInfo("방 입장 시 보호막", "새 방에 들어갈 때마다 보호막을 준다. 개수만큼 합산되며 보호막은 누적되지 않는다(매 방마다 다시 설정).")]
[System.Serializable]
public class ItemRoomEntryShieldEffect : ItemEffect
{
    [Tooltip("1개당 보호막 양.")]
    [Min(0f)] public float amount = 6f;

    protected override string AutoText(int count) => $"방 진입 보호막 +{amount * count:0.##}";

    protected override void FillTokens(int count, Dictionary<string, string> tokens)
    {
        tokens["값"] = Num(amount * count);
        tokens["1개값"] = Num(amount);
    }

    public override string TokenHelp => "{값} 개수 반영 보호막 · {1개값} 1개당 보호막 · " + base.TokenHelp;
}

[ItemEffectInfo("최대 대시 횟수 증가", "같은 아이템을 N개 모을 때마다 최대 대시 횟수 +1. 아이템에 1번만 넣는다(여러 번 넣어도 중복 적용 안 됨).")]
[System.Serializable]
public class ItemDashChargeEffect : ItemEffect
{
    [Tooltip("몇 개 모일 때마다 최대 대시 횟수 +1 인지.")]
    [Min(1)] public int copiesPerCharge = 3;

    protected override string AutoText(int count)
        => $"최대 대시 횟수 +{count / Mathf.Max(1, copiesPerCharge)} ({copiesPerCharge}개마다 +1)";

    protected override void FillTokens(int count, Dictionary<string, string> tokens)
    {
        tokens["값"] = (count / Mathf.Max(1, copiesPerCharge)).ToString();
        tokens["기준"] = copiesPerCharge.ToString();
    }

    public override string TokenHelp => "{값} 늘어난 대시 횟수 · {기준} 몇 개마다 · " + base.TokenHelp;
}

/// <summary>소지 여부만 본다. 여러 개 소지해도 초과 확률을 중복 변환하지 않는다.</summary>
[ItemEffectInfo("치명타 초과분 변환", "치명타 확률 100% 초과분 10%마다 치명타 피해 +10%. 여러 개 가져도 1번만 적용된다.")]
[System.Serializable]
public class ItemCritOverflowEffect : ItemEffect
{
    protected override string AutoText(int count) => "초과 치명타 확률 10%마다 치명타 피해 +10%";
}

/// <summary>
/// 주머니에 들어 있는 동안 플레이어 스탯을 보정한다.
/// 실제 적용/회수는 ItemPouch 가 자기 자신을 source 키로 써서 통째로 다시 계산한다.
/// </summary>
[ItemEffectInfo("능력치 보정", "주머니에 있는 동안 플레이어 능력치를 올리거나 내린다. 보유 개수만큼 합산된다.")]
[System.Serializable]
public class ItemStatEffect : ItemEffect
{
    public StatType statType = StatType.Health;

    [Tooltip("보정치. isPercent 면 0.1 = +10%, 아니면 고정치.")]
    public float value = 10f;

    [Tooltip("true=비율(합연산), false=고정치.")]
    public bool isPercent = false;

    public void Apply(CharacterStat stat, object source)
    {
        if (stat == null) return;
        if (isPercent) stat.Mods.AddPercent(source, statType, value);
        else           stat.Mods.AddFlat(source, statType, value);
    }

    /// <summary>표시용 능력치 이름.</summary>
    public string StatTitle => statType switch
    {
        StatType.CritChance => "치명타 확률", StatType.CritDamage => "치명타 피해",
        StatType.Defense => "방어력", StatType.MoveSpeed => "이동 속도",
        StatType.Evasion => "회피율", StatType.DashRechargeSpeed => "대시 충전 속도",
        StatType.DashDistance => "대시 거리", StatType.Health => "최대 체력",
        _ => string.IsNullOrEmpty(label) ? statType.ToString() : label
    };

    /// <summary>저장값(0.04)을 표시값(4)으로 바꿀 때 100 을 곱하는지. 회피율은 고정치여도 0~1 이다.</summary>
    public bool ScalesToPercent => isPercent || statType == StatType.Evasion;

    /// <summary>표시값 뒤에 % 를 붙이는지. 치명타 확률/피해는 고정치 자체가 %p 단위다.</summary>
    public bool ShowsPercentSign => ScalesToPercent || statType == StatType.CritChance || statType == StatType.CritDamage;

    /// <summary>개수가 반영된 표시값.</summary>
    public float DisplayAmount(int count) => value * count * (ScalesToPercent ? 100f : 1f);

    protected override string AutoText(int count)
    {
        float amount = DisplayAmount(count);
        return $"{StatTitle} {amount:+0.##;-0.##;0}{(ShowsPercentSign ? "%" : "")}";
    }

    protected override void FillTokens(int count, Dictionary<string, string> tokens)
    {
        tokens["값"] = Num(DisplayAmount(count));
        tokens["1개값"] = Num(DisplayAmount(1));
        tokens["능력치"] = StatTitle;
    }

    public override string TokenHelp => "{값} 개수 반영 수치 · {1개값} 1개당 수치 · {능력치} 능력치 이름 · " + base.TokenHelp;
}

/// <summary>
/// 방을 클리어할 때 플레이어를 회복시킨다.
///
/// [26/09/09] 예전엔 RoomInstance.MarkCleared 가 아이템과 무관하게 무조건 10 회복시켰다.
/// 그 로직을 이 효과로 옮겼다 — 이 효과를 가진 아이템이 주머니에 있어야만 회복한다.
/// 실제 발동은 ItemPouch 가 RoomInstance.OnAnyRoomCleared 를 구독해서 처리한다.
///
/// 같은 효과를 가진 아이템을 여러 개 들고 있으면 회복량은 그대로 합산된다(칸마다 1회).
/// </summary>
[ItemEffectInfo("방 클리어 시 회복", "방을 클리어할 때 체력을 회복한다. 고정치 또는 최대 체력 비율. 개수만큼 합산된다.")]
[System.Serializable]
public class ItemRoomClearHealEffect : ItemEffect
{
    [Tooltip("회복량. isPercent 면 0.1 = 최대 체력의 10%, 아니면 고정치.")]
    public float value = 10f;

    [Tooltip("true=최대 체력 비율, false=고정치.")]
    public bool isPercent = false;

    /// <summary>이번 방 클리어로 회복시킬 양. 음수는 0 으로 막는다(회복 효과가 딜을 넣으면 안 된다).</summary>
    public float ResolveAmount(CharacterHealth health)
    {
        if (health == null) return 0f;
        float amount = isPercent ? health.MaxHP * value : value;
        return Mathf.Max(0f, amount);
    }

    private float DisplayAmount(int count) => value * count * (isPercent ? 100f : 1f);

    protected override string AutoText(int count)
        => $"방 클리어 회복 +{DisplayAmount(count):0.##}{(isPercent ? "%" : "")}";

    protected override void FillTokens(int count, Dictionary<string, string> tokens)
    {
        tokens["값"] = Num(DisplayAmount(count));
        tokens["1개값"] = Num(DisplayAmount(1));
    }

    public override string TokenHelp => "{값} 개수 반영 회복량 · {1개값} 1개당 회복량 · " + base.TokenHelp;
}

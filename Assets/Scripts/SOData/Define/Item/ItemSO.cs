using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 아이템 등급. 4티어가 제일 흔하고 1티어가 제일 좋다(숫자가 작을수록 강함).
/// 값을 명시적으로 박아둔다 — 에셋이 이 숫자를 직렬화하므로 순서를 바꾸면 조용히 등급이 밀린다.
///
/// [주의] 기존 ItemRarity(Common~Legendary)와 섞지 말 것. 그쪽은 Common 이 0 이라 여기와 순서가
/// 정반대다. 아이템 시스템은 장비(EquipmentSO)와 완전히 별개라 등급 체계도 따로 둔다.
/// </summary>
public enum ItemTier
{
    [UnityEngine.InspectorName("등급 1")] Tier1 = 1,
    [UnityEngine.InspectorName("등급 2")] Tier2 = 2,
    [UnityEngine.InspectorName("등급 3")] Tier3 = 3,
    [UnityEngine.InspectorName("등급 4")] Tier4 = 4,
}

/// <summary>
/// 티어에서 자동으로 유도되는 값들(가격/색). 아이템 에셋마다 가격을 적지 않는 이유는
/// 아이템이 늘어나면 개별 값이 전부 어긋나기 때문이다 — 밸런싱은 아래 표 한 줄만 고친다.
/// </summary>
public static class ItemTierRules
{
    // 인덱스 = 티어 번호. [0] 은 안 쓰는 자리.
    private static readonly int[] PRICE  = { 0, 600, 400, 200, 100 };

    private static readonly Color[] COLOR =
    {
        Color.white,
        new Color(1.00f, 0.62f, 0.20f), // 1티어 — 주황
        new Color(0.72f, 0.45f, 0.95f), // 2티어 — 보라
        new Color(0.35f, 0.66f, 1.00f), // 3티어 — 파랑
        new Color(0.75f, 0.75f, 0.75f), // 4티어 — 회색
    };

    private static int Index(ItemTier t) => Mathf.Clamp((int)t, 1, 4);

    /// <summary>상점 구매가(골드).</summary>
    public static int Price(ItemTier t) => PRICE[Index(t)];

    /// <summary>UI 테두리/텍스트 색. (메서드 이름을 Color 로 두면 UnityEngine.Color 타입을 가려서 컴파일이 깨진다.)</summary>
    public static Color TierColor(ItemTier t) => COLOR[Index(t)];

    /// <summary>"3티어" 같은 표기.</summary>
    public static string Label(ItemTier t) => $"{Index(t)}티어";
}

/// <summary>주머니에 소지하는 아이템 한 종류. 장비와 별개이며 효과/최대 중첩은 에셋에서 조절한다.</summary>
[CreateAssetMenu(fileName = "Item", menuName = "Necromancer/Item/Item")]
public class ItemSO : ScriptableObject
{
    [Header("표시")]
    public string itemName;
    [TextArea] public string description;
    [Tooltip("비어 있으면 바닥 픽업 프리팹의 기존 기본 아이콘을 사용한다.")]
    public Sprite icon;

    [Header("등급")]
    [Tooltip("가격과 등급 색이 여기서 자동으로 나온다(ItemTierRules).")]
    public ItemTier tier = ItemTier.Tier4;

    [Header("중첩")]
    [Min(1), Tooltip("같은 아이템의 최대 보유 수. 주머니 종류/칸 수에는 제한이 없다.")]
    public int maxStack = 1;
    public int MaxStack => Mathf.Max(1, maxStack);

    [Tooltip("상점·방 보상에서 무작위로 등장할 수 있는 아이템. 기본 지급 전용 아이템은 끈다.")]
    public bool availableAsReward = true;

    [Header("효과")]
    [Tooltip("주머니에 들어 있는 동안 적용되는 효과들. 인스펙터에서 + 로 종류를 골라 추가한다.")]
    [SerializeReference] public List<ItemEffect> effects = new List<ItemEffect>();

    /// <summary>표기용 이름. itemName 이 비면 에셋 이름으로 폴백.</summary>
    public string DisplayName => string.IsNullOrEmpty(itemName) ? name : itemName;

    public int Price => ItemTierRules.Price(tier);
    public Color TierColor => ItemTierRules.TierColor(tier);

    /// <summary>툴팁 본문. 등급 표기를 설명 앞에 붙인다.</summary>
    public string TooltipBody => $"[{ItemTierRules.Label(tier)}]\n{description}";

    public string EffectSummary(int count)
    {
        var lines = new List<string>();
        if (effects != null) foreach (var effect in effects)
        {
            if (effect is ItemStatEffect stat)
            {
                string title = stat.statType switch
                {
                    StatType.CritChance => "치명타 확률", StatType.CritDamage => "치명타 피해",
                    StatType.Defense => "방어력", StatType.MoveSpeed => "이동 속도",
                    StatType.Evasion => "회피율", StatType.DashRechargeSpeed => "대시 충전 속도",
                    StatType.DashDistance => "대시 거리", StatType.Health => "최대 체력",
                    _ => string.IsNullOrEmpty(stat.label) ? stat.statType.ToString() : stat.label
                };
                bool percent = stat.isPercent || stat.statType == StatType.Evasion;
                bool points = stat.statType == StatType.CritChance || stat.statType == StatType.CritDamage;
                float amount = stat.value * count * (percent ? 100f : 1f);
                lines.Add($"{title} {amount:+0.##;-0.##;0}{(percent || points ? "%" : "")}");
            }
            else if (effect is ItemRoomEntryShieldEffect shield) lines.Add($"방 진입 보호막 +{shield.amount * count:0.##}");
            else if (effect is ItemDashChargeEffect charge) lines.Add($"최대 대시 횟수 +{count / Mathf.Max(1, charge.copiesPerCharge)} ({charge.copiesPerCharge}개마다 +1)");
            else if (effect is ItemCritOverflowEffect) lines.Add("초과 치명타 확률 10%마다 치명타 피해 +10%");
            else if (effect is ItemRoomClearHealEffect heal) lines.Add($"방 클리어 회복 +{heal.value * count * (heal.isPercent ? 100f : 1f):0.##}{(heal.isPercent ? "%" : "")}");
            else if (effect != null) lines.Add(!string.IsNullOrEmpty(effect.label) ? effect.label : description);
        }
        return string.Join("\n", lines);
    }
}

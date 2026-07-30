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
    Tier1 = 1,
    Tier2 = 2,
    Tier3 = 3,
    Tier4 = 4,
}

/// <summary>
/// 티어에서 자동으로 유도되는 값들(가격/환급/색). 아이템 에셋마다 가격을 적지 않는 이유는
/// 아이템이 늘어나면 개별 값이 전부 어긋나기 때문이다 — 밸런싱은 아래 표 한 줄만 고친다.
/// </summary>
public static class ItemTierRules
{
    // 인덱스 = 티어 번호. [0] 은 안 쓰는 자리.
    private static readonly int[] PRICE  = { 0, 600, 400, 200, 100 };
    private static readonly int[] REFUND = { 0, 270, 180,  60,  30 };

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

    /// <summary>분해 시 돌려받는 골드.</summary>
    public static int Refund(ItemTier t) => REFUND[Index(t)];

    /// <summary>UI 테두리/텍스트 색. (메서드 이름을 Color 로 두면 UnityEngine.Color 타입을 가려서 컴파일이 깨진다.)</summary>
    public static Color TierColor(ItemTier t) => COLOR[Index(t)];

    /// <summary>"3티어" 같은 표기.</summary>
    public static string Label(ItemTier t) => $"{Index(t)}티어";
}

/// <summary>
/// 아이템 정의(템플릿). 하나의 에셋 = 하나의 아이템 종류.
///
/// 장비(EquipmentSO)와는 완전히 별개의 시스템이다:
///   · 장비 = 무기 한 자루. Q/E 플레이어 스킬을 부여하고 강화 레벨이 있다.
///   · 아이템 = 주머니에 넣는 것. 효과가 고정이고 강화가 없다. 여러 개 동시 소지(기본 4칸).
/// 서로 참조하지 않으므로 한쪽을 고칠 때 다른 쪽을 볼 필요가 없다.
///
/// 획득 경로는 전부 '바닥에 떨어짐' 이다(상점 구매/엘리트 드랍 모두). 바닥에서 F 를 짧게 누르면
/// 주머니에 습득, 3초 길게 누르면 분해되어 골드로 환급된다 — GroundItem 참조.
/// </summary>
[CreateAssetMenu(fileName = "Item", menuName = "Necromancer/Item/Item")]
public class ItemSO : ScriptableObject
{
    [Header("표시")]
    public string itemName;
    [TextArea] public string description;
    [Tooltip("비어 있으면 바닥/주머니에서 흰 사각으로 대체된다.")]
    public Sprite icon;

    [Header("등급")]
    [Tooltip("가격과 분해 환급액이 여기서 자동으로 나온다(ItemTierRules).")]
    public ItemTier tier = ItemTier.Tier4;

    [Header("효과")]
    [Tooltip("주머니에 들어 있는 동안 적용되는 효과들. 인스펙터에서 + 로 종류를 골라 추가한다.")]
    [SerializeReference] public List<ItemEffect> effects = new List<ItemEffect>();

    /// <summary>표기용 이름. itemName 이 비면 에셋 이름으로 폴백.</summary>
    public string DisplayName => string.IsNullOrEmpty(itemName) ? name : itemName;

    public int Price => ItemTierRules.Price(tier);
    public int Refund => ItemTierRules.Refund(tier);
    public Color TierColor => ItemTierRules.TierColor(tier);

    /// <summary>툴팁 본문. 등급 표기를 설명 앞에 붙인다.</summary>
    public string TooltipBody => $"[{ItemTierRules.Label(tier)}]\n{description}";
}

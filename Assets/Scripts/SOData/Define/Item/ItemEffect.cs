using UnityEngine;

/// <summary>
/// 아이템 효과 한 조각. ItemSO.effects 에 [SerializeReference] 로 담기므로 아이템 하나가
/// 여러 개를 가질 수 있고, 새 종류를 추가할 때 ItemSO 의 필드를 늘리지 않아도 된다.
///
/// 지금 있는 종류:
///   · ItemStatEffect        — 주머니에 있는 동안 상시 플레이어 스탯 보정 (최대 체력 +10 등)
///   · ItemDamageBonusEffect — 조건이 맞는 피해에만 얹히는 보너스 (빙결 파괴 +5, HP 만피 대상 +10% 등)
///
/// 강화/성장 개념은 없다 — 아이템 효과는 고정이다(장비의 enhanceLevel 과 다른 점).
/// </summary>
[System.Serializable]
public abstract class ItemEffect
{
    [Tooltip("인스펙터 식별용 라벨(표기 전용).")]
    public string label;
}

/// <summary>
/// 주머니에 들어 있는 동안 플레이어 스탯을 보정한다.
/// 실제 적용/회수는 ItemPouch 가 자기 자신을 source 키로 써서 통째로 다시 계산한다.
/// </summary>
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
}

/// <summary>대상(적)의 상태를 보는 조건.</summary>
public enum ItemDamageCondition
{
    Always,          // 조건 없음
    TargetAtFullHP,  // 대상 체력이 100%
    TargetHasStatus, // 대상이 requiredStatus 를 갖고 있음
}

/// <summary>
/// 조건이 맞는 피해에만 보너스를 얹는다. ItemPouch 가 DamageEventBus.OnBeforeDamageCalculated
/// 에서 판정하므로 '피해가 깎이기 전' 상태를 본다 — 대상 HP 는 아직 안 줄었고 방어력도 안 먹었다.
///
/// [빙결 깨기 피해를 늘리는 법] filterByDamageType = true, damageType = Freeze 로 두면 된다.
/// 빙결 파괴 피해는 CharacterStatus 가 DealSelfDamage 로 쏘는 DamageType.Freeze / 갈래 Debuff 인데,
/// 그 피해도 이 이벤트를 그대로 타므로 여기서 잡힌다. 조건을 TargetHasStatus(Freeze) 로 잡으면
/// 안 된다 — 파괴 피해가 나가는 시점엔 이미 빙결이 해제돼 있어서 절대 안 걸린다.
///
/// [주의] 방어 무시 여부는 원래 피해의 성격을 그대로 따라간다. 빙결 파괴는 고정피해라
/// flatBonus 가 방어력을 무시하고 그대로 들어가지만, 평타에 얹은 flatBonus 는 방어력을 먹는다.
/// </summary>
[System.Serializable]
public class ItemDamageBonusEffect : ItemEffect
{
    [Header("조건 (대상 상태)")]
    public ItemDamageCondition condition = ItemDamageCondition.Always;

    [Tooltip("condition 이 TargetHasStatus 일 때만 쓴다.")]
    public StatusType requiredStatus = StatusType.Freeze;

    [Header("필터 (내 공격의 종류)")]
    [Tooltip("켜면 특정 피해 속성에만 적용된다. 빙결 파괴 피해를 늘리려면 켜고 Freeze 로.")]
    public bool filterByDamageType = false;
    public DamageType damageType = DamageType.Physical;

    [Tooltip("켜면 특정 갈래에만 적용된다. 평타만 늘리려면 켜고 BasicAttack 으로.")]
    public bool filterByCategory = false;
    public DamageCategory category = DamageCategory.BasicAttack;

    [Header("보너스")]
    [Tooltip("더해지는 고정치.")]
    public float flatBonus = 0f;
    [Tooltip("곱해지는 비율. 0.1 = +10%.")]
    public float percentBonus = 0f;

    /// <summary>이 피해에 보너스를 얹어야 하는가.</summary>
    public bool Matches(CharacterHealth target, in DamageInfo info)
    {
        if (filterByDamageType && info.type != damageType) return false;
        if (filterByCategory && info.category != category) return false;

        switch (condition)
        {
            case ItemDamageCondition.TargetAtFullHP:
                // 아직 이번 피해가 안 깎인 상태다. float 비교라 아주 작은 오차는 만피로 본다.
                return target != null && target.MaxHP > 0f && target.CurHP >= target.MaxHP - 0.01f;

            case ItemDamageCondition.TargetHasStatus:
                var st = target != null ? target.GetComponent<CharacterStatus>() : null;
                return st != null && st.HasStatus(requiredStatus);

            default:
                return true;
        }
    }

    /// <summary>보너스를 얹는다. 비율을 먼저 곱하고 고정치를 나중에 더한다.</summary>
    public void Apply(ref DamageInfo info)
    {
        if (percentBonus != 0f) info.amount *= (1f + percentBonus);
        if (flatBonus != 0f) info.amount += flatBonus;
    }
}

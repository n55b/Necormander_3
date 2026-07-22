using UnityEngine;

/// <summary>
/// 장비 패시브 한 개. EquipmentSO.passives 에 [SerializeReference] 로 담기므로 종류를 자유롭게 늘릴 수 있고
/// 장비 하나가 여러 개를 가질 수 있다.
///
/// 두 갈래:
///   · 스탯증가형 (EquipmentStatPassive)   — 놀고 있던 StatModifierRegistry 에 장비를 소스로 얹는다.
///   · 추가능력형 (EquipmentAbilityPassive) — '디버프가 스킬에 적용' / '디버프 데미지↑' 류. 지금은 데이터+질의 경로만.
///
/// 착용 시 Apply, 해제 시 Remove. 강화 스케일을 위해 enhanceLevel 을 받는 자리를 열어뒀다(수치 미정 → 경로만).
/// </summary>
[System.Serializable]
public abstract class EquipmentPassive
{
    [Tooltip("인스펙터 식별용 라벨(표기 전용).")]
    public string label;

    /// <summary>착용 시. source 는 이 장비 인스턴스 — 해제 때 Mods.RemoveSource(source) 로 통째로 걷어내는 키.</summary>
    public abstract void Apply(CharacterStat stat, object source, int enhanceLevel);

    /// <summary>해제 시. 스탯 보정은 매니저의 Mods.RemoveSource(source) 가 일괄 처리하므로 대개 비워도 된다.</summary>
    public virtual void Remove(CharacterStat stat, object source) { }
}

/// <summary>스탯증가형 패시브. 예: 공격력 +10%, 최대체력 +20.</summary>
[System.Serializable]
public class EquipmentStatPassive : EquipmentPassive
{
    public StatType statType = StatType.Attack;

    [Tooltip("보정치. isPercent 면 0.1 = +10%, 아니면 고정치.")]
    public float value = 0.1f;

    [Tooltip("true=비율(합연산), false=고정치.")]
    public bool isPercent = true;

    public override void Apply(CharacterStat stat, object source, int enhanceLevel)
    {
        if (stat == null) return;
        // ponytail: 강화 스케일 미정 → 지금은 value 를 그대로 건다. enhanceLevel 반영은 수치 정해지면 여기서.
        if (isPercent) stat.Mods.AddPercent(source, statType, value);
        else           stat.Mods.AddFlat(source, statType, value);
    }
    // Remove: 매니저가 Mods.RemoveSource(source) 로 처리 → 오버라이드 불필요.
}

/// <summary>추가능력형 패시브. 지금은 '어떤 능력을 얼마 세기로 갖는지' 데이터만 — 실제 발동은 소비처가 질의로 가져간다.</summary>
[System.Serializable]
public class EquipmentAbilityPassive : EquipmentPassive
{
    public EquipmentAbilityType ability = EquipmentAbilityType.None;

    [Tooltip("추가능력의 세기(예: 디버프 데미지 배수). 의미는 ability 별. 강화 시 이 값이 오르는 자리(경로).")]
    public float magnitude = 1f;

    public override void Apply(CharacterStat stat, object source, int enhanceLevel)
    {
        // ponytail: 질의형이라 착용 시 걸어둘 영구 상태가 없다.
        // 소비처(스킬 방출 / DoT 계산)가 PlayerSkillInventoryManager.GetEquippedAbilityMagnitude 로 가져간다.
        // 실제 훅 연결은 능력 내용이 확정되면.
    }
}

/// <summary>추가능력 종류. 표기용 겸 소비처 질의 키.</summary>
public enum EquipmentAbilityType
{
    None = 0,
    ApplyDebuffToSkills,   // 디버프가 플레이어 스킬에 적용됨
    DebuffDamageUp,        // 디버프 데미지 상승
}

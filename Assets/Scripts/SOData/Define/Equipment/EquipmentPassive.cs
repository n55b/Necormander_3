using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스탯 보정 한 조각(재사용). 강화 레벨당 증가(valuePerLevel)를 스스로 갖는다.
/// EquipmentStatPassive(단일) 와 EquipmentBuffPassive(리스트) 가 공유한다.
/// </summary>
[System.Serializable]
public class StatEffect
{
    public StatType statType = StatType.Attack;

    [Tooltip("보정치. isPercent 면 0.1 = +10%, 아니면 고정치.")]
    public float value = 0.1f;

    [Tooltip("true=비율(합연산), false=고정치.")]
    public bool isPercent = true;

    [Tooltip("강화 레벨당 증가분(value 단위). 0 이면 강화 무관.")]
    public float valuePerLevel = 0f;

    public float Effective(int level) => value + valuePerLevel * Mathf.Max(0, level);

    public void Apply(CharacterStat stat, object source, int level)
    {
        if (stat == null) return;
        float v = Effective(level);
        if (isPercent) stat.Mods.AddPercent(source, statType, v);
        else           stat.Mods.AddFlat(source, statType, v);
    }
}

/// <summary>
/// 장비 패시브 한 개. EquipmentSO.passives 에 [SerializeReference] 로 담기므로 종류를 자유롭게 늘릴 수 있고
/// 장비 하나가 여러 개(1개 이상)를 가질 수 있다.
///
/// 종류(설계):
///   · 스탯형     (EquipmentStatPassive)          — 착용 중 상시, 플레이어 스탯 증가.
///   · 상태이상형 (EquipmentStatusPassive)        — 특정 상태이상의 부여 확률/강도 증가. 질의형.
///   · 조건형버프 (EquipmentConditionBuffPassive) — 조건 성립 '중' 계속 유지되는 스탯 버프(전투 고양). 매니저가 매 프레임 토글.
///   · 발동형버프 (EquipmentTriggerBuffPassive)   — 이벤트로 발동해 '다음 타격'에 소비되는 1회성 버프(밀침/끌기→다음 1타 강화).
/// (Q/E 스킬 데미지 강화는 각 스킬의 강화 커브(PlayerSkillSO.enhanceEffects)가 담당 — 장비 패시브 아님.)
///
/// 강화 스케일은 각 패시브가 자기 파라미터별로 enhanceLevel 을 반영한다(전역 배수 아님).
/// 나중에 미니언 패시브로 이관하려면 이 타입들을 그대로 재사용하면 된다(전부 CharacterStat + source 로만 동작).
/// </summary>
[System.Serializable]
public abstract class EquipmentPassive
{
    [Tooltip("인스펙터 식별용 라벨(표기 전용).")]
    public string label;

    /// <summary>착용 시. source 는 이 장비 인스턴스(해제 때 Mods.RemoveSource(source) 로 통째로 걷어내는 키).
    /// enhanceLevel = 장비 강화레벨(각 패시브가 파라미터별로 반영).</summary>
    public abstract void Apply(CharacterStat stat, object source, int enhanceLevel);

    /// <summary>해제 시. 스탯 보정은 매니저의 Mods.RemoveSource(source) 가 일괄 처리하므로 대개 비워도 된다.</summary>
    public virtual void Remove(CharacterStat stat, object source) { }
}

/// <summary>스탯형: 착용 중 상시 스탯 증가.</summary>
[System.Serializable]
public class EquipmentStatPassive : EquipmentPassive
{
    public StatEffect effect = new StatEffect();

    public override void Apply(CharacterStat stat, object source, int enhanceLevel)
        => effect?.Apply(stat, source, enhanceLevel);
    // Remove: 매니저가 Mods.RemoveSource(source) 로 처리 → 오버라이드 불필요.
}

/// <summary>
/// 상태이상형: 특정 상태이상의 부여 확률/강도를 올린다. 질의형(착용 시 걸어둘 영구 상태 없음) —
/// 소비처(스킬 타격이 굴릴 때, DoT/지속 계산 때)가 매니저 질의(GetStatus*)로 값을 가져간다.
/// 확률만 / 강도만 / 둘 다 전부 가능 — 안 쓰는 건 0 으로 둔다.
/// </summary>
[System.Serializable]
public class EquipmentStatusPassive : EquipmentPassive
{
    [Tooltip("어떤 상태이상에 대한 증폭인지. (예: Freeze)")]
    public StatusType status = StatusType.None;

    [Tooltip("Q/E 스킬 타격이 이 상태이상을 부여할 확률 보너스. 0.2 = +20%p.")]
    public float applyChanceBonus = 0f;
    [Tooltip("확률 보너스의 강화 레벨당 증가분.")]
    public float applyChancePerLevel = 0f;

    [Tooltip("이 상태이상의 강도 보너스(지속·피해 등). 0.3 = +30%.")]
    public float strengthBonus = 0f;
    [Tooltip("강도 보너스의 강화 레벨당 증가분.")]
    public float strengthPerLevel = 0f;

    public float EffectiveChance(int level) => applyChanceBonus + applyChancePerLevel * Mathf.Max(0, level);
    public float EffectiveStrength(int level) => strengthBonus + strengthPerLevel * Mathf.Max(0, level);

    public override void Apply(CharacterStat stat, object source, int enhanceLevel) { } // 질의형 — no-op
}

/// <summary>
/// 조건형 버프: 조건이 성립하는 '동안' effects 의 스탯을 부여한다(전투 고양 등).
/// 매니저(PlayerSkillInventoryManager)가 매 프레임 조건을 굴려 on/off 토글한다 — Apply 는 no-op.
///
/// InCombatNoRecentHit: 전투 중(방에 적 있음) + 마지막 피격 후 EffectiveDelay 초 경과 → ON.
///                      피격 시 OFF + 타이머 리셋. (나중에 Always 등 조건을 enum 에 추가하면 항시 버프도 같은 타입으로.)
/// </summary>
[System.Serializable]
public class EquipmentConditionBuffPassive : EquipmentPassive
{
    public BuffCondition condition = BuffCondition.InCombatNoRecentHit;

    [Tooltip("조건 재성립까지 필요한 무피격 시간(초). InCombatNoRecentHit 에서 사용.")]
    public float reapplyDelay = 5f;
    [Tooltip("무피격 시간의 강화 레벨당 증가분(초). 예: -0.1 이면 레벨당 0.1초 단축(5→4.5).")]
    public float reapplyDelayPerLevel = 0f;

    [Tooltip("조건 성립 중 부여할 스탯들(각자 강화 커브 가능).")]
    public List<StatEffect> effects = new List<StatEffect>();

    public float EffectiveDelay(int level) => Mathf.Max(0f, reapplyDelay + reapplyDelayPerLevel * Mathf.Max(0, level));

    public override void Apply(CharacterStat stat, object source, int enhanceLevel) { } // 드라이버 관리 — no-op
}

/// <summary>조건형 버프 발동 조건. 확장 가능(예: 나중에 Always 추가 → 항시 버프).</summary>
public enum BuffCondition
{
    InCombatNoRecentHit,   // 전투 중 + 최근 피격 없음
}

/// <summary>
/// 발동형 버프: 특정 이벤트로 발동해 잠깐 유지되다 '다음 타격'에 소비되는 1회성 버프(투지 등).
/// 매니저가 트리거 이벤트를 구독해 켜고, 다음 조건 타격의 데미지를 boost 한 뒤 소비한다. 비중첩(발동 시 지속만 갱신).
///
/// OnPushOrPull: 플레이어 진영이 적을 밀치거나 끌어당기면 발동. 소비 대상 = 다음 플레이어 기본공격 or 스킬 타격.
/// </summary>
[System.Serializable]
public class EquipmentTriggerBuffPassive : EquipmentPassive
{
    public BuffTrigger trigger = BuffTrigger.OnPushOrPull;

    [Tooltip("발동 후 유지 시간(초). 이 안에 조건 타격을 안 하면 사라진다.")]
    public float buffDuration = 3f;

    [Tooltip("소비되는 타격의 데미지 증가율. 0.2 = +20%.")]
    public float damageBonus = 0.2f;
    [Tooltip("데미지 증가율의 강화 레벨당 증가분. 0.02 = 레벨당 +2%.")]
    public float damageBonusPerLevel = 0f;

    public float EffectiveBonus(int level) => damageBonus + damageBonusPerLevel * Mathf.Max(0, level);

    public override void Apply(CharacterStat stat, object source, int enhanceLevel) { } // 드라이버 관리 — no-op
}

/// <summary>발동형 버프 트리거. 확장 가능.</summary>
public enum BuffTrigger
{
    OnPushOrPull,   // 적을 밀치거나 끌어당김
}

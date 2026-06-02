using UnityEngine;
using System;

/// <summary>
/// 전투 중 발생하는 주요 이벤트(피격, 회피 등)를 전역적으로 중계하는 이벤트 버스입니다.
/// 보석(Gem) 핸들러들이 특정 엔티티 코드에 결합되지 않고 이 버스를 통해 효과를 적용합니다.
/// </summary>
public static class DamageEventBus
{
    /// <summary>
    /// 공격이 명중하여 데미지 계산이 시작되기 직전 (여기서 데미지 증폭/감소 연산 수행)
    /// 인자: 피해자(CharacterHealth), 공격 정보(ref DamageInfo)
    /// </summary>
    public delegate void DamageCalculationHandler(CharacterHealth target, ref DamageInfo info);
    public static event DamageCalculationHandler OnBeforeDamageCalculated;

    /// <summary>
    /// 실제 데미지를 입었을 때 (반사 대미지, 타격 횟수 누적 등에 사용)
    /// 인자: 피해자(CharacterHealth), 공격 정보(DamageInfo)
    /// </summary>
    public static event Action<CharacterHealth, DamageInfo> OnDamageReceived;

    /// <summary>
    /// 공격이 빗나갔을 때 (회피 발생 시)
    /// 인자: 피해자(CharacterHealth), 공격자 스탯(CharacterStat)
    /// </summary>
    public static event Action<CharacterHealth, CharacterStat> OnEvasionOccurred;

    /// <summary>
    /// 엔티티가 사망했을 때 (처형, 시체 폭발 등에 사용)
    /// 인자: 사망자(CharacterHealth), 공격 정보(DamageInfo)
    /// </summary>
    public static event Action<CharacterHealth, DamageInfo> OnEntityDied;

    /// <summary>
    /// 처형 임계치를 계산하기 직전 (단두대 등이 임계치 증폭)
    /// </summary>
    public delegate void ExecutionCalculationHandler(CharacterHealth target, ref float executeThreshold);
    public static event ExecutionCalculationHandler OnBeforeExecutionCalculated;

    /// <summary>
    /// 처형이 발동하여 사망했을 때 (공포 등 발동)
    /// </summary>
    public static event Action<CharacterHealth> OnEntityExecuted;

    /// <summary>
    /// 체력 회복 전 힐량 증폭 계산을 위한 이벤트
    /// </summary>
    public delegate void HealCalculationHandler(CharacterHealth target, ref float healAmount);
    public static event HealCalculationHandler OnBeforeHealCalculated;

    // --- 이벤트 트리거 헬퍼 함수 --- //

    public static void TriggerBeforeDamageCalculated(CharacterHealth target, ref DamageInfo info)
    {
        OnBeforeDamageCalculated?.Invoke(target, ref info);
    }

    public static void TriggerDamageReceived(CharacterHealth target, DamageInfo info)
    {
        OnDamageReceived?.Invoke(target, info);
    }

    public static void TriggerEvasionOccurred(CharacterHealth target, CharacterStat attackerStat)
    {
        OnEvasionOccurred?.Invoke(target, attackerStat);
    }

    public static void TriggerEntityDied(CharacterHealth target, DamageInfo info)
    {
        OnEntityDied?.Invoke(target, info);
    }

    public static void TriggerBeforeExecutionCalculated(CharacterHealth target, ref float threshold)
    {
        OnBeforeExecutionCalculated?.Invoke(target, ref threshold);
    }

    public static void TriggerEntityExecuted(CharacterHealth target)
    {
        OnEntityExecuted?.Invoke(target);
    }

    public static void TriggerBeforeHealCalculated(CharacterHealth target, ref float healAmount)
    {
        OnBeforeHealCalculated?.Invoke(target, ref healAmount);
    }
}

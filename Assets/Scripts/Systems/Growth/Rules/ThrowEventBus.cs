using System;
using UnityEngine;

public static class ThrowEventBus
{
    // 투척 시작 시 (데미지/범위 증폭 등 투척 스탯 결정 및 체공 시간 조절)
    public delegate void ThrowStartHandler(CommandData job, ref float damageMult, ref float radiusMult, ref float durationMult);
    public static event ThrowStartHandler OnThrowStart;

    // 투척 착탄 데미지 계산 직전 (착탄 전용 추가 피해나 범위 등)
    public delegate void ThrowImpactBeforeDamageHandler(CommandData job, ImpactAction action, Vector2 position, ref float finalDamage, ref float radius, GameObject target);
    public static event ThrowImpactBeforeDamageHandler OnThrowImpactBeforeDamage;

    // 투척 데미지 계산 및 적용 직후 (특수 장판 생성이나 추가 상태이상 등)
    public delegate void ThrowImpactAfterDamageHandler(CommandData job, ImpactAction action, Vector2 position, Collider2D[] hitTargets, GameObject singleTarget);
    public static event ThrowImpactAfterDamageHandler OnThrowImpactAfterDamage;

    public static void TriggerThrowStart(CommandData job, ref float damageMult, ref float radiusMult, ref float durationMult)
    {
        OnThrowStart?.Invoke(job, ref damageMult, ref radiusMult, ref durationMult);
    }

    public static void TriggerThrowImpactBeforeDamage(CommandData job, ImpactAction action, Vector2 position, ref float finalDamage, ref float radius, GameObject target)
    {
        OnThrowImpactBeforeDamage?.Invoke(job, action, position, ref finalDamage, ref radius, target);
    }

    public static void TriggerThrowImpactAfterDamage(CommandData job, ImpactAction action, Vector2 position, Collider2D[] hitTargets, GameObject singleTarget)
    {
        OnThrowImpactAfterDamage?.Invoke(job, action, position, hitTargets, singleTarget);
    }
}

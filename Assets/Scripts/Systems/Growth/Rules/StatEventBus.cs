using System;
using UnityEngine;

public struct StatModifiers
{
    public float atkMultiplier;
    public float hpMultiplier;
    public float speedMultiplier;
    // 향후 스탯 종류 추가 시 여기에 필드 추가

    public void Apply(CharacterStat stat)
    {
        // stat 내부에 public 속성이 없으므로, stat 계산 식 자체에 참여하도록 설계
    }
}

public static class StatEventBus
{
    /// <summary>
    /// 아군 전용 스탯 계산 전 이벤트
    /// </summary>
    public delegate void AllyStatCalculationHandler(CharacterStat stat, ref float atkMult, ref float hpMult, ref float atkSpdMult, ref float moveSpdMult);
    public static event AllyStatCalculationHandler OnAllyStatCalculate;

    /// <summary>
    /// 적군 전용 스탯 계산 전 이벤트
    /// </summary>
    public delegate void EnemyStatCalculationHandler(CharacterStat stat, ref float atkMult, ref float hpMult, ref float atkSpdMult, ref float moveSpdMult);
    public static event EnemyStatCalculationHandler OnEnemyStatCalculate;

    public delegate void PreviewStatCalculateHandler(CommandData minionType, ref float hp, ref float atk, ref float moveSpeed);
    public static event PreviewStatCalculateHandler OnPreviewStatCalculate;

    public static void TriggerStatCalculate(CharacterStat stat, ref float atkMult, ref float hpMult, ref float atkSpdMult, ref float moveSpdMult)
    {
        if (stat.IsEnemy)
        {
            OnEnemyStatCalculate?.Invoke(stat, ref atkMult, ref hpMult, ref atkSpdMult, ref moveSpdMult);
        }
        else
        {
            OnAllyStatCalculate?.Invoke(stat, ref atkMult, ref hpMult, ref atkSpdMult, ref moveSpdMult);
        }
    }

    public static void TriggerPreviewStatCalculate(CommandData minionType, ref float hp, ref float atk, ref float moveSpeed)
    {
        OnPreviewStatCalculate?.Invoke(minionType, ref hp, ref atk, ref moveSpeed);
    }
}

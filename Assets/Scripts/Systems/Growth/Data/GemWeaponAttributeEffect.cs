using System;
using UnityEngine;

/// <summary>
/// 기본 공격 시 디버프 스택을 부여하는 보석 효과입니다.
/// </summary>
[Serializable]
public class GemWeaponAttributeEffect : GemEffect
{
    public DebuffCategory category;

    [Header("Stack Type (if Category is Stack)")]
    public DebuffStackType debuffType;
    public float stackAmount;

    [Header("Bool Type (if Category is Bool)")]
    public DebuffBoolType boolType;
    public float duration = 10.0f;

    public override string GetDescription() 
    {
        if (category == DebuffCategory.Stack) return $"Attack: +{stackAmount} {debuffType}";
        else return $"Attack: Apply {boolType} ({duration}s)";
    }

    public override void Apply(InventoryManager.GemAggregatedStats targetStats)
    {
        if (category == DebuffCategory.Stack)
        {
            if (!targetStats.WeaponAttributes.ContainsKey(debuffType)) targetStats.WeaponAttributes[debuffType] = 0f;
            targetStats.WeaponAttributes[debuffType] += stackAmount;
        }
        else
        {
            if (!targetStats.WeaponBoolAttributes.ContainsKey(boolType)) targetStats.WeaponBoolAttributes[boolType] = 0f;
            targetStats.WeaponBoolAttributes[boolType] = Mathf.Max(targetStats.WeaponBoolAttributes[boolType], duration);
        }
    }
}

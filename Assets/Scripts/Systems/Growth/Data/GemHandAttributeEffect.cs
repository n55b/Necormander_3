using System;
using UnityEngine;

/// <summary>
/// 던지기 적중 시 디버프 스택을 부여하는 보석 효과입니다.
/// </summary>
[Serializable]
public class GemHandAttributeEffect : GemEffect
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
        if (category == DebuffCategory.Stack) return $"Throw: +{stackAmount} {debuffType}";
        else return $"Throw: Apply {boolType} ({duration}s)";
    }

    public override void Apply(InventoryManager.GemAggregatedStats targetStats)
    {
        if (category == DebuffCategory.Stack)
        {
            if (!targetStats.HandAttributes.ContainsKey(debuffType)) targetStats.HandAttributes[debuffType] = 0f;
            targetStats.HandAttributes[debuffType] += stackAmount;
        }
        else
        {
            if (!targetStats.HandBoolAttributes.ContainsKey(boolType)) targetStats.HandBoolAttributes[boolType] = 0f;
            targetStats.HandBoolAttributes[boolType] = Mathf.Max(targetStats.HandBoolAttributes[boolType], duration);
        }
    }
}

using System;

/// <summary>
/// 기본 공격 시 디버프 스택을 부여하는 보석 효과입니다.
/// </summary>
[Serializable]
public class GemWeaponAttributeEffect : GemEffect
{
    public DebuffStackType debuffType;
    public float stackAmount;

    public override string GetDescription() => $"Attack: +{stackAmount} {debuffType}";
    public override void Apply(InventoryManager.GemAggregatedStats targetStats)
    {
        if (!targetStats.WeaponAttributes.ContainsKey(debuffType)) targetStats.WeaponAttributes[debuffType] = 0f;
        targetStats.WeaponAttributes[debuffType] += stackAmount;
    }
}

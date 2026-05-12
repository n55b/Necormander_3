using System;

/// <summary>
/// 던지기 적중 시 디버프 스택을 부여하는 보석 효과입니다.
/// </summary>
[Serializable]
public class GemHandAttributeEffect : GemEffect
{
    public DebuffStackType debuffType;
    public float stackAmount;

    public override string GetDescription() => $"Throw: +{stackAmount} {debuffType}";
    public override void Apply(InventoryManager.GemAggregatedStats targetStats)
    {
        if (!targetStats.HandAttributes.ContainsKey(debuffType)) targetStats.HandAttributes[debuffType] = 0f;
        targetStats.HandAttributes[debuffType] += stackAmount;
    }
}

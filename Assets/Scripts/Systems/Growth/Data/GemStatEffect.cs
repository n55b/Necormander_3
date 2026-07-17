using System;

/// <summary>
/// 기초 스탯(공격력, 체력 등)을 강화하는 보석 효과입니다.
/// </summary>
[Serializable]
public class GemStatEffect : GemEffect
{
    public StatType statType;
    public float value;

    public override string GetDescription() 
    {
        if (statType == StatType.Health) return $"{statType}: +{value}";
        return $"{statType}: +{value * 100}%";
    }
    public override void Apply(InventoryManager.GemAggregatedStats targetStats)
    {
        switch (statType)
        {
            case StatType.Attack: targetStats.AttackBonus += value; break;
            case StatType.Health: targetStats.HealthBonus += value; break;
            case StatType.AttackSpeed: targetStats.AttackSpeedBonus += value; break;
        }
    }
}

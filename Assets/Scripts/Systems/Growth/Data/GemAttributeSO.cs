using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 미니언의 기초 스탯(공격력, 체력 등)을 강화하는 보석입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewAttributeGem", menuName = "Necromancer/Growth/Gem - Attribute")]
public class GemAttributeSO : GemSO
{
    [Header("강화 수치")]
    public StatType statType;
    public float baseBonusValue;

    public override GrowthItemData GetDynamicDisplayData(CommandData job)
    {
        string jobName = job.ToString().Replace("Skeleton", "");
        string bonusInfo = $"Enhances {jobName}'s {GetStatName()} by {baseBonusValue * 100}%";
        string finalDesc = string.IsNullOrEmpty(this.description) ? bonusInfo : $"{this.description}\n({bonusInfo})";

        return new GrowthItemData {
            itemName = $"[{jobName}] {itemName}",
            description = finalDesc,
            icon = this.icon,
            rarity = this.rarity
        };
    }

    public override List<StatModifier> GetStatModifiers()
    {
        return new List<StatModifier> { new StatModifier(statType, baseBonusValue) };
    }

    private string GetStatName()
    {
        switch (statType)
        {
            case StatType.Attack: return "Attack Damage";
            case StatType.Health: return "Max Health";
            case StatType.AttackSpeed: return "Attack Speed";
            case StatType.RespawnTime: return "Respawn Speed";
            case StatType.ThrowEffect: return "Throw Ability";
            default: return "Movement Speed";
        }
    }
}

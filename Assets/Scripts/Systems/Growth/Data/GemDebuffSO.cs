using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 투척 적중 시 적에게 상태 이상(중독, 한기 등)을 부여하는 보석입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewDebuffGem", menuName = "Necromancer/Growth/Gem - Debuff")]
public class GemDebuffSO : GemSO
{
    [Header("디버프 설정")]
    public DebuffStackType targetDebuffType;
    public float baseDebuffStack = 1.0f;

    public override GrowthItemData GetDynamicDisplayData(CommandData job)
    {
        string jobName = job.ToString().Replace("Skeleton", "");
        string bonusInfo = $"Applies {baseDebuffStack} stacks of {targetDebuffType}";
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
        // GemDebuffSO는 직접적인 스탯 보너스를 제공하지 않으므로, 현재는 빈 리스트를 반환합니다.
        // 향후 디버프 스택 관련 시너지 등을 위해 확장될 수 있습니다.
        return new List<StatModifier>();
    }
}

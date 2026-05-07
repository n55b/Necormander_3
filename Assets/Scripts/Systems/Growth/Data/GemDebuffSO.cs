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
}

using UnityEngine;

[CreateAssetMenu(fileName = "GemPrizeSOData", menuName = "Necromancer/Prize/GemPrizeSOData")]
public class GemPrizeSOData : PrizeDataSO
{
    [SerializeField] GemSO _gem;

    public override void BuyItem()
    {
        RewardCandidate reward = 
        new RewardCandidate { 
            displayData = _gem.GetDynamicDisplayData(CommandData.SkeletonWarrior), 
            rawData = _gem, 
            category = RewardCategory.Gem,
            targetJob = CommandData.SkeletonWarrior // 내부 우회용 기본값
        };

        RewardManager.Instance.ApplyReward(reward);
    }
}

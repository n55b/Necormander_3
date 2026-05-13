using UnityEngine;

[CreateAssetMenu(fileName = "MinionPrizeSOData", menuName = "Necromancer/Prize/MinionPrizeSOData")]
public class MinionPrizeSOData : PrizeDataSO
{
    [SerializeField] MinionLineageSO _minion;

    public override void BuyItem()
    {
        RewardCandidate reward = new RewardCandidate { displayData = _minion.baseItemData, rawData = _minion, techIndex = 0, category = RewardCategory.Minion };
        RewardManager.Instance.ApplyReward(reward);
    }
}

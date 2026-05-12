using UnityEngine;

[CreateAssetMenu(fileName = "MinionPrizeSOData", menuName = "Necromancer/Prize/PrizeDataSO")]
public class MinionPrizeSOData : PrizeDataSO
{
    [SerializeField] MinionDataSO _minion;
}

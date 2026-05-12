using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 모든 던전내 상품 아이템을 관리하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "PrizeRegistry", menuName = "Necromancer/Registry/PrizeRegistry")]
public class PrizeRegistrySO : ScriptableObject
{
    [Header("던전 내 상품 아이템 데이터")]
    public List<PrizeDataSO> prizeData;
}

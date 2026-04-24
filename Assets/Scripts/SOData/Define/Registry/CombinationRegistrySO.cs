using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 던지기 조합 시너지 데이터를 관리하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "CombinationRegistry", menuName = "Necromancer/Data/CombinationRegistry")]
public class CombinationRegistrySO : ScriptableObject
{
    [Header("모든 던지기 조합 데이터")]
    public List<ThrowCombinationSO> allCombinations;
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 모든 미니언 데이터를 아군과 적군으로 구분하여 관리하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "MinionRegistry", menuName = "Necromancer/Registry/MinionRegistry")]
public class MinionRegistrySO : ScriptableObject
{
    [Header("아군 미니언 데이터")]
    public List<MinionDataSO> allyMinionData;

    [Header("적군 미니언 데이터")]
    public List<MinionDataSO> enemyMinionData;
}

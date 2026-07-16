using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 모든 미니언 데이터를 아군과 적군으로 구분하여 관리하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "MinionRegistry", menuName = "Necromancer/Registry/MinionRegistry")]
public class MinionRegistrySO : ScriptableObject
{
    [Header("아군 미니언 데이터")]
    public List<MinionDataSO> allyMinionData; // 소환수(메인/서브 혼재)라 공통 base 로 받는다

    [Header("Enemy Minions")]
    public List<EnemyMinionDataSO> enemyMinionData;

    [Header("Elite Minions")]
    public List<EnemyMinionDataSO> eliteMinionData; // 엘리트 전용 데이터 목록

    [Header("Boss Minions")]
    public List<EnemyMinionDataSO> bossMinionData; // 보스 전용 데이터 목록
}

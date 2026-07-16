using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct EnemyCount
{
    public EnemyMinionDataSO enemyData;
    public int count;
}

[CreateAssetMenu(fileName = "NewEnemyCluster", menuName = "Necromancer/Data/EnemyCluster")]
public class EnemyClusterSO : ScriptableObject
{
    public string clusterName = "새로운 군집";
    [Tooltip("이 군집에 포함될 적과 그 수")]
    public List<EnemyCount> enemies = new List<EnemyCount>();
}

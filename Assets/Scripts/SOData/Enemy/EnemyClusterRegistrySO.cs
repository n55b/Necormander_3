using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemyClusterRegistry", menuName = "Necromancer/Registry/EnemyClusterRegistry")]
public class EnemyClusterRegistrySO : ScriptableObject
{
    public List<EnemyClusterSO> enemyClusters = new List<EnemyClusterSO>();
}

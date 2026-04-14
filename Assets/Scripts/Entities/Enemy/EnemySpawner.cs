using Unity.Collections;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("소환할 프리팹")]
    [SerializeField] GameObject enemyPrefab;

    [Header("소환 쿨타임")]
    [SerializeField] float coolTime;
    [SerializeField]float curTime = 0.0f;

    private void Update()
    {
        if(curTime < coolTime)
        {
            curTime += Time.deltaTime;
        }
        else
        {
            SpawnEnemy();
            curTime = 0.0f;
        }
    }

    private void SpawnEnemy()
    {
        GameObject obj = Instantiate(enemyPrefab);
        obj.transform.position = transform.position;
    }
}

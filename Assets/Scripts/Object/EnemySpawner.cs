using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("소환할 프리팹")]
    [SerializeField] GameObject enemyPrefab;

    [Header("소환 설정")]
    [SerializeField] int firstSpawn = 20;
    [SerializeField] int continueSpawn = 5;
    int curRespawnCount = 0;

    [Header("소환 쿨타임")]
    [SerializeField] float coolTime;
    [SerializeField] float curTime = 0.0f;

    public LayerMask obstacleLayer; // 벽이나 장애물 레이어 설정

    private void Update()
    {
        if(curRespawnCount >= continueSpawn)
            return;

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
        int n = 5;
        if(curRespawnCount == 0)
        {
            n = firstSpawn;
        }

        List<Vector2> pos = GetSummonPositions2D(n, 10.0f);

        for(int i = 0; i < n; i++)
        {
            GameObject obj = Instantiate(enemyPrefab);
            Vector3 vec = (Vector3)pos[i];
            vec.z = 10.0f;
            obj.transform.position = vec;
        }
        curRespawnCount++;
    }

    public List<Vector2> GetSummonPositions2D(int count, float radius)
    {
        List<Vector2> resultPositions = new List<Vector2>();
        int attempts = 0;
        int maxAttempts = count * 10;

        while (resultPositions.Count < count && attempts < maxAttempts)
        {
            attempts++;

            Vector2 randomPos = (Vector2)transform.position + (Random.insideUnitCircle * radius);
            Collider2D hit = Physics2D.OverlapCircle(randomPos, 0.2f, obstacleLayer);

            if (hit == null)
            {
                if (!IsTooClose(randomPos, resultPositions, 0.5f))
                {
                    resultPositions.Add(randomPos);
                }
            }
        }
        return resultPositions;
    }

    private bool IsTooClose(Vector2 pos, List<Vector2> list, float minDistance)
    {
        foreach (Vector2 p in list)
        {
            if (Vector2.Distance(pos, p) < minDistance) return true;
        }
        return false;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum SpawnType
{
    Periodic,   // 기존의 주기적 소환
    Encounter   // 엔터 더 건전 스타일 (방 진입 시 기습)
}

/// <summary>
/// 플레이어 감지 시 적을 생성하는 스포너입니다. 
/// 주기적 소환 또는 랜덤 위치 기습(Encounter) 모드를 지원합니다.
/// </summary>
public class DynamicEnemySpawner : MonoBehaviour
{
    [Header("General Settings")]
    [SerializeField] private SpawnType spawnType = SpawnType.Encounter;
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private float activationRange = 10.0f;

    [Header("Encounter Settings (Gungeon Style)")]
    [SerializeField] private int groupsCount = 4;           // 생성할 부대(그룹)의 수
    [SerializeField] private int enemiesPerGroup = 3;       // 각 부대당 마릿수
    [SerializeField] private float spawnDistanceFromCenter = 7.0f; // 중심에서 최대 얼마나 떨어진 곳까지 생성할지
    [SerializeField] private float groupSpread = 1.5f;      // 부대 내 배치 간격
    [SerializeField] private bool triggerOnlyOnce = true;  // 한 번만 발동할지

    [Header("Periodic Settings")]
    [SerializeField] private float spawnInterval = 3.0f;
    [SerializeField] private float spawnRadius = 5.0f;
    [SerializeField] private int maxActiveEnemies = 10;

    [Header("Visual Debug")]
    [SerializeField] private bool showGizmos = true;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private float _spawnTimer;
    private Transform _playerTransform;
    private bool _isTriggered = false;

    private void Start()
    {
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            _playerTransform = GameManager.Instance.PLAYERCONTROLLER.transform;
        }
        else
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) _playerTransform = playerObj.transform;
        }
    }

    private void Update()
    {
        if (_playerTransform == null) return;
        if (spawnType == SpawnType.Encounter && _isTriggered) return;

        float distanceToPlayer = Vector2.Distance(transform.position, _playerTransform.position);

        if (distanceToPlayer <= activationRange)
        {
            if (spawnType == SpawnType.Encounter)
            {
                TriggerEncounter();
            }
            else
            {
                UpdatePeriodicSpawn();
            }
        }
    }

    private void TriggerEncounter()
    {
        _isTriggered = true;
        Debug.Log($"<color=red>[Encounter]</color> Ambush! Spawning {groupsCount} groups randomly.");

        for (int i = 0; i < groupsCount; i++)
        {
            // [변경점] 고정 각도 대신 원 내의 랜덤한 지점을 부대의 중심점으로 선택
            Vector2 randomPosInsideCircle = Random.insideUnitCircle * spawnDistanceFromCenter;
            Vector3 groupCenter = transform.position + new Vector3(randomPosInsideCircle.x, randomPosInsideCircle.y, 0f);

            SpawnGroup(groupCenter);
        }
    }

    private void SpawnGroup(Vector3 center)
    {
        for (int i = 0; i < enemiesPerGroup; i++)
        {
            Vector2 offset = Random.insideUnitCircle * groupSpread;
            Vector3 spawnPos = center + new Vector3(offset.x, offset.y, 0f);

            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                spawnPos = hit.position;
            }

            GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
            _activeEnemies.Add(enemy);
        }
    }

    private void UpdatePeriodicSpawn()
    {
        _activeEnemies.RemoveAll(item => item == null);
        if (_activeEnemies.Count >= maxActiveEnemies) return;

        _spawnTimer += Time.deltaTime;
        if (_spawnTimer >= spawnInterval)
        {
            Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
            Vector3 spawnPos = transform.position + new Vector3(randomCircle.x, randomCircle.y, 0f);

            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                spawnPos = hit.position;
            }

            GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
            _activeEnemies.Add(enemy);
            _spawnTimer = 0f;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        // 플레이어 감지 범위 (하늘색)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, activationRange);

        if (spawnType == SpawnType.Encounter)
        {
            // 기습 가능 영역 표시 (빨간색)
            Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
            Gizmos.DrawSphere(transform.position, spawnDistanceFromCenter);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, spawnDistanceFromCenter);
        }
        else
        {
            // 주기적 소환 범위 (노란색)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
        }
    }
}

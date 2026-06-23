using UnityEngine;
using System.Collections;

/// <summary>
/// 압력 발판을 밟으면 방의 무작위 벽면 지점에서 발판 위치를 향해 화살을 연속 발사하는 함정입니다.
/// </summary>
public class TrapArrow : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float fireDelay = 1f;
    [SerializeField] private float cooldown = 1f; // 화살 발사 후 다시 작동하기까지의 쿨다운
    [SerializeField] private float damage = 10f;
    [SerializeField] private float arrowSpeed = 15f;
    [SerializeField] private float arrowLifeTime = 3f;
    [SerializeField] private int arrowCount = 3;        // 한 번에 쏘는 화살 개수
    [SerializeField] private float fireInterval = 0.2f; // 연속 발사 시 시간 간격

    [Header("References")]
    [SerializeField] private GameObject arrowPrefab; // Projectile 컴포넌트가 붙어 있는 프리팹
    [SerializeField] private Transform firePoint;     // 화살 스폰 기본 위치 (방을 못 찾을 때의 폴백용)
    [SerializeField] private LayerMask targetLayer;

    private bool _isCooldown = false;

    private void Start()
    {
        if (firePoint == null)
        {
            firePoint = transform;
        }

        // 기본적으로 targetLayer가 설정되어 있지 않다면 Player, Enemy, Ally 등을 포함
        if (targetLayer == 0)
        {
            targetLayer = LayerMask.GetMask("Player", "Enemy", "Ally", "Army", "Player_Dash");
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 타겟 레이어 검사
        if (((1 << other.gameObject.layer) & targetLayer) == 0) return;

        if (!_isCooldown)
        {
            StartCoroutine(FireRoutine());
        }
    }

    private IEnumerator FireRoutine()
    {
        _isCooldown = true;

        // 밟은 후 설정된 지연 시간 대기
        yield return new WaitForSeconds(fireDelay);

        RoomInstance room = GetComponentInParent<RoomInstance>();

        for (int i = 0; i < arrowCount; i++)
        {
            Vector2 spawnPos = GetArrowSpawnPosition(room);
            FireArrow(spawnPos);
            yield return new WaitForSeconds(fireInterval);
        }

        // 발사 완료 후 쿨다운 대기
        yield return new WaitForSeconds(cooldown);

        _isCooldown = false;
    }

    private Vector2 GetArrowSpawnPosition(RoomInstance room)
    {
        if (room == null)
        {
            // 방이 존재하지 않는 맵 빌드 외 환경(테스트 씬 등): 함정 주변 8~12칸 반경의 랜덤 방향
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = Random.Range(8f, 12f);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            return (Vector2)transform.position + offset;
        }

        // 방이 존재하는 경우: 방의 외곽 벽 계산
        Vector2 roomCenter = (Vector2)room.transform.position;
        Vector2 size = (Vector2)room.roomSize;

        // 벽 안쪽에 스폰되도록 약간의 마진(0.5f)을 줌
        float minX = roomCenter.x - size.x * 0.5f + 0.5f;
        float maxX = roomCenter.x + size.x * 0.5f - 0.5f;
        float minY = roomCenter.y - size.y * 0.5f + 0.5f;
        float maxY = roomCenter.y + size.y * 0.5f - 0.5f;

        // 4개의 외벽 방향 중 랜덤 선택 (0: 좌, 1: 우, 2: 하, 3: 상)
        int wallSide = Random.Range(0, 4);
        Vector2 spawnPos = Vector2.zero;

        switch (wallSide)
        {
            case 0: // 왼쪽 벽면
                spawnPos = new Vector2(minX, Random.Range(minY, maxY));
                break;
            case 1: // 오른쪽 벽면
                spawnPos = new Vector2(maxX, Random.Range(minY, maxY));
                break;
            case 2: // 아래쪽 벽면
                spawnPos = new Vector2(Random.Range(minX, maxX), minY);
                break;
            case 3: // 위쪽 벽면
                spawnPos = new Vector2(Random.Range(minX, maxX), maxY);
                break;
        }

        return spawnPos;
    }

    private void FireArrow(Vector2 spawnPos)
    {
        if (arrowPrefab == null)
        {
            Debug.LogWarning("[TrapArrow] Arrow Prefab이 지정되지 않았습니다.");
            return;
        }

        GameObject arrowObj = Instantiate(arrowPrefab, spawnPos, Quaternion.identity);
        Projectile projectile = arrowObj.GetComponent<Projectile>();
        if (projectile != null)
        {
            // 화살의 목적지는 발판(현재 함정의 위치)으로 조준하여 저격 발사
            Vector2 targetPos = (Vector2)transform.position;
            projectile.Init(targetPos, damage, targetLayer, gameObject, arrowSpeed, arrowLifeTime);
        }
        else
        {
            Debug.LogError("[TrapArrow] 생성된 화살 프리팹에 Projectile 컴포넌트가 없습니다.");
        }
    }
}

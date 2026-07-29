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

    [Header("Spawn (벽 안쪽 발사)")]
    [Tooltip("함정에서 벽 방향으로 레이캐스트해 만난 벽 표면에서 방 '안쪽'으로 이만큼 물러난 지점에서 화살을 쏜다. 값이 클수록 벽에서 더 떨어져(더 안쪽에서) 발사.")]
    [SerializeField] private float wallInsetMargin = 0.4f;
    [Tooltip("벽 탐지 레이캐스트가 기하학적 예상 거리보다 얼마나 더 멀리까지 벽을 찾을지의 여유값.")]
    [SerializeField] private float wallRaycastPadding = 3f;

    [Header("References")]
    [SerializeField] private GameObject arrowPrefab; // Projectile 컴포넌트가 붙어 있는 프리팹
    [SerializeField] private Transform firePoint;     // 화살 스폰 기본 위치 (방을 못 찾을 때의 폴백용)
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private Animator plateAnimator; // PlateVisual의 Animator (미지정 시 자식에서 자동 탐색)

    [Header("Sound")]
    [Tooltip("압력판 작동 사운드")]
    [SerializeField] private AudioClip plateSound;
    [Range(0f, 1f)]
    [SerializeField] private float plateVolume = 1f;
    [Tooltip("화살 발사 사운드")]
    [SerializeField] private AudioClip arrowFireSound;
    [Range(0f, 1f)]
    [SerializeField] private float arrowFireVolume = 1f;


    private const string StateOn = "TrapOn";
    private const string StateOff = "TrapOff";

    private bool _isCooldown = false;

    private void Start()
    {
        if (firePoint == null)
        {
            firePoint = transform;
        }


        // PlateVisual의 Animator 자동 탐색 후 Off 상태(마지막 프레임)로 초기화
        if (plateAnimator == null)
        {
            plateAnimator = GetComponentInChildren<Animator>();
        }
        if (plateAnimator != null)
        {
            plateAnimator.Play(StateOff, 0, 1f);
        }
        // 기본적으로 targetLayer가 설정되어 있지 않다면 Player, Enemy, Ally 등을 포함
        if (targetLayer == 0)
        {
            targetLayer = Layers.TrapTargets;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 타겟 레이어 검사
        if (((1 << other.gameObject.layer) & targetLayer) == 0) return;

        // [추가] 방이 클리어되었다면 작동 안함
        RoomInstance room = GetComponentInParent<RoomInstance>();
        if (room != null && room.isCleared) return;

        if (!_isCooldown)
        {
            StartCoroutine(FireRoutine());
        }
    }

    private IEnumerator FireRoutine()
    {
        _isCooldown = true;


        // 발판이 눌리며 함정 작동(On) 애니메이션 재생
        if (plateAnimator != null)
        {
            plateAnimator.Play(StateOn, 0, 0f);
        }

        // 압력판 작동 사운드 재생
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(plateSound, plateVolume);

        }
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
        // 발판 복귀(Off) 애니메이션 재생
        if (plateAnimator != null)
        {
            plateAnimator.Play(StateOff, 0, 0f);
        }

    }

    private Vector2 GetArrowSpawnPosition(RoomInstance room)
    {
        // 함정(압력 발판)은 항상 방 '안쪽'에 있으므로, 여기서 바깥(벽)으로 레이캐스트해
        // 실제 벽면을 찾은 뒤 그 벽 '안쪽'에서 화살을 쏜다. 이렇게 하면 벽 두께/방 원점 오프셋과
        // 무관하게 항상 방 내부에서 발사되어, 화살이 벽에 막혀 사라지는 문제가 사라진다.
        Vector2 trapPos = (Vector2)transform.position;

        // 1) 대략적인 목표 벽 지점을 잡아 '방향'을 정한다(좌/우/상/하 랜덤 + 벽면 따라 랜덤 위치).
        Vector2 aimWallPoint;
        if (room != null)
        {
            // ★ 방의 실제 월드 중심 = transform.position + centerOffset (벽 타일맵 중심). centerOffset를 빼먹으면 벽선이 어긋난다.
            Vector2 roomCenter = (Vector2)room.transform.position + room.centerOffset;
            Vector2 size = (Vector2)room.roomSize;
            float minX = roomCenter.x - size.x * 0.5f;
            float maxX = roomCenter.x + size.x * 0.5f;
            float minY = roomCenter.y - size.y * 0.5f;
            float maxY = roomCenter.y + size.y * 0.5f;

            switch (Random.Range(0, 4)) // 0: 좌, 1: 우, 2: 하, 3: 상
            {
                case 0:  aimWallPoint = new Vector2(minX, Random.Range(minY, maxY)); break;
                case 1:  aimWallPoint = new Vector2(maxX, Random.Range(minY, maxY)); break;
                case 2:  aimWallPoint = new Vector2(Random.Range(minX, maxX), minY); break;
                default: aimWallPoint = new Vector2(Random.Range(minX, maxX), maxY); break;
            }
        }
        else
        {
            // 방이 없는 테스트 씬 등: 함정 주변 랜덤 방향으로 약 12칸 지점을 목표로.
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            aimWallPoint = trapPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 12f;
        }

        // 2) 함정 → 목표 방향으로 실제 벽을 찾아 벽 안쪽 지점을 반환.
        Vector2 dir = aimWallPoint - trapPos;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        dir.Normalize();

        float maxDist = Vector2.Distance(trapPos, aimWallPoint) + wallRaycastPadding;
        RaycastHit2D hit = Physics2D.Raycast(trapPos, dir, maxDist, Layers.WallMask);
        if (hit.collider != null)
        {
            // 레이는 함정(내부)에서 바깥으로 나가므로 hit.point는 벽의 '안쪽 면'.
            // dir(=바깥 방향)만큼 되돌아오면 벽 바로 안쪽(방 내부)에서 발사된다.
            return hit.point - dir * wallInsetMargin;
        }

        // 벽을 못 찾으면(열린 통로 등) 기하학적 벽 지점에서 살짝 안쪽으로 당겨 폴백.
        return aimWallPoint - dir * wallInsetMargin;
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
            // 화살 발사 사운드 재생
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFX(arrowFireSound, arrowFireVolume);
            }

        }
        else
        {
            Debug.LogError("[TrapArrow] 생성된 화살 프리팹에 Projectile 컴포넌트가 없습니다.");
        }
    }
}

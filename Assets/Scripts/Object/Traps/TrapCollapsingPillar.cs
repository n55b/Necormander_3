using UnityEngine;
using System.Collections;
using UnityEngine.AI;

/// <summary>
/// 3번 타격받으면 공격의 반대 방향으로 무너져 내리며 큰 데미지를 주고, NavMeshObstacle을 통해 영구적으로 길을 막는 기둥 함정입니다.
/// </summary>
public class TrapCollapsingPillar : MonoBehaviour, IDamageable
{
    [Header("Settings")]
    [SerializeField] private float hp = 3f; // 3번 타격 시 무너짐
    [SerializeField] private float collapseDelay = 1f;
    [SerializeField] private float collapseDuration = 0.5f;
    [SerializeField] private float collapseDamage = 20f;
    [SerializeField] private float pillarHeight = 3.0f;
    [SerializeField] private float pillarWidth = 0.8f;
    [SerializeField] private LayerMask targetLayer;

    [Header("References")]
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField] private BoxCollider2D physicsCollider;
    [SerializeField] private Transform pillarVisual;         // 무너지는 연출을 할 자식 비주얼 (Root는 회전하지 않음)
    [SerializeField] private GameObject telegraphPrefab;     // TelegraphHitbox Prefab 연결 권장

    private bool _isDead = false;
    private bool _isCollapsing = false;
    private Vector2 _lastAttackDirection = Vector2.right;

    public bool IsDead => _isDead;

    private void Start()
    {
        if (physicsCollider == null)
        {
            physicsCollider = GetComponent<BoxCollider2D>();
        }

        if (pillarVisual == null)
        {
            pillarVisual = transform.Find("Visual");
        }

        // NavMeshObstacle은 쓰러진 후에만 동작하도록 초기화
        if (obstacle != null)
        {
            obstacle.enabled = false;
            obstacle.carving = true;
        }

        if (targetLayer == 0)
        {
            targetLayer = Layers.TrapTargets;
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        if (_isDead || _isCollapsing) return;

        // [추가] 방이 클리어되었다면 피해를 입지 않고 쓰러지지도 않음
        RoomInstance room = GetComponentInParent<RoomInstance>();
        if (room != null && room.isCleared) return;

        // 공격한 캐릭터가 있다면 공격 방향을 캐싱
        if (info.attacker != null)
        {
            // 공격자 -> 기둥 방향
            Vector2 attackDir = ((Vector2)transform.position - (Vector2)info.attacker.transform.position).normalized;
            _lastAttackDirection = new Vector2(attackDir.x > 0 ? 1f : -1f, 0f);
        }

        hp -= 1f; // 공격 횟수 1 차감
        if (hp <= 0)
        {
            _isDead = true;
            StartCoroutine(CollapseRoutine(_lastAttackDirection));
        }
    }

    private IEnumerator CollapseRoutine(Vector2 collapseDir)
    {
        _isCollapsing = true;

        // 넘어질 영역에 시각 장판(Telegraph) 생성
        SpawnTelegraphHitbox(collapseDir);

        // 3회 피격 후 1초 대기 (이 동안 장판 게이지가 서서히 차오름)
        yield return new WaitForSeconds(collapseDelay);

        // 회전 목표 각도 계산 (자식 비주얼만 90도 또는 -90도로 회전)
        float targetAngle = (collapseDir.x > 0) ? -90f : 90f;
        Quaternion startRotation = pillarVisual != null ? pillarVisual.localRotation : Quaternion.identity;
        Quaternion endRotation = Quaternion.Euler(0f, 0f, targetAngle);

        float elapsed = 0f;
        while (elapsed < collapseDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / collapseDuration;
            t = Mathf.SmoothStep(0f, 1f, t); // 부드러운 연출
            
            if (pillarVisual != null)
            {
                pillarVisual.localRotation = Quaternion.Slerp(startRotation, endRotation, t);
            }
            yield return null;
        }
        
        if (pillarVisual != null)
        {
            pillarVisual.localRotation = endRotation;
        }

        // 부모의 회전은 0도로 고정한 채, 콜라이더 및 NavMeshObstacle 크기/센터를 가로 방향으로 변경!
        // 이를 통해 2D NavMesh Carving이 정확하게 가로막을 수 있게 픽스합니다.
        ConfigurePillarObstacle(collapseDir);

        // ponytail: '쓰러진 뒤 레이어를 바꿔 중복 타격을 방지'하려던 자리. 원래 코드는 존재하지 않는
        // 'Obstacle' 레이어(=-1)를 대입해 매번 런타임 에러만 냈고 레이어는 그대로였다 → 의도는 한 번도
        // 동작한 적 없음. 에러만 제거하고 기존 동작을 유지한다. 중복 타격이 실제로 문제가 되면
        // 실재하는 레이어(Wall/Unsteppable 등)를 골라 대입할 것.
    }

    private void SpawnTelegraphHitbox(Vector2 collapseDir)
    {
        if (telegraphPrefab == null) return;

        // center는 기둥 밑동(현재 transform.position)에서 쓰러질 방향으로 높이의 절반만큼 나아간 곳
        Vector2 center = (Vector2)transform.position + collapseDir * (pillarHeight * 0.5f);
        float angle = (collapseDir.x > 0) ? -90f : 90f;
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);

        GameObject telegraphObj = Instantiate(telegraphPrefab, center, rotation);
        
        // 장판의 가로 크기를 기둥의 높이(pillarHeight)로, 세로 크기를 기둥의 너비(pillarWidth)로 크기 조절
        telegraphObj.transform.localScale = new Vector3(pillarHeight, pillarWidth, 1f);

        BaseHitBox hitbox = telegraphObj.GetComponent<BaseHitBox>();
        if (hitbox != null)
        {
            DamageInfo dmgInfo = new DamageInfo(collapseDamage, DamageType.Physical, gameObject, category: DamageCategory.Trap);
            // 선딜레이 1초로 기둥 무너짐 지연(1초)과 매칭하고, 0.2초 동안 타격 판정 유지
            hitbox.Init(dmgInfo, targetLayer, 0.2f, startDelay: 1f, isAlly: false);
        }
    }

    private void ConfigurePillarObstacle(Vector2 collapseDir)
    {
        // 쓰러진 방향에 맞춘 Center와 Size 계산
        float posX = collapseDir.x * (pillarHeight * 0.5f);
        Vector2 newCenter = new Vector2(posX, pillarWidth * 0.5f);
        Vector2 newSize = new Vector2(pillarHeight, pillarWidth);

        // 1. 물리 콜라이더 크기 조절 (가로로 누움)
        if (physicsCollider != null)
        {
            physicsCollider.offset = newCenter;
            physicsCollider.size = newSize;
        }

        // 2. NavMeshObstacle 크기 및 센터 조절 및 활성화 (가로로 깎기)
        if (obstacle != null)
        {
            obstacle.center = new Vector3(newCenter.x, newCenter.y, 0f);
            obstacle.size = new Vector3(newSize.x, newSize.y, 1f);
            obstacle.enabled = true; // 쓰러진 후 활성화
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position + Vector3.up * (pillarHeight * 0.5f), new Vector3(pillarWidth, pillarHeight, 1f));
    }
}

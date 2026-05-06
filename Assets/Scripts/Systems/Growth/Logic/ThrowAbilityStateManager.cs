using UnityEngine;
using System.Collections.Generic;

/* 
 * ==============================================================================
 * [ CRITICAL: THROW ABILITY STATE MANAGER ]
 * ------------------------------------------------------------------------------
 * 이 스크립트는 프로젝트의 핵심 시스템 중 하나이므로 **사용자의 명시적인 지시 없이 삭제하지 마십시오.**
 * 
 * 1. 역할: 
 *    - 플레이어가 보유한 모든 '던지기 능력(ThrowAbilitySO)'들의 실시간 상태(스택, 쿨타임, 콤보 등)를 
 *      통합 관리하는 컨트롤 타워입니다.
 *    - ScriptableObject(데이터)와 MonoBehaviour(실행/상태) 사이의 브릿지 역할을 수행합니다.
 * 
 * 2. 왜 필요한가?
 *    - 능력이 추가될 때마다 플레이어에게 새로운 컴포넌트를 계속 붙이는 '비대화'를 방지하기 위함입니다.
 *    - 투척 A와 투척 B 사이의 연속성(예: 저글링 스택 유지)이 필요한 데이터를 안전하게 보관합니다.
 * 
 * 3. 활용 방법:
 *    - 새로운 누적형 능력을 만들 때: 이 클래스에 변수와 관리 메서드를 추가하십시오.
 *    - 능력 SO(ScriptableObject)에서: `player.GetComponent<ThrowAbilityStateManager>()`를 통해 상태를 읽거나 수정하십시오.
 * ==============================================================================
 */

/// <summary>
/// 플레이어의 던지기 능력들과 관련된 동적 상태(스택, 쿨타임 등)를 총괄 관리하는 매니저입니다.
/// </summary>
public class ThrowAbilityStateManager : MonoBehaviour
{
    [Header("Juggling State")]
    [SerializeField] private int jugglingStacks = 0;
    public int JugglingStacks => jugglingStacks;
    public const int MAX_JUGGLING_STACKS = 4;

    [Header("Juggling Settings")]
    public float jugglingCatchRadius = 0.5f; 
    public float jugglingCatchDuration = 0.8f; 
    public float jugglingDropDistance = 2.0f; // 기존 1.5f에서 2.0f로 약간 확장
    public float jugglingStackExpireTime = 4.0f; // 스택 유지 시간 (4초로 연장)

    private float _jugglingTimer = 0f;

    private void Update()
    {
        // 저글링 스택 타이머 관리
        if (jugglingStacks > 0)
        {
            _jugglingTimer += Time.deltaTime;
            if (_jugglingTimer >= jugglingStackExpireTime)
            {
                ResetJugglingStacks();
            }
        }
    }

    public void AddJugglingStack()
    {
        jugglingStacks = Mathf.Min(jugglingStacks + 1, MAX_JUGGLING_STACKS);
        _jugglingTimer = 0f; // 스택을 쌓을 때마다 타이머 리셋
        Debug.Log($"<color=yellow>[Juggling]</color> Stack Added! Current: {jugglingStacks}");
    }

    public void ResetJugglingStacks()
    {
        if (jugglingStacks > 0)
        {
            jugglingStacks = 0;
            _jugglingTimer = 0f;
            Debug.Log("<color=red>[Juggling]</color> Stacks Reset!");
        }
    }

    /// <summary>
    /// 저글링 낙하지점을 생성하고, 충격 지점에서 해당 지점으로 구체를 튕겨 보냅니다.
    /// </summary>
    public void SpawnJugglingCatchZone(Vector2 impactPos, GameObject zonePrefab, GameObject clusterPrefab)
    {
        // 1. 낙하 지점 계산 (플레이어 근처로 튕겨 나옴)
        Vector2 playerPos = transform.position;
        Vector2 dropPos = playerPos + (Random.insideUnitCircle.normalized * jugglingDropDistance);
        
        // 2. 장판 생성
        GameObject zoneObj = Instantiate(zonePrefab != null ? zonePrefab : new GameObject("JugglingCatchZone"), dropPos, Quaternion.identity);
        if (zonePrefab == null)
        {
            var sr = zoneObj.AddComponent<SpriteRenderer>();
            sr.color = new Color(1f, 0.92f, 0.016f, 0.5f);
            sr.sortingOrder = -1;
        }

        var catchZone = zoneObj.GetComponent<JugglingCatchZone>();
        if (catchZone == null) catchZone = zoneObj.AddComponent<JugglingCatchZone>();
        
        // 3. 튕겨 나가는 구체 생성 (시각적 효과용 Cluster 재활용)
        if (clusterPrefab != null)
        {
            GameObject ballObj = Instantiate(clusterPrefab, impactPos, Quaternion.identity);
            
            // 실제 투척 로직은 제거하고 비주얼과 포물선 이동만 사용
            ThrowCluster cluster = ballObj.GetComponent<ThrowCluster>();
            if (cluster != null)
            {
                cluster.Setup(new List<IThrowable>()); // 빈 클러스터
                cluster.SetVisualRadius(0.3f);
                var sr = cluster.GetVisualRenderer();
                if (sr != null) sr.color = new Color(1f, 1f, 0.5f, 0.8f); // 약간 노란빛

                // 궤적 설정 (충격지점 -> 장판지점)
                float dist = Vector2.Distance(impactPos, dropPos);
                float duration = jugglingCatchDuration;
                float height = 2.0f; // 튕겨 나가는 높이

                // ThrowCluster의 Launch를 그대로 사용하면 OnLanded가 호출되므로 
                // JugglingCatchZone이 이 이벤트를 감시하게 함
                catchZone.Init(this, ballObj, duration, jugglingCatchRadius);
                cluster.Launch(impactPos, dropPos, duration, height, false, 0.5f);
            }
        }
        else
        {
            // 구체 프리팹이 없으면 기존처럼 시간제로 작동
            catchZone.Init(this, null, jugglingCatchDuration, jugglingCatchRadius);
        }
    }
}

/// <summary>
/// 저글링 낙하지점의 실시간 판정을 담당하는 컴포넌트입니다.
/// </summary>
public class JugglingCatchZone : MonoBehaviour
{
    private ThrowAbilityStateManager _stateManager;
    private GameObject _returningBall;
    private float _radius;
    private float _timer;
    private float _targetDuration;
    private SpriteRenderer _sr;
    private bool _isTriggered = false;

    public void Init(ThrowAbilityStateManager manager, GameObject ball, float duration, float radius)
    {
        _stateManager = manager;
        _returningBall = ball;
        _targetDuration = duration;
        _radius = radius;
        _timer = 0f;
        _sr = GetComponentInChildren<SpriteRenderer>();

        transform.localScale = Vector3.one * (radius * 2f);
    }

    private void Update()
    {
        if (_isTriggered) return;

        _timer += Time.deltaTime;
        
        // 비주얼 피드백 (도착 시간이 다가올수록 장판이 선명해짐)
        if (_sr != null)
        {
            float alpha = Mathf.Lerp(0.2f, 0.7f, _timer / _targetDuration);
            Color c = _sr.color;
            c.a = alpha;
            _sr.color = c;
        }

        // 구체가 파괴되었거나(착지했거나) 시간이 다 되면 판정
        if (_returningBall == null || _timer >= _targetDuration + 0.1f)
        {
            CheckCatch();
        }
    }

    private void CheckCatch()
    {
        if (_isTriggered) return;
        _isTriggered = true;

        if (_stateManager == null) 
        {
            Destroy(gameObject);
            return;
        }

        // [개선] 단순 거리 체크 대신 물리 엔진(OverlapCircle)을 사용하여 플레이어 감지
        // 플레이어 레이어를 감지하여 범위 안에 플레이어가 걸쳐만 있어도 성공으로 판정
        int playerMask = LayerMask.GetMask("Player");
        Collider2D playerCollider = Physics2D.OverlapCircle(transform.position, _radius, playerMask);
        
        if (playerCollider != null)
        {
            _stateManager.AddJugglingStack();
        }
        else
        {
            // [수정] 이제 놓쳤다고 해서 즉시 스택을 초기화하지 않습니다.
            // 2초의 글로벌 타이머가 만료될 때만 초기화되도록 변경하여 더 관대한 플레이가 가능합니다.
            Debug.Log("<color=white>[Juggling]</color> Catch Missed. (Global timer still active)");
        }

        Destroy(gameObject);
    }
}

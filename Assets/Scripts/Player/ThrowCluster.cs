using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 2D 환경에서 여러 유닛을 하나로 묶어 던지기 위한 클러스터 오브젝트입니다.
/// 모든 유닛을 대신해 단일 Circle 물리 충돌과 궤적 이동을 처리합니다.
/// </summary>
public class ThrowCluster : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float baseRadius = 0.35f;
    [SerializeField] private float radiusPerUnit = 0.05f;
    [SerializeField] private Transform visualCircle; // [추가] 인스펙터에서 자식 Circle 스프라이트 할당

    private ArcMovement _arcMovement;
    private CircleCollider2D _collider;
    private Rigidbody2D _rb;
    private List<AllyController> _units = new List<AllyController>();
    private bool _isDirectThrow = false;
    private float _chargeRatio = 0f;
    
    private void Awake()
    {
        // 물리 및 이동 컴포넌트 자동 설정
        _arcMovement = gameObject.AddComponent<ArcMovement>();
        _collider = gameObject.AddComponent<CircleCollider2D>();
        _collider.isTrigger = true;
        
        _rb = gameObject.AddComponent<Rigidbody2D>();
        _rb.gravityScale = 0;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _rb.simulated = false;

        // 레이어를 FlyingObject로 설정
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (flyingLayer != -1) gameObject.layer = flyingLayer;

        // [수정] 처음부터 꺼두지 않고, 유닛이 있을 때만 보이도록 설정
        if (visualCircle != null) visualCircle.gameObject.SetActive(false);
    }

    /// <summary>
    /// 던질 유닛들을 클러스터 안으로 모으고 크기를 설정합니다.
    /// </summary>
    public void Setup(List<AllyController> units)
    {
        _units.Clear();
        _units.AddRange(units);

        if (_units.Count == 0)
        {
            if (visualCircle != null) visualCircle.gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        if (visualCircle != null) visualCircle.gameObject.SetActive(true);

        // 유닛 수에 비례하여 원의 크기 결정
        float targetRadius = baseRadius + (_units.Count - 1) * radiusPerUnit;
        _collider.radius = targetRadius;

        // 비주얼 원 크기 동기화 (Sprite의 기본 크기가 1x1일 때)
        if (visualCircle != null)
        {
            visualCircle.localScale = new Vector3(targetRadius * 2f, targetRadius * 2f, 1f);
        }

        // 모든 유닛을 클러스터 자식으로 넣고 중앙으로 정렬
        foreach (var unit in _units)
        {
            unit.transform.SetParent(this.transform);
            // 집어들었을 때의 위치 (중앙 근처)
            unit.transform.localPosition = Random.insideUnitCircle * (_collider.radius * 0.3f);
            unit.OnPickedUp(); 
        }
    }

    /// <summary>
    /// 목표 지점을 향해 클러스터를 발사합니다.
    /// </summary>
    public void Launch(Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float chargeRatio)
    {
        _isDirectThrow = isDirect;
        _chargeRatio = chargeRatio;

        // [추가] 모든 유닛에게 투척 데이터 전달 (효과 발동을 위해 필수)
        foreach (var unit in _units)
        {
            if (unit != null) unit.PrepareForClusterThrow(chargeRatio, isDirect);
        }

        transform.SetParent(null); // 플레이어에게서 분리
        transform.position = startPos;
        _rb.simulated = true;

        Vector2 diff = targetPos - startPos;
        float speed = diff.magnitude / duration;
        _rb.linearVelocity = diff.normalized * speed;

        _arcMovement.StartArc(duration, maxHeight);
    }

    private void Update()
    {
        // 비행 중일 때만 높이 애니메이션 적용
        if (_arcMovement != null && _arcMovement.IsFlying)
        {
            float h = _arcMovement.CurrentHeight;
            foreach (var unit in _units)
            {
                if (unit != null)
                {
                    Vector3 lp = unit.transform.localPosition;
                    lp.y = h + (unit.transform.GetSiblingIndex() * 0.01f);
                    unit.transform.localPosition = lp;
                }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        int wallMask = LayerMask.GetMask("Wall", "Obstacle");
        bool isWall = (wallMask & (1 << other.gameObject.layer)) != 0;

        if (isWall)
        {
            _arcMovement.StopArc();
            return;
        }

        // [추가] 직구(풀차지)일 경우 적이나 오브젝트에 부딪히면 즉시 멈춤
        if (_isDirectThrow)
        {
            int opponentMask = LayerMask.GetMask("Enemy"); 
            int objectMask = LayerMask.GetMask("Object");
            
            bool isTargetHit = ((opponentMask | objectMask) & (1 << other.gameObject.layer)) != 0;
            
            if (isTargetHit)
            {
                _arcMovement.StopArc();
            }
        }
    }

    private bool _isLanded = false;

    private ThrowRecipe _activeRecipe;

    public void SetRecipe(ThrowRecipe recipe)
    {
        _activeRecipe = recipe;
    }

    private void OnLanded()
    {
        if (_isLanded) return;
        _isLanded = true;

        _rb.simulated = false;
        _rb.linearVelocity = Vector2.zero;

        // [구조 개편] 클러스터 통합 효과 처리
        ProcessClusterImpact();

        foreach (var unit in _units)
        {
            if (unit == null) continue;
            unit.transform.SetParent(null);
            unit.OnLanded(); 
        }

        _units.Clear();
        Destroy(gameObject);
    }

    private void ProcessClusterImpact()
    {
        if (_activeRecipe == null) return;

        int totalExecutions = _activeRecipe.GetTotalExecutionCount();
        Debug.Log($"<color=cyan>[ThrowCluster]</color> Executing Effects: {totalExecutions} times (Mode: {_activeRecipe.targetingMode})");

        for (int i = 0; i < totalExecutions; i++)
        {
            switch (_activeRecipe.targetingMode)
            {
                case TargetingMode.Target:
                    ExecuteTargetImpact();
                    break;
                case TargetingMode.Area:
                    ExecuteAreaImpact();
                    break;
                case TargetingMode.Self:
                    ExecuteSelfImpact();
                    break;
            }
        }
    }

    private void ExecuteTargetImpact()
    {
        if (_activeRecipe.finalTarget == null) return;
        GameObject target = _activeRecipe.finalTarget;

        ApplyDamage(target); // 데미지 적용
        if (_activeRecipe.hasCC) ApplyCC(target);
        if (_activeRecipe.hasShield) ApplyShield(target);
        if (_activeRecipe.hasFormation) ApplyFormation(target);
    }

    private void ExecuteAreaImpact()
    {
        float radius = _activeRecipe.GetScaledRadius();
        
        // 1. 적군 대상 효과 (데미지, CC, 진형파괴)
        if (_activeRecipe.impactDamage > 0 || _activeRecipe.hasCC || _activeRecipe.hasFormation)
        {
            Collider2D[] enemies = Physics2D.OverlapCircleAll(transform.position, radius, LayerMask.GetMask("Enemy"));
            foreach (var enemy in enemies)
            {
                ApplyDamage(enemy.gameObject); // 데미지 적용
                if (_activeRecipe.hasCC) ApplyCC(enemy.gameObject);
                if (_activeRecipe.hasFormation) ApplyFormation(enemy.gameObject);
            }

            // [특수] 광역 진형파괴는 플레이어도 밀림
            if (_activeRecipe.hasFormation)
            {
                GameObject player = GameManager.Instance.PLAYERCONTROLLER.gameObject;
                if (Vector2.Distance(transform.position, player.transform.position) <= radius)
                {
                    ApplyFormation(player);
                }
            }
        }

        // 2. 아군 대상 효과 (Shield)
        if (_activeRecipe.hasShield)
        {
            Collider2D[] allies = Physics2D.OverlapCircleAll(transform.position, radius, LayerMask.GetMask("Army"));
            foreach (var ally in allies)
            {
                ApplyShield(ally.gameObject);
            }
        }
    }

    private void ExecuteSelfImpact()
    {
        GameObject player = GameManager.Instance.PLAYERCONTROLLER.gameObject;

        // Self 모드에서도 필요하다면 데미지(자폭 데미지 등)를 줄 수 있지만, 
        // 현재는 아군에게 이로운 효과만 주도록 되어 있으므로 데미지는 제외합니다.
        
        if (_activeRecipe.hasCC)
        {
            ApplyCC(player);
        }
        
        if (_activeRecipe.hasShield)
        {
            ApplyShield(player);
        }

        if (_activeRecipe.hasFormation)
        {
            // 플레이어 돌진 (진형파괴의 Self 버전)
            Debug.Log("Self Formation: 플레이어 돌진!");
            Vector2 moveDir = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerMovement>().MoveInput;
            if (moveDir == Vector2.zero) moveDir = (Vector2)GameManager.Instance.PLAYERCONTROLLER.transform.up;
            
            Rigidbody2D playerRb = player.GetComponent<Rigidbody2D>();
            if (playerRb != null)
            {
                float dashForce = _activeRecipe.GetScaledValue(10f);
                playerRb.AddForce(moveDir * dashForce, ForceMode2D.Impulse);
            }

            // 진형 파괴 시각 효과도 플레이어 위치에 생성
            ApplyFormation(player);
        }
    }

    // --- 개별 효과 적용 헬퍼 메서드들 ---

    private void ApplyDamage(GameObject target)
    {
        if (_activeRecipe.impactDamage <= 0) return;

        if (target.TryGetComponent<BaseEntity>(out var entity))
        {
            // [중요] 타겟이 적군일 때만 데미지 적용
            if (entity.team == Team.Enemy)
            {
                if (entity.TryGetComponent<CharacterStat>(out var stat))
                {
                    DamageInfo info = new DamageInfo(_activeRecipe.impactDamage, DamageType.Physical, gameObject);
                    stat.GetDamage(info);
                    Debug.Log($"Apply Impact Damage to enemy {target.name}: {_activeRecipe.impactDamage}");
                }
            }
            else
            {
                Debug.Log($"Damage skipped for ally {target.name}");
            }
        }
        else if (target.CompareTag("Player") && _activeRecipe.targetTeam == Team.Enemy)
        {
            // 플레이어도 적군 입장에선 공격 대상이지만, 
            // 현재는 투척 레시피가 적군 타겟일 때만 데미지를 주므로 플레이어는 안전함.
        }
    }

    private void ApplyCC(GameObject target)
    {
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        float duration = 3.0f; 

        // 시각 효과 소환
        if (_activeRecipe.targetingMode == TargetingMode.Area)
        {
            if (registry != null && registry.ccAreaPrefab != null)
            {
                GameObject vfx = Instantiate(registry.ccAreaPrefab, transform.position, Quaternion.identity);
                float radius = _activeRecipe.GetScaledRadius();
                vfx.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
                Destroy(vfx, duration);
            }
        }
        else // Target 또는 Self
        {
            if (registry != null && registry.ccAttachVFX != null)
            {
                // 대상에게 부착하여 따라다니게 함
                GameObject vfx = Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                Destroy(vfx, duration);
            }
        }

        // 로직 적용
        if (target.TryGetComponent<CharacterStat>(out var stat))
        {
            float slowAmount = _activeRecipe.GetScaledValue(_activeRecipe.ccBaseValue);
            // CharacterStat의 슬로우 시스템 활용 (지속시간 5초 가정)
            stat.ApplySlow("ThrowCC", slowAmount, 5.0f);
            Debug.Log($"Apply CC to {target.name}: Slow {slowAmount}");
        }
    }

    private void ApplyShield(GameObject target)
    {
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        float duration = 3.0f; 

        // 시각 효과 소환
        if (_activeRecipe.targetingMode == TargetingMode.Area)
        {
            if (registry != null && registry.shieldAreaPrefab != null)
            {
                GameObject vfx = Instantiate(registry.shieldAreaPrefab, transform.position, Quaternion.identity);
                float radius = _activeRecipe.GetScaledRadius();
                vfx.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
                Destroy(vfx, duration);
            }
        }
        else // Target 또는 Self
        {
            if (registry != null && registry.shieldAttachVFX != null)
            {
                // 대상에게 부착하여 따라다니게 함
                GameObject vfx = Instantiate(registry.shieldAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                Destroy(vfx, duration);
            }
        }

        // 로직 적용
        if (target.TryGetComponent<CharacterStat>(out var stat))
        {
            float shieldAmount = _activeRecipe.GetScaledValue(_activeRecipe.shieldBaseValue); 
            // 3초 지속되는 보호막(임시 체력) 추가
            stat.AddShield(shieldAmount, 3.0f);
            Debug.Log($"Apply Shield to {target.name}: {shieldAmount} points for 3s");
        }
    }

    private void ApplyFormation(GameObject target)
    {
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        float duration = 0.5f; 

        // [수정] 진형 파괴는 어떤 모드든 '지점'을 강조하는 장판형 VFX 사용
        if (registry != null && registry.formationAreaVFX != null)
        {
            // Target/Self일 때는 해당 유닛의 위치에, Area일 때는 클러스터 착지 지점에 생성
            Vector3 spawnPos = (_activeRecipe.targetingMode == TargetingMode.Area) ? transform.position : target.transform.position;
            GameObject vfx = Instantiate(registry.formationAreaVFX, spawnPos, Quaternion.identity);
            
            // 크기 조절: Area일 때는 설정된 반경으로, 단일 타겟일 때는 작게(예: 1.0f)
            float radius = (_activeRecipe.targetingMode == TargetingMode.Area) ? _activeRecipe.GetScaledRadius() : 1.0f;
            vfx.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            Destroy(vfx, duration);
        }

        // 로직 적용
        if (target.TryGetComponent<Rigidbody2D>(out var rb))
        {
            float knockbackForce = _activeRecipe.GetScaledValue(_activeRecipe.formationBaseValue);
            Vector2 dir = (target.transform.position - transform.position).normalized;
            if (dir == Vector2.zero) dir = Random.insideUnitCircle.normalized;

            rb.AddForce(dir * knockbackForce, ForceMode2D.Impulse);
            Debug.Log($"Apply Formation to {target.name}: Knockback {knockbackForce}");
        }
    }

    public float GetCurrentRadius() => _collider != null ? _collider.radius : baseRadius;
}

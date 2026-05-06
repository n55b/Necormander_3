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
    private List<IThrowable> _units = new List<IThrowable>();
    private bool _isDirectThrow = false;
    private float _chargeRatio = 0f;
    private Transform _targetTransform;
    private float _launchSpeed;
    private Vector2 _lastTravelDir; // [추가] 넉백 방향 계산을 위한 마지막 비행 방향
    
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
    public void Setup(List<IThrowable> units)
    {
        _units.Clear();
        _units.AddRange(units);

        // [수정] 유닛이 없어도 비주얼 원은 활성화할 수 있도록 변경 (잔상 효과 등)
        gameObject.SetActive(true);
        if (visualCircle != null) visualCircle.gameObject.SetActive(true);

        if (_units.Count == 0) return;

        // 유닛 수에 비례하여 원의 크기 결정
        float targetRadius = baseRadius + (_units.Count - 1) * radiusPerUnit;
        _collider.radius = targetRadius;

        // 비주얼 원 크기 동기화
        if (visualCircle != null)
        {
            visualCircle.localScale = new Vector3(targetRadius * 2f, targetRadius * 2f, 1f);
        }

        // 모든 유닛을 클러스터 자식으로 넣고 중앙으로 정렬
        foreach (var unit in _units)
        {
            if (unit != null)
            {
                unit.transform.SetParent(this.transform);
                unit.transform.localPosition = Random.insideUnitCircle * (_collider.radius * 0.3f);
                unit.OnPickedUp(); 
            }
        }
    }

    /// <summary>
    /// 클러스터의 비주얼 크기를 직접 설정합니다. (실제 유닛이 없는 잔상용)
    /// </summary>
    public void SetVisualRadius(float radius)
    {
        if (visualCircle != null)
        {
            visualCircle.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            visualCircle.gameObject.SetActive(true);
        }
        if (_collider != null) _collider.radius = radius;
    }

    public SpriteRenderer GetVisualRenderer()
    {
        return visualCircle != null ? visualCircle.GetComponent<SpriteRenderer>() : null;
    }

    /// <summary>
    /// 목표 지점(또는 타겟)을 향해 클러스터를 발사합니다.
    /// </summary>
    public void Launch(Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float chargeRatio)
    {
        _isDirectThrow = isDirect;
        _chargeRatio = chargeRatio;

        // [추가] 모든 유닛에게 투척 데이터 전달
        foreach (var unit in _units)
        {
            if (unit != null) unit.PrepareForClusterThrow(chargeRatio, isDirect);
        }

        transform.SetParent(null);
        transform.position = startPos;
        _rb.simulated = true;

        // 레시피로부터 타겟 정보 획득 (직구 던지기가 아닐 때만)
        if (_activeRecipe != null && _activeRecipe.info.targetingMode == TargetingMode.Target && !isDirect)
        {
            _targetTransform = _activeRecipe.info.finalTarget != null ? _activeRecipe.info.finalTarget.transform : null;
        }

        Vector2 diff = targetPos - startPos;
        float dist = diff.magnitude;
        
        // [수정] 거리가 너무 가깝거나 시간이 0이면 발사 속도를 0으로 처리하여 NaN 방지
        _launchSpeed = (duration > 0.001f && dist > 0.001f) ? dist / duration : 0f;
        
        // 타겟이 있고 직구가 아니라면 추적 모드로 발사
        if (_targetTransform != null && !isDirect && _launchSpeed > 0f)
        {
            Vector2 dir = (targetPos - startPos).normalized;
            if (dir != Vector2.zero)
            {
                _rb.linearVelocity = dir * _launchSpeed;
                _arcMovement.StartTrackingArc(_targetTransform, maxHeight);
            }
            else { HandleZeroDistanceLaunch(); }
        }
        else if (_launchSpeed > 0f)
        {
            Vector2 dir = diff.normalized;
            if (dir != Vector2.zero)
            {
                _rb.linearVelocity = dir * _launchSpeed;
                _arcMovement.StartArc(duration, maxHeight);
            }
            else { HandleZeroDistanceLaunch(); }
        }
        else
        {
            HandleZeroDistanceLaunch();
        }
    }

    private void HandleZeroDistanceLaunch()
    {
        // 사실상 제자리 낙하 처리
        _rb.linearVelocity = Vector2.zero;
        OnLanded();
    }

    private void Update()
    {
        if (_arcMovement != null && _arcMovement.IsFlying)
        {
            // [추가] 넉백 방향 계산을 위해 비행 방향 실시간 기록
            if (_rb.linearVelocity.sqrMagnitude > 0.01f)
            {
                _lastTravelDir = _rb.linearVelocity.normalized;
            }

            // [추가] 타겟 추적 중이라면 물리 속도 실시간 보정
            if (_targetTransform != null)
            {
                Vector2 currentPos = transform.position;
                Vector2 targetPos = _targetTransform.position;
                Vector2 diff = targetPos - currentPos;
                if (diff.sqrMagnitude > 0.0001f)
                {
                    Vector2 dir = diff.normalized;
                    Vector2 newVel = dir * _launchSpeed;
                    if (!float.IsNaN(newVel.x) && !float.IsNaN(newVel.y))
                    {
                        _rb.linearVelocity = newVel;
                    }
                }
            }

            // 비행 중 높이 애니메이션 적용
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
        if (_activeRecipe == null) return;

        // [핵심 수정] 포물선 투척일 때는 비행 중 충돌을 완전히 무시합니다.
        // 포물선 투척은 오직 목적지에 도달했을 때(OnLanded)만 효과가 발생해야 합니다.
        if (!_isDirectThrow) return;

        int wallLayer = LayerMask.NameToLayer("Wall");
        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        bool isWall = other.gameObject.layer == wallLayer || other.gameObject.layer == obstacleLayer;

        // 직구/포물선 공통 충돌 로직
        int opponentMask = (_activeRecipe.info.targetTeam == Team.Enemy) ? LayerMask.GetMask("Enemy") : LayerMask.GetMask("Army", "Player");
        int objectMask = LayerMask.GetMask("Object");
        bool isTargetHit = ((opponentMask | objectMask) & (1 << other.gameObject.layer)) != 0;

        if (isWall || isTargetHit)
        {
            // [핀볼 로직 우선 체크] (직구 + 범위 모드 + 튕김 횟수 남음)
            bool isPinballApplicable = _isDirectThrow && 
                                     _activeRecipe.info.targetingMode == TargetingMode.Area && 
                                     InventoryManager.Instance.ActiveAbilities.Exists(a => a is ThrowPinballAbilitySO);

            if (isPinballApplicable)
            {
                ThrowPinballAbilitySO pinballAbility = (ThrowPinballAbilitySO)InventoryManager.Instance.ActiveAbilities.Find(a => a is ThrowPinballAbilitySO);
                if (_activeRecipe.state.bounceCount < pinballAbility.maxBounces)
                {
                    ExecutePinballBounce(other, pinballAbility.bounceDuration);
                    return;
                }
            }

            // 핀볼이 아니거나 횟수를 다 쓴 경우에만 기존 정지 로직 실행
            if (isWall)
            {
                _arcMovement.StopArc();
                return;
            }

            // 중복 타격 방지 (동일 관통 단계에서 같은 놈 두 번 때리기 방지)
            if (_activeRecipe.state.hitTargets.Contains(other.gameObject)) return;

            // 타겟 리스트에 추가 (관통 및 최종 적중 공통)
            _activeRecipe.state.hitTargets.Add(other.gameObject);

            // [핵심] 관통 로직
            if (_activeRecipe.state.pierceCount < _activeRecipe.state.maxPierce)
            {
                _activeRecipe.state.pierceCount++;
                _activeRecipe.info.finalTarget = other.gameObject;
                
                // 비행 중 즉시 효과 발동 (중단하지 않음)
                if (GameManager.Instance.throwImpactManager != null)
                {
                    GameManager.Instance.throwImpactManager.ProcessThrowImpact(_activeRecipe, transform.position, _lastTravelDir, this);
                }
            }
            else
            {
                // 더 이상 관통할 수 없으면 정지
                if (_activeRecipe.info.targetingMode == TargetingMode.Target)
                {
                    _activeRecipe.info.finalTarget = other.gameObject;
                }
                _arcMovement.StopArc();
            }
        }
    }

    private void ExecutePinballBounce(Collider2D other, float duration)
    {
        _activeRecipe.state.bounceCount++;
        
        Vector2 currentPos = transform.position;
        float radius = GetCurrentRadius();
        
        int wallMask = LayerMask.GetMask("Wall", "Obstacle");
        int opponentMask = (_activeRecipe.info.targetTeam == Team.Enemy) ? LayerMask.GetMask("Enemy") : LayerMask.GetMask("Army", "Player");
        int objectMask = LayerMask.GetMask("Object");
        int totalMask = wallMask | opponentMask | objectMask;

        // 1. 정확한 충돌 정보 획득 (CircleCast)
        float castDist = 1.0f; 
        RaycastHit2D hit = Physics2D.CircleCast(currentPos - _lastTravelDir * castDist, radius, _lastTravelDir, castDist * 2f, totalMask);
        
        Vector2 normal = (hit.collider != null) ? hit.normal : -_lastTravelDir;
        Vector2 hitCentroid = (hit.collider != null) ? hit.centroid : currentPos;

        // 2. 위치 보정 (부딪힌 지점에서 법선 방향으로 미세하게 띄움)
        Vector2 safeOrigin = hitCentroid + normal * 0.05f;
        transform.position = safeOrigin;
        Physics2D.SyncTransforms();

        // 3. 지능형 경로 샘플링 (Smart Sampling)
        Vector2 reflectDir = Vector2.Reflect(_lastTravelDir, normal).normalized;
        Vector2 reverseDir = -_lastTravelDir;
        
        // 후보 방향 리스트: 반사 -> 반전 -> 법선 기준 회전각들
        Vector2[] candidates = {
            reflectDir,
            reverseDir,
            (Quaternion.Euler(0, 0, 45) * normal),
            (Quaternion.Euler(0, 0, -45) * normal),
            (Quaternion.Euler(0, 0, 30) * normal),
            (Quaternion.Euler(0, 0, -30) * normal)
        };

        Vector2 finalDir = Vector2.zero;
        float checkDist = 0.6f; // 이 거리만큼 앞길이 비어있어야 함

        foreach (Vector2 cand in candidates)
        {
            if (cand.sqrMagnitude < 0.01f) continue;
            
            // 법선과 반대되는 방향(벽 안쪽)은 아예 배제
            if (Vector2.Dot(cand, normal) < -0.1f) continue;

            // 해당 방향으로 갈 수 있는지 CircleCast로 미리 확인
            if (!Physics2D.CircleCast(safeOrigin, radius * 0.85f, cand.normalized, checkDist, totalMask))
            {
                finalDir = cand.normalized;
                break;
            }
        }

        // 모든 후보가 막혔다면 (극심한 끼임 상태) 최후의 수단으로 법선 방향 선택
        if (finalDir == Vector2.zero) finalDir = normal;

        // 4. 물리 속도 적용
        float currentSpeed = _rb.linearVelocity.magnitude;
        if (currentSpeed < 5f) currentSpeed = 15f; 
        _rb.linearVelocity = finalDir * currentSpeed;
        _lastTravelDir = finalDir;

        // 5. 지속 시간 및 횟수 처리
        ThrowPinballAbilitySO pinballAbility = (ThrowPinballAbilitySO)InventoryManager.Instance.ActiveAbilities.Find(a => a is ThrowPinballAbilitySO);
        if (_activeRecipe.state.bounceCount >= pinballAbility.maxBounces)
        {
            _arcMovement.ResetDuration(0.3f);
            Debug.Log($"<color=yellow>[Pinball]</color> Final Smart Bounce! Dir: {finalDir}");
        }
        else
        {
            _arcMovement.ResetDuration(duration);
            Debug.Log($"<color=cyan>[Pinball]</color> Smart Bounced! Count: {_activeRecipe.state.bounceCount}/{pinballAbility.maxBounces}. Dir: {finalDir}");
        }

        // 효과 발동
        if (GameManager.Instance.throwImpactManager != null)
        {
            GameManager.Instance.throwImpactManager.ProcessThrowImpact(_activeRecipe, transform.position, finalDir, this);
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

        bool isImpactSuccess = false;

        if (_activeRecipe != null)
        {
            int wallMask = LayerMask.GetMask("Wall", "Obstacle");
            // [수정] 핀볼은 이미 충돌 시 효과를 냈으므로 마지막 착지 시 중복 방지 체크 필요할 수도 있음
            // 하지만 요구사항상 "5초가 지나면 멈춘다"이므로 마지막 멈춤 효과도 발동
            bool hitWall = Physics2D.OverlapCircle(transform.position, GetCurrentRadius() * 0.8f, wallMask);

            if (!hitWall)
            {
                if (_activeRecipe.info.targetingMode == TargetingMode.Self) isImpactSuccess = true;
                else if (_activeRecipe.info.targetingMode == TargetingMode.Area) isImpactSuccess = true;
                else if (_activeRecipe.info.targetingMode == TargetingMode.Target && _activeRecipe.info.finalTarget != null) isImpactSuccess = true;
                else if (_activeRecipe.state.maxPierce > 0) isImpactSuccess = true;
                // [추가] 핀볼로 튕기고 날아가다 멈춘 경우
                else if (_activeRecipe.state.bounceCount > 0) isImpactSuccess = true;
            }
        }

        // 효과 실행
        if (isImpactSuccess && _activeRecipe != null && !_activeRecipe.info.isImmediateApplied)
        {
            GameManager.Instance.throwImpactManager.ProcessThrowImpact(_activeRecipe, transform.position, _lastTravelDir, this);
        }

        // [핵심] 튕기기 예외 처리 (Bouncing 능력용)
        if (_activeRecipe != null && _activeRecipe.state.isBouncing)
        {
            _isLanded = false; 
            _activeRecipe.state.isBouncing = false;
            return;
        }

        // [핵심] 마스터 클러스터만 유닛 생명주기 관리
        if (_activeRecipe != null && _activeRecipe.state.isMaster)
        {
            foreach (var unit in _units)
            {
                if (unit == null) continue;
                unit.SetImpacted(isImpactSuccess); 
                unit.transform.SetParent(null);
                unit.OnLanded();
            }
        }

        _units.Clear();
        Destroy(gameObject);
    }

    public float GetCurrentRadius() => _collider != null ? _collider.radius : baseRadius;
}

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
    [SerializeField] private Transform visualCircle;

    private ArcMovement _arcMovement;
    private CircleCollider2D _collider;
    private Rigidbody2D _rb;
    private List<IThrowable> _units = new List<IThrowable>();
    private bool _isDirectThrow = false;
    private float _chargeRatio = 0f;
    private Transform _targetTransform;
    private float _launchSpeed;
    private Vector2 _lastTravelDir;
    
    private void Awake()
    {
        _arcMovement = gameObject.AddComponent<ArcMovement>();
        _collider = gameObject.AddComponent<CircleCollider2D>();
        _collider.isTrigger = true;
        
        _rb = gameObject.AddComponent<Rigidbody2D>();
        _rb.gravityScale = 0;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _rb.simulated = false;

        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (flyingLayer != -1) gameObject.layer = flyingLayer;

        if (visualCircle != null) visualCircle.gameObject.SetActive(false);
    }

    public void Setup(List<IThrowable> units)
    {
        _units.Clear();
        _units.AddRange(units);

        gameObject.SetActive(true);
        if (visualCircle != null) visualCircle.gameObject.SetActive(true);

        if (_units.Count == 0) return;

        float targetRadius = baseRadius + (_units.Count - 1) * radiusPerUnit;
        _collider.radius = targetRadius;

        if (visualCircle != null)
        {
            visualCircle.localScale = new Vector3(targetRadius * 2f, targetRadius * 2f, 1f);
        }

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

    public void Launch(Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float chargeRatio)
    {
        _isDirectThrow = isDirect;
        _chargeRatio = chargeRatio;

        // [수정] 발사 시 비주얼 확실히 활성화
        if (visualCircle != null) visualCircle.gameObject.SetActive(true);

        foreach (var unit in _units)
        {
            if (unit != null) unit.PrepareForClusterThrow(chargeRatio, isDirect);
        }

        transform.SetParent(null);
        transform.position = startPos;
        _rb.simulated = true;

        if (_activeRecipe != null && _activeRecipe.info.targetingMode == TargetingMode.Target && !isDirect)
        {
            _targetTransform = _activeRecipe.info.finalTarget != null ? _activeRecipe.info.finalTarget.transform : null;
        }

        Vector2 diff = targetPos - startPos;
        float dist = diff.magnitude;
        _launchSpeed = (duration > 0.001f && dist > 0.001f) ? dist / duration : 0f;
        
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
        _rb.linearVelocity = Vector2.zero;
        OnLanded();
    }

    private bool _isLanded = false;
    private ThrowRecipe _activeRecipe;

    public void SetRecipe(ThrowRecipe recipe)
    {
        _activeRecipe = recipe;
    }

    private void Update()
    {
        if (_arcMovement != null && _arcMovement.IsFlying)
        {
            if (_rb.linearVelocity.sqrMagnitude > 0.01f)
            {
                _lastTravelDir = _rb.linearVelocity.normalized;
            }

            // 타겟 추적 중이었는데 타겟이 사라진 경우 처리
            if (_activeRecipe != null && _activeRecipe.info.targetingMode == TargetingMode.Target && !_isDirectThrow)
            {
                if (_targetTransform == null)
                {
                    Vector2 currentPos = transform.position;
                    Vector2 targetPoint = _activeRecipe.info.impactPoint;
                    Vector2 diff = targetPoint - currentPos;
                    float dist = diff.magnitude;
                    
                    if (dist < 0.2f) 
                    {
                        OnLanded();
                        return;
                    }
                    
                    if (diff.sqrMagnitude > 0.0001f)
                    {
                        _rb.linearVelocity = diff.normalized * _launchSpeed;
                    }
                }
            }

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

            float h = _arcMovement.CurrentHeight;
            foreach (var unit in _units)
            {
                if (unit != null && (unit is MonoBehaviour mb && mb != null))
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
        if (!_isDirectThrow) return;

        int wallLayer = LayerMask.NameToLayer("Wall");
        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        bool isWall = other.gameObject.layer == wallLayer || other.gameObject.layer == obstacleLayer;

        int opponentMask = (_activeRecipe.info.targetTeam == Team.Enemy) ? LayerMask.GetMask("Enemy") : LayerMask.GetMask("Army", "Player");
        int objectMask = LayerMask.GetMask("Object");
        bool isTargetHit = ((opponentMask | objectMask) & (1 << other.gameObject.layer)) != 0;

        if (isWall || isTargetHit)
        {
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

            if (isWall) { _arcMovement.StopArc(); return; }
            if (_activeRecipe.state.hitTargets.Contains(other.gameObject)) return;
            _activeRecipe.state.hitTargets.Add(other.gameObject);

            if (_activeRecipe.state.pierceCount < _activeRecipe.state.maxPierce)
            {
                _activeRecipe.state.pierceCount++;
                _activeRecipe.info.finalTarget = other.gameObject;
                if (GameManager.Instance.throwImpactManager != null)
                {
                    GameManager.Instance.throwImpactManager.ProcessThrowImpact(_activeRecipe, transform.position, _lastTravelDir, this);
                }
            }
            else
            {
                if (_activeRecipe.info.targetingMode == TargetingMode.Target) _activeRecipe.info.finalTarget = other.gameObject;
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

        float castDist = 1.0f; 
        RaycastHit2D hit = Physics2D.CircleCast(currentPos - _lastTravelDir * castDist, radius, _lastTravelDir, castDist * 2f, totalMask);
        Vector2 normal = (hit.collider != null) ? hit.normal : -_lastTravelDir;
        Vector2 hitCentroid = (hit.collider != null) ? hit.centroid : currentPos;

        Vector2 safeOrigin = hitCentroid + normal * 0.05f;
        transform.position = safeOrigin;
        Physics2D.SyncTransforms();

        Vector2 reflectDir = Vector2.Reflect(_lastTravelDir, normal).normalized;
        Vector2 reverseDir = -_lastTravelDir;
        Vector2[] candidates = { reflectDir, reverseDir, (Quaternion.Euler(0, 0, 45) * normal), (Quaternion.Euler(0, 0, -45) * normal) };

        Vector2 finalDir = Vector2.zero;
        foreach (Vector2 cand in candidates)
        {
            if (Vector2.Dot(cand, normal) < -0.1f) continue;
            if (!Physics2D.CircleCast(safeOrigin, radius * 0.85f, cand.normalized, 0.6f, totalMask)) { finalDir = cand.normalized; break; }
        }

        if (finalDir == Vector2.zero) finalDir = normal;

        float currentSpeed = _rb.linearVelocity.magnitude;
        if (currentSpeed < 5f) currentSpeed = 15f; 
        _rb.linearVelocity = finalDir * currentSpeed;
        _lastTravelDir = finalDir;

        ThrowPinballAbilitySO pinballAbility = (ThrowPinballAbilitySO)InventoryManager.Instance.ActiveAbilities.Find(a => a is ThrowPinballAbilitySO);
        if (_activeRecipe.state.bounceCount >= pinballAbility.maxBounces) _arcMovement.ResetDuration(0.3f);
        else _arcMovement.ResetDuration(duration);

        if (GameManager.Instance.throwImpactManager != null) GameManager.Instance.throwImpactManager.ProcessThrowImpact(_activeRecipe, transform.position, finalDir, this);
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
            if (!Physics2D.OverlapCircle(transform.position, GetCurrentRadius() * 0.8f, wallMask))
            {
                if (_activeRecipe.info.targetingMode == TargetingMode.Self || _activeRecipe.info.targetingMode == TargetingMode.Area || (_activeRecipe.info.targetingMode == TargetingMode.Target && _activeRecipe.info.finalTarget != null) || _activeRecipe.state.maxPierce > 0 || _activeRecipe.state.bounceCount > 0)
                    isImpactSuccess = true;
            }
            _activeRecipe.info.isMissed = !isImpactSuccess;
        }

        if (_activeRecipe != null && !_activeRecipe.info.isImmediateApplied)
        {
            GameManager.Instance.throwImpactManager.ProcessThrowImpact(_activeRecipe, transform.position, _lastTravelDir, this);
        }

        if (_activeRecipe != null && _activeRecipe.state.isBouncing) { _isLanded = false; _activeRecipe.state.isBouncing = false; return; }

        // [수정] 튕기기가 아님을 확인한 뒤에 비주얼을 비활성화
        if (visualCircle != null) visualCircle.gameObject.SetActive(false);

        if (_activeRecipe != null && _activeRecipe.state.isMaster)
        {
            foreach (var unit in _units)
            {
                if (unit == null || (unit is MonoBehaviour mb && mb == null)) continue;

                // [추가] 부모 해제 전/후에 투척 비용(체력 소모) 적용
                unit.transform.SetParent(null);
                unit.ApplyThrowCost();

                // [체크] 체력 소모 후 아직 살아있는 경우에만 상태 복구(OnLanded) 호출
                if (unit != null && (unit is MonoBehaviour aliveMb && aliveMb != null))
                {
                    unit.SetImpacted(isImpactSuccess); 
                    unit.OnLanded();
                }
            }
        }
        _units.Clear();
        Destroy(gameObject);
    }

    public float GetCurrentRadius() => _collider != null ? _collider.radius : baseRadius;
}

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
        if (_activeRecipe != null && _activeRecipe.targetingMode == TargetingMode.Target && !isDirect)
        {
            _targetTransform = _activeRecipe.finalTarget != null ? _activeRecipe.finalTarget.transform : null;
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
                // [수정] 직구 충돌 시, 'Target' 모드일 때만 충돌 대상을 타겟으로 등록
                if (_activeRecipe != null)
                {
                    if (_activeRecipe.targetingMode == TargetingMode.Target)
                    {
                        _activeRecipe.finalTarget = other.gameObject;
                    }
                }
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

        // [리팩토링] 효과 발동 성공 여부 판단
        bool isImpactSuccess = false;

        if (_activeRecipe != null)
        {
            // 1. Self 모드: 던지는 즉시 발동되었으므로 무조건 성공
            if (_activeRecipe.targetingMode == TargetingMode.Self)
            {
                isImpactSuccess = true;
            }
            // 2. Area 모드: 지면에 닿으면 무조건 발동 (벽 충돌 제외는 아래 logic에서 처리)
            else if (_activeRecipe.targetingMode == TargetingMode.Area)
            {
                isImpactSuccess = true;
            }
            // 3. Target 모드: 최종 타겟이 지정되어 있어야 성공
            else if (_activeRecipe.targetingMode == TargetingMode.Target && _activeRecipe.finalTarget != null)
            {
                isImpactSuccess = true;
            }
        }

        // [추가] 만약 벽에 부딪혀서 멈춘 것이라면 무조건 실패 처리 (벽 레이어 체크)
        // (StopArc가 호출되어 여기 왔을 때, 주변에 벽이 있는지 확인)
        int wallMask = LayerMask.GetMask("Wall", "Obstacle");
        if (Physics2D.OverlapCircle(transform.position, GetCurrentRadius(), wallMask))
        {
            isImpactSuccess = false;
            Debug.Log("<color=orange>[ThrowCluster]</color> Hit Wall! Impact Failed.");
        }

        // 효과 처리를 DataManager에 위임 (성공했을 때만, 그리고 즉시발동이 아닐 때만)
        if (isImpactSuccess && _activeRecipe != null && !_activeRecipe.isImmediateApplied)
        {
            GameManager.Instance.dataManager.ProcessThrowImpact(_activeRecipe, transform.position, _lastTravelDir);
        }

        // 유닛들에게 결과 알림 및 지상 복구
        foreach (var unit in _units)
        {
            if (unit == null) continue;
            unit.SetImpacted(isImpactSuccess); // 여기서 결정된 성공 여부를 전달
            unit.transform.SetParent(null);
            unit.OnLanded();
        }

        _units.Clear();
        Destroy(gameObject);
    }

    public float GetCurrentRadius() => _collider != null ? _collider.radius : baseRadius;
}

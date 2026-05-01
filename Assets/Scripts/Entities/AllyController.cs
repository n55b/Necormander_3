using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 아군 유닛 전용 컨트롤러입니다. 
/// AIPatternSO(브레인)와 협력하여 유닛의 행동과 던지기 로직을 처리합니다.
/// </summary>
public class AllyController : BaseEntity, IThrowable
{
    [Header("Ally 전용 설정")]
    public Transform player;
    [SerializeField] bool isBattle = false;

    public CommandData MinionType => minionData != null ? minionData.minionType : CommandData.SkeletonWarrior;

    [Header("Throw Physics")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float straightHeight = 0.1f;
    [SerializeField] private float minSpeed = 5f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private float fullChargeSpeed = 30f;

    public float JumpHeight => jumpHeight;
    public float StraightHeight => straightHeight;
    public float MinSpeed => minSpeed;
    public float MaxSpeed => maxSpeed;
    public float FullChargeSpeed => fullChargeSpeed;

    [Header("References")]
    private ArcMovement _arcMovement;

    private float _throwStartTime;
    private float _originalDamping;
    private float _lastChargeRatio; 
    private int _originalLayer;
    private string _originalSortingLayerName;
    private bool _hasImpacted = false;
    private bool _isDirectThrow = false;

    protected override void Awake()
    {
        base.Awake();
        _arcMovement = GetComponent<ArcMovement>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (_rb != null) _originalDamping = _rb.linearDamping;
        if (_sr != null) _originalSortingLayerName = _sr.sortingLayerName;
        
        // [수정] 원래 레이어를 미리 안전하게 저장
        _originalLayer = gameObject.layer;
        team = Team.Ally;
    }

    public override void Initialize(MinionDataSO data)
    {
        base.Initialize(data);
    }

    protected override bool CanExecuteAI()
    {
        // 비행 중이거나 던져진 상태일 때는 AI 차단
        if ((_arcMovement != null && _arcMovement.IsFlying) || (_runtimeBrain != null && _runtimeBrain.CurrentState == AIState.Thrown)) 
            return false;
        
        return base.CanExecuteAI();
    }

    protected override void HandleNoTarget()
    {
        // 이제 브레인이 스스로 판단하므로, 브레인 외부에서의 강제 개입은 최소화합니다.
    }

    #region IThrowable 구현

    public void OnPickedUp()
    {
        // [중요] 구형 FSM 대신 브레인 상태를 Thrown으로 변경
        if (_runtimeBrain != null) _runtimeBrain.SetState(AIState.Thrown);

        // [추가] 다음 투척을 위해 충돌 여부 리셋
        _hasImpacted = false;

        // [수정] 피격 연출 코루틴이 실행 중일 경우를 대비해 강제 리셋
        if (_stats != null) _stats.ResetVisualFeedback();

        if (_rb != null) _rb.simulated = false;
        if (_collider != null) _collider.enabled = false;
        if (_agent != null) _agent.enabled = false;
    }

    /// <summary>
    /// [추가] 클러스터에 담겨 던져질 때 필요한 데이터를 설정합니다.
    /// </summary>
    public void PrepareForClusterThrow(float chargeRatio, bool isDirect)
    {
        _lastChargeRatio = chargeRatio;
        _isDirectThrow = isDirect;
        _hasImpacted = false;
        
        // 시각적 레이어 설정
        if (_sr != null) _sr.sortingLayerName = "FlyingObject";
    }

    public void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _throwStartTime = Time.time;
        _lastChargeRatio = chargeRatio;
        _hasImpacted = false;
        _isDirectThrow = (chargeRatio >= 1.0f); 

        // _originalLayer는 이미 Awake에서 저장됨

        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (flyingLayer != -1) gameObject.layer = flyingLayer;

        if (_sr != null) _sr.sortingLayerName = "FlyingObject";

        if (_rb != null)
        {
            _rb.simulated = true;
            _rb.linearDamping = 0f;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        if (_collider != null)
        {
            _collider.enabled = true;
            _collider.isTrigger = true; 
        }

        if (_agent != null) _agent.enabled = false;

        Vector2 startPos = (Vector2)transform.position;
        Vector2 diff = targetPosition - startPos;
        float distance = diff.magnitude;
        Vector2 direction = diff.normalized;

        ThrowParams p = new ThrowParams();
        if (_isDirectThrow)
        {
            p.speed = fullChargeSpeed;
            p.duration = 1.5f; 
            p.maxHeight = straightHeight;
        }
        else
        {
            p.speed = Mathf.Lerp(minSpeed, maxSpeed, chargeRatio);
            p.duration = distance / p.speed;
            float targetHeight = Mathf.Lerp(jumpHeight, straightHeight, chargeRatio);
            p.maxHeight = Mathf.Min(targetHeight, distance * 0.5f);
        }

        if (_rb != null) _rb.linearVelocity = direction * p.speed;
        if (_arcMovement != null) _arcMovement.StartArc(p.duration, p.maxHeight);
    }

    public void SetImpacted(bool value)
    {
        _hasImpacted = value;
    }

    public virtual void OnLanded()
    {
        // [수정] 투척 성공 시 리스크(체력 차감) 로직 제거
        /*
        if (_hasImpacted && _stats != null)
        {
            float fixedDamage = _stats.MAXHP / 3f;
            DamageInfo riskInfo = new DamageInfo(fixedDamage, DamageType.Fixed, gameObject);
            _stats.GetDamage(riskInfo);
            Debug.Log($"<color=red>[Risk]</color> {gameObject.name} 투척 성공으로 인한 체력 차감: {fixedDamage:F1}");
        }
        */

        gameObject.layer = _originalLayer;
        if (_sr != null && !string.IsNullOrEmpty(_originalSortingLayerName))
            _sr.sortingLayerName = _originalSortingLayerName;

        if (_agent != null)
        {
            _agent.enabled = true;
            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit hit, 1.0f, UnityEngine.AI.NavMesh.AllAreas))
                _agent.Warp(hit.position);
        }

        if (_rb != null)
        {
            _rb.simulated = true; 
            _rb.linearVelocity = Vector2.zero;
            _rb.linearDamping = _originalDamping;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        }

        if (_collider != null)
        {
            _collider.enabled = true; 
            _collider.isTrigger = false;
        }

        if (_runtimeBrain != null) _runtimeBrain.Init(this);
    }

    #endregion

    public void SetBattleState(bool _set)
    {
        isBattle = _set;
    }
}

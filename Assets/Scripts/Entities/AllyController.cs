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

    protected override void SetupLayers()
    {
        base.SetupLayers();
        if (_nearestFinder != null) _nearestFinder.targetLayer = opponentLayer;
    }

    protected override bool CanExecuteAI()
    {
        // 비행 중이거나 던져진 상태일 때는 AI 차단
        if ((_arcMovement != null && _arcMovement.IsFlying) || (_runtimeBrain != null && _runtimeBrain.CurrentState == AIState.Caught)
        || _runtimeBrain != null && _runtimeBrain.CurrentState == AIState.Thrown) 
            return false;
        
        return base.CanExecuteAI();
    }

    protected override void HandleNoTarget()
    {
    }

    #region IThrowable 구현

    public void OnPickedUp()
    {
        // 1. 브레인 상태를 즉시 Caught 변경 (타겟팅 제외 핵심)
        if (_runtimeBrain != null) _runtimeBrain.SetState(this, AIState.Caught);
        _hasImpacted = false;

        if (_stats != null)
        {
            _stats.Visual.ResetVisuals();
            // [수정] 사용자 요청에 따라 인스턴트 무적 로직 제거 (레이어로 처리)
            // if (_stats.Health != null) _stats.Health.Invincible = true;
        }

        // 2. 모든 자식 포함 레이어 변경 및 콜라이더 비활성화 (투사체 통과용)
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (flyingLayer != -1) SetLayerRecursive(gameObject, flyingLayer);
        else Debug.LogError("[AllyController] FlyingObject layer not found!");

        if (_rb != null) _rb.simulated = false;
        
        foreach (var col in GetComponentsInChildren<Collider2D>())
        {
            col.enabled = false;
        }

        if (_agent != null) _agent.enabled = false;
    }

    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }

    public void PrepareForClusterThrow(float chargeRatio, bool isDirect)
    {
        _lastChargeRatio = chargeRatio;
        _isDirectThrow = isDirect;
        _hasImpacted = false;
        
        if (_sr != null) _sr.sortingLayerName = "FlyingObject";
    }

    public void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _throwStartTime = Time.time;
        _lastChargeRatio = chargeRatio;
        _hasImpacted = false;
        _isDirectThrow = (chargeRatio >= 1.0f);
        
        // 던져질 때 Thrown 상태로 변경 (애니메이션 재생)
        if (_runtimeBrain != null) 
        {
            _runtimeBrain.SetState(this, AIState.Thrown);

            if(targetPosition.x - this.transform.position.x > 0.0f)
            {
                _sr.flipX = true;
            }
            else if (targetPosition.x - this.transform.position.x < 0.0f)
            {
                _sr.flipX = false;
            }
        }

        if (_sr != null) _sr.sortingLayerName = "FlyingObject";

        // [수정] 개별 물리 활성화 제거
        // 부모인 ThrowCluster가 위치와 궤적을 제어하므로, 자식 유닛은 물리 시뮬레이션을 끕니다.
        // 이 Rigidbody가 켜져 있으면 부모-자식 간의 물리 충돌이나 보정 때문에 위치가 어긋나게 됩니다.
        if (_rb != null)
        {
            _rb.simulated = false; 
        }

        if (_collider != null)
        {
            _collider.enabled = false; // 충돌 체크도 클러스터가 전담
        }

        if (_agent != null) _agent.enabled = false;

        // 기존의 개별 속도 할당 및 ArcMovement 시작 로직을 제거합니다.
    }

    public void SetImpacted(bool value)
    {
        _hasImpacted = value;
    }

    public void ApplyThrowCost()
    {
        if (minionData == null || minionData.hpCostRatioPerThrow <= 0) return;
        float damageAmount = _stats.MAXHP * minionData.hpCostRatioPerThrow;
        _stats.Health.GetDamage(new DamageInfo(damageAmount, DamageType.Fixed, null));
    }

    public virtual void OnLanded()
    {
        // 1. 레이어 및 상태 복구
        SetLayerRecursive(gameObject, _originalLayer);

        if (_sr != null && !string.IsNullOrEmpty(_originalSortingLayerName))
            _sr.sortingLayerName = _originalSortingLayerName;

        if (_stats != null && _stats.Health != null) _stats.Health.Invincible = false;

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

        foreach (var col in GetComponentsInChildren<Collider2D>())
        {
            col.enabled = true;
        }

        if (_runtimeBrain != null) _runtimeBrain.Init(this);
    }

    #endregion

    public void SetBattleState(bool _set)
    {
        isBattle = _set;
    }
}

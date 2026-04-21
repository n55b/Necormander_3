using UnityEngine;
using Necromancer.Interfaces;
using Necromancer.Physics;
using System.Collections.Generic;
using Necromancer.Player;

/// <summary>
/// 아군 유닛 전용 컨트롤러입니다. 
/// BaseEntity를 상속받으며, 던지기(IThrowable)와 조합 시너지 기능을 포함합니다.
/// </summary>
public class AllyController : BaseEntity, IThrowable
{
    [Header("Ally 전용 설정")]
    public Transform player;
    [SerializeField] bool isBattle = false;

    public MinionDataSO MinionData => minionData;
    public CommandData MinionType => minionData != null ? minionData.minionType : CommandData.SkeletonWarrior;

    [Header("Throw States & Physics")]
    public FSMStateSO thrownState;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float straightHeight = 0.1f;
    [SerializeField] private float minSpeed = 5f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private float fullChargeSpeed = 30f;

    [Header("References")]
    private ArcMovement _arcMovement;
    private BaseThrowImpactSO impactEffect; 

    // 투척 물리 변수
    private float _throwStartTime;
    private float _originalDamping;
    private float _lastChargeRatio; 
    private int _originalLayer;
    private string _originalSortingLayerName;
    private bool _hasImpacted = false;
    private bool _isDirectThrow = false;

    // 조합 시너지 데이터
    private ThrowCombinationSO _activeCombination;
    private bool _isCombinationLead = false;
    private List<AllyController> _combinationSupporters;

    protected override void Awake()
    {
        base.Awake();
        _arcMovement = GetComponent<ArcMovement>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (_rb != null) _originalDamping = _rb.linearDamping;
        
        team = Team.Ally;
    }

    public override void Initialize(MinionDataSO data)
    {
        base.Initialize(data); // 부모의 공통 초기화 호출

        if (minionData != null)
        {
            if (minionData.throwImpact != null) impactEffect = minionData.throwImpact;
        }
    }

    protected override bool CanExecuteAI()
    {
        // 비행 중이거나 thrownState일 때는 AI 차단
        if ((_arcMovement != null && _arcMovement.IsFlying) || _fsm.currentState == thrownState) 
            return false;
        
        return base.CanExecuteAI();
    }

    protected override void HandleNoTarget()
    {
        // 아군은 타겟이 없으면 플레이어를 따라갑니다.
        _fsm.target = player;
        _fsm.ChangeState(followState);
    }

    #region IThrowable 구현 (아군 전용)

    public void OnPickedUp()
    {
        _fsm.ChangeState(thrownState);
        if (_rb != null) _rb.simulated = false;
        if (_collider != null) _collider.enabled = false;
        if (_agent != null) _agent.enabled = false;
    }

    public void SetCombination(ThrowCombinationSO combo, bool isLead, List<AllyController> supporters)
    {
        _activeCombination = combo;
        _isCombinationLead = isLead;
        _combinationSupporters = supporters;
    }

    public void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _throwStartTime = Time.time;
        _lastChargeRatio = chargeRatio;
        _hasImpacted = false;
        _isDirectThrow = (chargeRatio >= 1.0f);

        _originalLayer = gameObject.layer;
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (flyingLayer != -1) gameObject.layer = flyingLayer;

        if (_sr != null)
        {
            _originalSortingLayerName = _sr.sortingLayerName;
            _sr.sortingLayerName = "FlyingObject";
        }

        if (_rb != null)
        {
            _rb.simulated = true;
            _rb.linearDamping = 0f;
        }

        if (_collider != null)
        {
            _collider.enabled = true;
            _collider.isTrigger = true;
        }

        if (_agent != null) _agent.enabled = false;

        if (!_isDirectThrow) targetPosition += Random.insideUnitCircle * 0.8f;

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

        if (impactEffect != null) impactEffect.OnPreThrow(p, chargeRatio);

        if (_rb != null) _rb.linearVelocity = direction * p.speed;
        if (_arcMovement != null) _arcMovement.StartArc(p.duration, p.maxHeight);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Time.time - _throwStartTime < 0.05f) return;
        if (_hasImpacted) return;

        if (_isDirectThrow)
        {
            // opponentLayer에 속한 적이나 벽에 부딪히면 임팩트 실행
            if (_arcMovement != null && _arcMovement.IsFlying && ((opponentLayer.value | LayerMask.GetMask("Wall", "Obstacle")) & (1 << other.gameObject.layer)) != 0)
            {
                TriggerImpact(other.gameObject);
                _arcMovement.StopArc();
            }
        }
    }

    private void TriggerImpact(GameObject targetObj)
    {
        if (_hasImpacted) return;
        _hasImpacted = true;

        if (_activeCombination != null)
        {
            if (_isCombinationLead && _activeCombination.combinationEffect != null)
            {
                CombinationContext context = new CombinationContext
                {
                    leadAttacker = this.gameObject,
                    impactPosition = transform.position,
                    chargeRatio = _lastChargeRatio,
                    supporters = _combinationSupporters
                };
                _activeCombination.combinationEffect.Execute(context);
            }
            return;
        }

        if (impactEffect != null)
        {
            ImpactContext context = new ImpactContext
            {
                attacker = this.gameObject,
                target = targetObj,
                impactPosition = transform.position,
                chargeRatio = _lastChargeRatio
            };
            impactEffect.Apply(context);
        }
    }

    public virtual void OnLanded()
    {
        if (!_hasImpacted) TriggerImpact(null);

        gameObject.layer = _originalLayer;
        if (_sr != null && !string.IsNullOrEmpty(_originalSortingLayerName))
            _sr.sortingLayerName = _originalSortingLayerName;

        if (_agent != null) _agent.enabled = true;

        _activeCombination = null;
        _isCombinationLead = false;
        _combinationSupporters = null;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.linearDamping = _originalDamping;
        }
        if (_collider != null) _collider.isTrigger = false;

        // 착지 후 타겟 재탐색 로직은 BaseEntity의 HandleAIUpdate에 의해 수행됨
    }

    #endregion

    public void SetBattleState(bool _set)
    {
        isBattle = _set;
    }
}

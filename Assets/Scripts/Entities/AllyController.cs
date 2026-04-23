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

    // TrajectoryPredictor용 프로퍼티
    public float JumpHeight => jumpHeight;
    public float StraightHeight => straightHeight;
    public float MinSpeed => minSpeed;
    public float MaxSpeed => maxSpeed;
    public float FullChargeSpeed => fullChargeSpeed;

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
        // --- 1. 투척 상태 데이터 초기화 ---
        _throwStartTime = Time.time;
        _lastChargeRatio = chargeRatio;
        _hasImpacted = false;
        _isDirectThrow = (chargeRatio >= 1.0f); 

        // --- 2. 레이어 및 렌더링 설정 ---
        _originalLayer = gameObject.layer;
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (flyingLayer != -1) gameObject.layer = flyingLayer;

        if (_sr != null)
        {
            _originalSortingLayerName = _sr.sortingLayerName;
            _sr.sortingLayerName = "FlyingObject";
        }

        // --- 3. 물리 엔진 설정 ---
        if (_rb != null)
        {
            _rb.simulated = true;
            _rb.linearDamping = 0f;
            // [최적화된 터널링 방지] 엔진 내부의 고도로 최적화된 연속 충돌 감지 기능을 사용합니다.
            // 비행 중에만 활성화되므로 성능 부하를 최소화하면서 벽 뚫기 현상을 방지합니다.
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        if (_collider != null)
        {
            _collider.enabled = true;
            _collider.isTrigger = true; 
        }

        if (_agent != null) _agent.enabled = false;

        // --- 4. 투척 물리 파라미터 계산 ---
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

        // --- 5. 투척 실행 ---
        if (impactEffect != null) impactEffect.OnPreThrow(p, chargeRatio);

        if (_rb != null) _rb.linearVelocity = direction * p.speed;
        if (_arcMovement != null) _arcMovement.StartArc(p.duration, p.maxHeight);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 이미 충돌이 발생했다면 중복 처리를 방지합니다.
        if (_hasImpacted) return;

        // --- 1. 공통 충돌 체크 (벽 및 장애물) ---
        // 벽이나 장애물은 높이에 상관없이 무조건 투척 유닛을 멈추게 합니다.
        int wallMask = LayerMask.GetMask("Wall", "Obstacle");
        bool isWall = (wallMask & (1 << other.gameObject.layer)) != 0;

        if (isWall)
        {
            if (_arcMovement != null && _arcMovement.IsFlying)
            {
                TriggerImpact(other.gameObject);
                _arcMovement.StopArc();
            }
            return; 
        }

        // --- 2. 직사 투척(Direct Throw) 전용 충돌 체크 ---
        // 직구는 낮게 날아가므로 적군(opponentLayer)과 일반 오브젝트(Object) 모두에 부딪힙니다.
        if (_isDirectThrow)
        {
            int objectMask = LayerMask.GetMask("Object");
            // 적군 레이어 또는 Object 레이어인지 확인
            bool isHitTarget = ((opponentLayer.value | objectMask) & (1 << other.gameObject.layer)) != 0;

            if (isHitTarget && _arcMovement != null && _arcMovement.IsFlying)
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

        // --- 1. 물리 및 레이어 복구 ---
        gameObject.layer = _originalLayer;
        if (_sr != null && !string.IsNullOrEmpty(_originalSortingLayerName))
            _sr.sortingLayerName = _originalSortingLayerName;

        if (_agent != null) _agent.enabled = true;

        // --- 2. 조합 데이터 초기화 ---
        _activeCombination = null;
        _isCombinationLead = false;
        _combinationSupporters = null;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.linearDamping = _originalDamping;
            // [복구] 착지 후에는 다시 일반 감지 모드로 전환
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        }
        if (_collider != null) _collider.isTrigger = false;

        // --- 3. 상태 복구 ---
        if (idleState != null)
        {
            _fsm.ChangeState(idleState);
        }
        
        Debug.Log($"<color=green>[AllyController]</color> {gameObject.name} 착지 완료 및 AI 복구");
    }

    #endregion

    public void SetBattleState(bool _set)
    {
        isBattle = _set;
    }
}

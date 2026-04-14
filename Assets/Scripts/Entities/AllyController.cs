using UnityEngine;
using Necromancer.Interfaces;
using Necromancer.Physics;

public class AllyController : MonoBehaviour, IThrowable
{
    private EntityFSM _fsm;
    private Rigidbody2D _rb;
    private ArcMovement _arcMovement;
    private Collider2D _collider;

    public Transform player;
    public float detectRange = 10f;
    public LayerMask enemyLayer;

    [Header("State Assets")]
    public FSMStateSO idleState;
    public FSMStateSO followState;
    public FSMStateSO attackState;
    public FSMStateSO thrownState;

    [Header("Throw Attack Settings")]
    [SerializeField] private BaseThrowImpactSO impactEffect; // 인스펙터에서 Single/Splash 할당

    [Header("Throw Physics Settings (Ported)")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float straightHeight = 0.1f;
    [SerializeField] private float minSpeed = 5f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private float fullChargeSpeed = 30f;

    [Header(" NearestTargetFinder")]
    [SerializeField] NearestTargetFinder _nearestFinder;

    private LayerMask _hitLayers;
    private float _throwStartTime;
    private float _originalDamping;
    private float _lastChargeRatio; // 충돌 시 데미지 계산용

    [SerializeField] bool isBattle = false;

    public bool IsBattle => isBattle;
    public EntityFSM FSM => _fsm;

    void Awake()
    {
        _fsm = GetComponent<EntityFSM>();
        _rb = GetComponent<Rigidbody2D>();
        _arcMovement = GetComponent<ArcMovement>();
        _collider = GetComponent<Collider2D>();
        _nearestFinder = GetComponent<NearestTargetFinder>();

        if (_rb != null)
        {
            _rb.freezeRotation = true;
            _originalDamping = _rb.linearDamping;
        }

        // 충돌 필터링 설정 (기본적으로 Enemy와 벽 등을 포함)
        _hitLayers = LayerMask.GetMask("Enemy", "Wall", "Obstacle");
        if (_hitLayers == 0)
        {
            _hitLayers = ~(LayerMask.GetMask("Player") | (1 << 2)); // Player와 Ignore Raycast 레이어 제외
        }
    }

    void Update()
    {
        // 던져진 상태이거나 비활성화 상태면 원래 로직 중단
        if (_fsm.currentState == thrownState || !enabled) return;

        // 공격 중일 때 다른 명령 x
        if(_fsm.currentState == attackState && _fsm.target != null)
            return;

        Transform trs = _nearestFinder.FindNearest(detectRange);
        // null일떄는 주변에 없는 것

        if (trs != null)
        {
            float dist = Vector3.Distance(transform.position, trs.position);
            if (dist <= _fsm.stats.ATKRANGE)
            {
                if (_fsm.target == null || _fsm.target != trs)
                    _fsm.target = trs;

                _fsm.ChangeState(attackState);
                return;
            }
        }

        if (isBattle)
        {
            if (trs != null)
            {
                if (_fsm.target == null || _fsm.target != trs)
                    _fsm.target = trs;

                _fsm.ChangeState(followState);
            }
            else
            {
                _fsm.target = player;
            }
        }
        else
        {
            _fsm.target = player;
        }
    }

    #region IThrowable 구현

    public void OnPickedUp()
    {
        _fsm.ChangeState(thrownState);

        if (_rb != null) _rb.simulated = false;
        if (_collider != null) _collider.enabled = false;
    }

    public void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _throwStartTime = Time.time;
        _lastChargeRatio = chargeRatio; // 차징 비율 저장
        transform.rotation = Quaternion.identity;

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

        Vector2 startPos = (Vector2)transform.position;
        Vector2 diff = targetPosition - startPos;
        float distance = diff.magnitude;
        Vector2 direction = diff.normalized;

        float speed;
        float duration;
        float maxHeight;

        if (chargeRatio >= 1.0f)
        {
            speed = fullChargeSpeed;
            duration = 2.0f;
            maxHeight = straightHeight;
        }
        else
        {
            speed = Mathf.Lerp(minSpeed, maxSpeed, chargeRatio);
            duration = distance / speed;

            float targetHeight = Mathf.Lerp(jumpHeight, straightHeight, chargeRatio);
            maxHeight = Mathf.Min(targetHeight, distance * 0.5f);
        }

        if (_rb != null) _rb.linearVelocity = direction * speed;
        if (_arcMovement != null) _arcMovement.StartArc(duration, maxHeight);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 던진 직후의 레이캐스트/트리거 무시
        if (Time.time - _throwStartTime < 0.05f) return;

        // 적 레이어와의 충돌 감지
        if (_arcMovement != null && _arcMovement.IsFlying && (_hitLayers.value & (1 << other.gameObject.layer)) != 0)
        {
            Debug.Log($"<color=red>[Throw Hit]</color> {gameObject.name} hit <b>{other.name}</b>");

            // --- 데미지 처리 ---
            if (impactEffect != null)
            {
                impactEffect.Apply(this.gameObject, other.gameObject, _lastChargeRatio);
            }

            _arcMovement.StopArc();
        }
    }

    public virtual void OnLanded()
    {
        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.linearDamping = _originalDamping;
        }

        if (_collider != null) _collider.isTrigger = false;

        // 착지 후 즉시 플레이어를 따라가도록 상태 복구
        _fsm.target = player;
        _fsm.ChangeState(followState);

        Debug.Log($"{gameObject.name} landed and returning to player!");
    }

    #endregion

    public void SetBattleState(bool _set)
    {
        isBattle = _set;
    }
}

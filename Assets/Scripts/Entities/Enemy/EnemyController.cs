using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [SerializeField] private EntityFSM _fsm;
    public float detectRange = 15f;
    public LayerMask targetLayer;

    [Header("State Assets")]
    public FSMStateSO idleState;
    public FSMStateSO followState;
    public FSMStateSO attackState;

    [Header(" NearestTargetFinder")]
    [SerializeField] NearestTargetFinder _nearestFinder;

    void Awake()
    {
        _fsm = GetComponent<EntityFSM>();
    }

    void Update()
    {
        // 공격 중일 때 타겟 상태 체크
        if (_fsm.currentState == attackState && _fsm.target != null)
        {
            if (_fsm.target.TryGetComponent<CharacterStat>(out var targetStat))
            {
                // 타겟이 죽었거나 플레이어에게 들렸는지(Invincible) 체크
                if (targetStat.IsDead || targetStat.Invincible)
                {
                    _fsm.target = null;
                    _fsm.ChangeState(idleState); // 일단 대기 상태로 전환 후 다음 프레임에 타겟 재탐색
                    return;
                }
            }

            float dist = Vector3.Distance(transform.position, _fsm.target.position);
            // 사거리 밖으로 벗어나면 다시 추적
            if (dist > _fsm.stats.ATKRANGE + 0.5f)
            {
                _fsm.ChangeState(followState);
                return;
            }

            return;
        }

        Transform trs = _nearestFinder.FindNearest(detectRange);

        if (trs != null)
        {
            float dist = Vector3.Distance(transform.position, trs.position);
            if (_fsm.target == null || _fsm.target != trs)
                _fsm.target = trs;

            if (dist <= _fsm.stats.ATKRANGE)
            {
                _fsm.ChangeState(attackState);
                return;
            }
            else
            {
                _fsm.ChangeState(followState); // 추격 상태로 전환
            }
        }
        else
        {
            _fsm.target = null;
            _fsm.ChangeState(idleState);   // 대기 상태로 전환
        }
    }
}

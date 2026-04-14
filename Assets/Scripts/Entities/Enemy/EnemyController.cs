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
        // 공격 중일 때 다른 명령 x
        if(_fsm.currentState == attackState && _fsm.target != null)
            return;

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

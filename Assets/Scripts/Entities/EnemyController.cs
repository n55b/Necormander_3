using UnityEngine;

public class EnemyController : MonoBehaviour
{
    private EntityFSM _fsm;
    public float detectRange = 15f;
    public LayerMask targetLayer;

    [Header("State Assets")]
    public FSMStateSO idleState;
    public FSMStateSO followState;
    public FSMStateSO attackState;

    void Awake() => _fsm = GetComponent<EntityFSM>();

    void Update()
    {
        // 1. 주변의 플레이어나 아군 탐색
        Collider2D[] targets = Physics2D.OverlapCircleAll(transform.position, detectRange, targetLayer);

        if (targets.Length > 0)
        {
            _fsm.target = targets[0].transform;
            _fsm.ChangeState(followState); // 추격 상태로 전환
        }
        else
        {
            _fsm.target = null;
            _fsm.ChangeState(idleState);   // 대기 상태로 전환
        }
    }    
}

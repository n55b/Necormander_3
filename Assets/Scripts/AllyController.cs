using UnityEngine;

public class AllyController : MonoBehaviour
{
    private EntityFSM _fsm;
    public Transform player;
    public float detectRange = 10f;
    public LayerMask enemyLayer;

    [Header("State Assets")]
    public FSMStateSO idleState;
    public FSMStateSO followState;
    public FSMStateSO attackState;

    [SerializeField] bool isBattle = false;

    public bool IsBattle { get { return isBattle; } }
    public EntityFSM FSM { get { return _fsm; } }

    void Awake() => _fsm = GetComponent<EntityFSM>();

    /// <summary>
    /// 1. 적이 공격 사거리에 들어오면 공격한다
    ///     - 플레이어가 이동해도 전투를 끝까지 진행
    ///     - target 설정
    /// 2. 플레이어 상태가 Battle이면 가장 주변의 적을 찾는다
    ///     - 이때 주변을 판단하는 기준은? 각각인가?
    ///     - target 설정 필요
    ///     - 플레이어가 먼저 공격하는 경우도 이에 해당함
    /// 3. 
    /// </summary>

    void Update()
    {
        if (isBattle)
        {
            // 주변에 적이 있는지 체크 (OverlapSphere 등 활용)
            Collider2D[] targets = Physics2D.OverlapCircleAll(transform.position, detectRange, enemyLayer);
            if (targets.Length > 0)
            {
                // 1. 적이 있으면 타겟을 적으로 바꾸고 공격 상태 로직 (필요시 ChangeState)
                _fsm.target = targets[0].transform;
                _fsm.ChangeState(followState);
            }
            else
            {
                // 2. 적이 없으면 타겟을 다시 플레이어로
                _fsm.target = player;
            }
        }
        else
        {
            
        }
    }

    
    public void SetBattleState(bool _set)
    {
        isBattle = _set;
    }
}

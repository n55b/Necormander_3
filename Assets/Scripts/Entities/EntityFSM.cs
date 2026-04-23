using UnityEngine;
using UnityEngine.TextCore.Text;

public class EntityFSM : MonoBehaviour
{
    public FSMStateSO currentState; // 현재 상태 (에셋 드래그 앤 드롭)
    public Transform target;        // 현재 대상 (플레이어 혹은 적)

    [HideInInspector] public CharacterStat stats; // 캐싱용
    [HideInInspector] public Rigidbody2D rb;      // 물리 이동용
    [HideInInspector] public UnityEngine.AI.NavMeshAgent agent; // 이동 제어용 (추가)

    public float atkTimer; // 임시 공격 타이머

    void Awake()
    {
        stats = GetComponent<CharacterStat>();
        rb = GetComponent<Rigidbody2D>();
        agent = GetComponent<UnityEngine.AI.NavMeshAgent>();

        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            // 같은 레이어끼리는 물리적으로 '벽'처럼 막지 않도록 설정 (소프트 밀기 전제조건)
            // 프로젝트 세팅을 건드리지 않고 코드로 제어합니다.
            Physics2D.IgnoreLayerCollision(gameObject.layer, gameObject.layer, true);
        }
    }

    void Start()
    {
        if (currentState != null)
            currentState.Enter(this);
    }

    void Update()
    {
        if (currentState != null)
            currentState.Execute(this);
    }

    public void ChangeState(FSMStateSO newState)
    {
        if (newState ==  null || newState == currentState) return;
        
        currentState.Exit(this);
        currentState = newState;
        currentState.Enter(this);
    }
}

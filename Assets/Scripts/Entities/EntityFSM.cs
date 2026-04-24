using UnityEngine;

/// <summary>
/// [사용 권장되지 않음] 과거 FSM 시스템의 유산입니다.
/// 현재는 애니메이션 상태 참조용 데이터 보관함 역할만 수행합니다.
/// 향후 새로운 애니메이션 시스템으로 완전히 대체될 예정입니다.
/// </summary>
public class EntityFSM : MonoBehaviour
{
    // 애니메이터가 현재 상태를 파악하기 위해 참조할 수 있는 필드들
    public FSMStateSO currentState; 
    public Transform target;        

    [HideInInspector] public CharacterStat stats;
    [HideInInspector] public Rigidbody2D rb;      
    [HideInInspector] public UnityEngine.AI.NavMeshAgent agent; 

    public float atkTimer; 

    private void Awake()
    {
        // 로직 없음 (데이터 보관용)
    }

    private void Update()
    {
        // 로직 없음 (브레인이 전담)
    }

    public void ChangeState(FSMStateSO newState)
    {
        // 로직 없음 (동작하지 않음)
    }
}

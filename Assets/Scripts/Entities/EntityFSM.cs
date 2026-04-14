using UnityEngine;
using UnityEngine.TextCore.Text;

public class EntityFSM : MonoBehaviour
{
    public FSMStateSO currentState; // 현재 상태 (에셋 드래그 앤 드롭)
    public Transform target;        // 현재 대상 (플레이어 혹은 적)

    [HideInInspector] public CharacterStat stats; // 캐싱용

    public float atkTimer; // 임시 공격 타이머

    void Awake()
    {
        stats = GetComponent<CharacterStat>();
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

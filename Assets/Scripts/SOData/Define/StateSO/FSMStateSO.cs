using UnityEngine;

public abstract class FSMStateSO : ScriptableObject
{
    // 상태에 진입할 때 한 번 실행
    public virtual void Enter(EntityFSM fsm) { }

    // 매 프레임 실행 (LogicUpdate)
    public abstract void Execute(EntityFSM fsm);

    // 상태를 빠져나갈 때 한 번 실행
    public virtual void Exit(EntityFSM fsm) { }
}

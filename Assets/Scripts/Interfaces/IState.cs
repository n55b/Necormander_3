using UnityEngine;

public abstract class IState
{
    protected EntityFSM _fsm;
    public IState(EntityFSM fsm) => _fsm = fsm;

    public virtual void Enter() {} // 상태 진입 시
    public virtual void Update() {} // 매 프레임
    public virtual void Exit() {} // 상태 종료 시
}

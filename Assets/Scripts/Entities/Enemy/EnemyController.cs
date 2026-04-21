using UnityEngine;

/// <summary>
/// 적군 유닛 전용 컨트롤러입니다. 
/// BaseEntity를 상속받으며, 적군만의 특수한 로직(보상, 스폰 관리 등)을 가집니다.
/// </summary>
public class EnemyController : BaseEntity
{
    protected override void Awake()
    {
        base.Awake();
        team = Team.Enemy;
    }

    protected override void HandleNoTarget()
    {
        // 적군은 타겟이 없으면 제자리 대기(Idle) 상태를 유지합니다.
        _fsm.target = null;
        _fsm.ChangeState(idleState);
    }

    // 적군 사망 시 보상 지급 등의 추가 로직을 여기에 작성할 수 있습니다.
    private void OnDestroy()
    {
        // 예: GameManager.Instance.dataManager.AddBonePoint(10);
    }
}

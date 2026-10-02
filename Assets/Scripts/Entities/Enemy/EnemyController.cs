using UnityEngine;

/// <summary>
/// 적군 유닛 전용 컨트롤러입니다. 
/// AIPatternSO(브레인)가 결정한 행동을 보조합니다.
/// </summary>
public class EnemyController : BaseEntity
{
    protected override void Awake()
    {
        // base.Awake()가 스탯과 적 목록을 등록하므로 팀을 먼저 확정한다.
        team = Team.Enemy;
        base.Awake();
    }

    protected override void HandleNoTarget()
    {
        // 이제 브레인이 스스로 판단하므로, 브레인 외부에서의 강제 개입은 최소화합니다.
    }

    protected override void OnDestroy()
    {
        base.OnDestroy(); // 브레인 클론 정리 (BaseEntity)
        // 사망 시 재화 지급 등의 보상 로직을 여기에 추가할 수 있습니다.
    }
}

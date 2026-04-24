using UnityEngine;

/// <summary>
/// 적군 유닛 전용 컨트롤러입니다. 
/// AIPatternSO(브레인)가 결정한 행동을 보조합니다.
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
        // 이제 브레인이 스스로 판단하므로, 브레인 외부에서의 강제 개입은 최소화합니다.
    }

    private void OnDestroy()
    {
        // 사망 시 재화 지급 등의 보상 로직을 여기에 추가할 수 있습니다.
    }
}

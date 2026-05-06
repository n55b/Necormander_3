using UnityEngine;

/// <summary>
/// 아무것도 하지 않고 제자리에 고정되어 있는 AI 패턴입니다.
/// 허수아비나 훈련용 더미에 사용합니다.
/// </summary>
[CreateAssetMenu(fileName = "StationaryPattern", menuName = "Necromancer/AI/Stationary Pattern")]
public class StationaryPatternSO : AIPatternSO
{
    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        currentState = AIState.Idle;
    }

    public override void Execute(BaseEntity entity)
    {
        // 아무것도 하지 않음 (타겟팅, 상태 전환, 물리 밀치기 모두 배제)
        // 오직 던져진 상태(Thrown)일 때만 기본 동작(중단)을 따릅니다.
        if (currentState == AIState.Thrown) return;
    }

    protected override void UpdateTargeting(BaseEntity entity) { }
    protected override void UpdateStateTransitions(BaseEntity entity) { }
    protected override void OnIdle(BaseEntity entity) { }
    protected override void OnFollow(BaseEntity entity) { }
    protected override void OnAttack(BaseEntity entity) { }
}

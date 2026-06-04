using UnityEngine;

/// <summary>
/// 전사: 단일 적에게 데미지를 입힙니다.
/// </summary>
public class WarriorAction : ImpactAction
{
    public float damage;
    public WarriorAction(float val) => damage = val;

    private static GameObject _lastHitTarget; // [유니크] 추적하는 눈 용 기록

    public override void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe)
    {
        if (target.TryGetComponent<BaseEntity>(out var entity) && entity.team == Team.Enemy)
        {
            // 데미지는 BaseDamageAction에서 일괄 처리되므로, 여기서는 이벤트/시각적 처리용 더미 값만 사용합니다.
            float finalDamage = recipe.GetScaledBonusDamage(); // 시각 효과 등에 참조될 수 있으므로 전사 보너스만 계산
            float radius = 0f;

            ThrowEventBus.TriggerThrowImpactBeforeDamage(CommandData.SkeletonWarrior, this, impactPos, ref finalDamage, ref radius, target);

            // 직접 GetDamage를 호출하지 않습니다. (BaseDamageAction이 담당)

            Collider2D[] dummyHits = new Collider2D[0];
            ThrowEventBus.TriggerThrowImpactAfterDamage(CommandData.SkeletonWarrior, this, impactPos, dummyHits, target);
        }
    }
}

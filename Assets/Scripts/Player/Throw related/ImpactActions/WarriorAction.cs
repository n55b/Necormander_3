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
            float finalDamage = recipe.GetScaledValue(damage);
            float radius = 0f;

            ThrowEventBus.TriggerThrowImpactBeforeDamage(CommandData.SkeletonWarrior, this, impactPos, ref finalDamage, ref radius, target);

            float hpBefore = entity.Stats.Health.CurHP;

            entity.Stats.Health.GetDamage(new DamageInfo(finalDamage, DamageType.Physical, null, true));

            Collider2D[] dummyHits = new Collider2D[0]; // 단일 타겟 스킬이므로 빈 배열 전달 (필요 시 target collider 포함 가능)
            ThrowEventBus.TriggerThrowImpactAfterDamage(CommandData.SkeletonWarrior, this, impactPos, dummyHits, target);
        }
    }
}

using UnityEngine;

/// <summary>
/// 궁수: 범위 내의 적에게 데미지를 입히며, 투척 범위를 결정합니다.
/// </summary>
public class ArcherAction : ImpactAction
{
    public float damage;
    public float radius;

    public ArcherAction(float dmg, float rad) { damage = dmg; radius = rad; }

    public override void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe)
    {
        if (recipe.info.targetingMode == TargetingMode.Area && target.TryGetComponent<BaseEntity>(out var entity) && entity.team == Team.Enemy)
        {
            float finalDamage = recipe.GetScaledValue(damage);
            float currentRadius = radius;

            ThrowEventBus.TriggerThrowImpactBeforeDamage(CommandData.SkeletonArcher, this, impactPos, ref finalDamage, ref currentRadius, target);

            entity.Stats.Health.GetDamage(new DamageInfo(finalDamage, DamageType.Physical, null, true));
            
            Collider2D[] dummyHits = new Collider2D[0]; // ArcherAction은 Area 폭발 각각의 개별 히트에 대한 처리이므로 필요 시 보완
            ThrowEventBus.TriggerThrowImpactAfterDamage(CommandData.SkeletonArcher, this, impactPos, dummyHits, target);
        }
    }
}

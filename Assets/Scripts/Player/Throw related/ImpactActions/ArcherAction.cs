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
        if (recipe.targetingMode == TargetingMode.Area && target.TryGetComponent<BaseEntity>(out var entity) && entity.team == Team.Enemy)
        {
            float finalDamage = recipe.GetScaledValue(damage);
            entity.Stats.GetDamage(new DamageInfo(finalDamage, DamageType.Physical, null));
        }
    }
}

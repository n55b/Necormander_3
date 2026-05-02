using UnityEngine;

/// <summary>
/// 전사: 단일 적에게 데미지를 입힙니다.
/// </summary>
public class WarriorAction : ImpactAction
{
    public float damage;
    public WarriorAction(float val) => damage = val;

    public override void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe)
    {
        if (target.TryGetComponent<BaseEntity>(out var entity) && entity.team == Team.Enemy)
        {
            float finalDamage = recipe.GetScaledValue(damage);
            entity.Stats.GetDamage(new DamageInfo(finalDamage, DamageType.Physical, null));
        }
    }
}

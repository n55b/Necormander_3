using UnityEngine;

/// <summary>
/// 방패병: 아군과 플레이어에게 보호막을 부여합니다.
/// </summary>
public class ShieldBearerAction : ImpactAction
{
    public float shieldAmount;
    public ShieldBearerAction(float val) => shieldAmount = val;

    public override void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe)
    {
        CharacterStat targetStat = null;
        if (target.TryGetComponent<BaseEntity>(out var entity) && entity.team == Team.Ally) targetStat = entity.Stats;
        else if (target.CompareTag("Player")) targetStat = target.GetComponentInChildren<CharacterStat>();

        if (targetStat != null)
        {
            bool allowShield = (recipe.targetingMode == TargetingMode.Self) || (recipe.targetingMode == TargetingMode.Area) || (recipe.targetTeam == Team.Ally);
            if (allowShield)
            {
                float finalShield = recipe.GetScaledValue(shieldAmount);
                targetStat.AddShield(finalShield, 3.0f);
                
                ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
                if (registry != null && registry.shieldAttachVFX != null)
                {
                    GameObject vfx = Object.Instantiate(registry.shieldAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                    targetStat.SetShieldVFX(vfx);
                }
            }
        }
    }
}

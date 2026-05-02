using UnityEngine;

/// <summary>
/// 사제: 적에게 슬로우를 걸고, 플레이어에게는 정화 연출을 보여줍니다.
/// </summary>
public class PriestAction : ImpactAction
{
    public float ccPower;
    public PriestAction(float val) => ccPower = val;

    public override void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe)
    {
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        
        if (target.TryGetComponent<BaseEntity>(out var entity) && entity.team == Team.Enemy)
        {
            float slowAmount = recipe.GetScaledValue(ccPower);
            float duration = 5.0f;
            entity.Stats.Status.ApplySlow("ThrowCC", slowAmount, duration);
            if (registry != null && registry.ccAttachVFX != null)
            {
                GameObject vfx = Object.Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                Object.Destroy(vfx, duration);
            }
        }
        else if (target.CompareTag("Player"))
        {
            if (recipe.targetingMode == TargetingMode.Self || recipe.targetingMode == TargetingMode.Area)
            {
                if (registry != null && registry.ccAttachVFX != null)
                {
                    CharacterStat pStat = target.GetComponentInChildren<CharacterStat>();
                    GameObject vfx = Object.Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                    if (pStat != null) pStat.Visual.SetCCVFX(vfx);
                    Object.Destroy(vfx, 1.0f);
                }
            }
        }
    }
}

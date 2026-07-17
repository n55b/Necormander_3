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
        
        BaseEntity entity = target.GetComponentInChildren<BaseEntity>();
        if (entity != null && entity.team == Team.Enemy)
        {
            // 기존 기본 슬로우 유지
            float slowAmount = recipe.GetScaledEffectValue(ccPower);
            float duration = 5.0f;
            entity.Stats.Status.ApplySlow("ThrowCC", slowAmount, duration);

            // [26/07/17] 사제 시너지의 한기/노화/부식 부여는 구 디버프와 함께 삭제됐다. 슬로우만 남는다.

            if (registry != null && registry.ccAttachVFX != null)
            {
                GameObject vfx = Object.Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                Object.Destroy(vfx, duration);
            }
        }
        else if (target.CompareTag("Player"))
        {
            if (recipe.info.targetingMode == TargetingMode.Self || recipe.info.targetingMode == TargetingMode.Area)
            {
                if (registry != null && registry.ccAttachVFX != null)
                {
                    CharacterStat pStat = target.GetComponentInParent<CharacterStat>();
                    if (pStat == null) pStat = target.GetComponentInChildren<CharacterStat>();
                    GameObject vfx = Object.Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                    if (pStat != null) pStat.Visual.SetCCVFX(vfx);
                    Object.Destroy(vfx, 1.0f);
                }
            }
        }
    }
}

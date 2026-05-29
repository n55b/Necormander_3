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
            // 기존 기본 슬로우 유지
            float slowAmount = recipe.GetScaledValue(ccPower);
            float duration = 5.0f;
            entity.Stats.Status.ApplySlow("ThrowCC", slowAmount, duration);

            // [추가] 사제 전용 직업 시너지 (한기, 노화, 부식)
            InventoryManager inven = InventoryManager.Instance;
            if (inven != null)
            {
                // 기본 1스택에 대해 풀차지/조합(multiplierBonus 등) 배율 적용
                float scaledStacks = recipe.GetScaledValue(1.0f);

                // 2개 시너지 (Level 1) 이상일 때 사제 발현
                if (GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Priest_Chill)) >= 1)
                {
                    entity.Stats.Status.AddDebuffStack(DebuffStackType.Chill, scaledStacks);
                }
                
                if (GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Priest_Aging)) >= 1)
                {
                    entity.Stats.Status.AddDebuffStack(DebuffStackType.Aging, scaledStacks);
                }

                if (GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Priest_Corrosion)) >= 1)
                {
                    // 부식은 Bool 타입이므로 지속시간(예: 5초) 부여
                    entity.Stats.Status.SetDebuffBool(DebuffBoolType.Corroded, 5.0f);
                }
            }

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
                    CharacterStat pStat = target.GetComponentInChildren<CharacterStat>();
                    GameObject vfx = Object.Instantiate(registry.ccAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                    if (pStat != null) pStat.Visual.SetCCVFX(vfx);
                    Object.Destroy(vfx, 1.0f);
                }
            }
        }
    }
}

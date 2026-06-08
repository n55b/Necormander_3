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
        // 1. 타겟 정보 확인
        CharacterStat targetStat = null;
        bool isAllyOrPlayer = false;

        BaseEntity entity = target.GetComponentInChildren<BaseEntity>();
        if (entity != null)
        {
            if (entity.team == Team.Ally)
            {
                targetStat = entity.Stats;
                isAllyOrPlayer = true;
            }
        }
        else if (target.CompareTag("Player"))
        {
            targetStat = target.GetComponentInParent<CharacterStat>();
            if (targetStat == null) targetStat = target.GetComponentInChildren<CharacterStat>();
            isAllyOrPlayer = true;
        }

        float finalShield = recipe.GetScaledEffectValue(shieldAmount);
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;

        // [이벤트 버스] 투척 착탄 전 이벤트: 보호막 수치나 추가 데미지 스탯 변경 가능
        float radius = 0f;
        ThrowEventBus.TriggerThrowImpactBeforeDamage(CommandData.SkeletonShieldbearer, this, impactPos, ref finalShield, ref radius, target);

        // 2. 아군/플레이어인 경우: 즉시 보호막 부여
        if (isAllyOrPlayer && targetStat != null)
        {
            targetStat.Status.AddShield(finalShield, 3.0f);
            
            if (registry != null && registry.shieldAttachVFX != null)
            {
                GameObject vfx = Object.Instantiate(registry.shieldAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                targetStat.Visual.SetShieldVFX(vfx);
            }
        }
        // 적군인 경우: 아무 효과도 주지 않습니다. (데미지는 BaseDamageAction이 처리)

        // [이벤트 버스] 투척 착탄 후 이벤트: 광역 폭발 데미지 등 추가 이펙트 처리
        Collider2D[] dummyHits = new Collider2D[0];
        ThrowEventBus.TriggerThrowImpactAfterDamage(CommandData.SkeletonShieldbearer, this, impactPos, dummyHits, target);
    }
}

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
            var inven = InventoryManager.Instance;
            float finalDamage = recipe.GetScaledValue(damage);

            if (inven != null)
            {
                float ballisticsMultiplier = 1f;
                // [유니크] 전사 인형 탄도학 I (포물선 던지기 데미지 10% 증가)
                if (!recipe.info.isDirect && inven.HasUniqueEffect(GemUniqueType.WarriorBallistics1)) ballisticsMultiplier += 0.10f;
                
                // [유니크] 전사 인형 탄도학 II (직구 던지기 데미지 10% 증가)
                if (recipe.info.isDirect && inven.HasUniqueEffect(GemUniqueType.WarriorBallistics2)) ballisticsMultiplier += 0.10f;
                
                // [유니크] 전사 인형 탄도학 III (던지기 단일 적용 효과 15% 증가)
                if (recipe.info.targetingMode == TargetingMode.Target && inven.HasUniqueEffect(GemUniqueType.WarriorBallistics3)) ballisticsMultiplier += 0.15f;

                finalDamage *= ballisticsMultiplier;

                // [유니크] 추적하는 눈 (TrackingEye): 연속으로 같은 대상을 맞출 시 데미지 12% 증가
                if (inven.HasUniqueEffect(GemUniqueType.TrackingEye))
                {
                    if (_lastHitTarget == target) finalDamage *= 1.12f;
                }
            }

            _lastHitTarget = target; // 마지막 피격 타겟 갱신

            // [시너지] 처형자 (Warrior_Executioner)
            if (inven != null)
            {
                int execLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Warrior_Executioner));
                float hpRatio = entity.Stats.Health.MaxHP > 0 ? entity.Stats.Health.CurHP / entity.Stats.Health.MaxHP : 1f;

                if (execLevel >= 1 && hpRatio < 0.5f) // (2) 스택: 50% 미만 적 20% 증가
                {
                    finalDamage *= 1.2f;
                }
                
                if (execLevel >= 3 && hpRatio <= 0.3f) // (6) 스택: 30% 이하 시 최대 50% 비례 증폭
                {
                    float extraAmp = 0.5f * ((0.3f - hpRatio) / 0.3f);
                    finalDamage *= (1f + extraAmp);
                }
            }

            float hpBefore = entity.Stats.Health.CurHP;

            entity.Stats.Health.GetDamage(new DamageInfo(finalDamage, DamageType.Physical, null, true));

            // [유니크] 짓눌리는 힘 (CrushingPower): 던지기 피해 초과 시(압살) 전사 체력 회복
            if (inven != null && inven.HasUniqueEffect(GemUniqueType.CrushingPower))
            {
                if (finalDamage > hpBefore)
                {
                    float excess = finalDamage - hpBefore;
                    foreach (var unit in recipe.state.heldUnits)
                    {
                        if (unit.MinionType == CommandData.SkeletonWarrior)
                        {
                            var allyHealth = unit.transform.GetComponentInChildren<CharacterHealth>();
                            if (allyHealth == null) allyHealth = unit.transform.GetComponentInParent<CharacterHealth>();
                            if (allyHealth != null) allyHealth.Heal(excess);
                        }
                    }
                }
            }

            ApplyCommonSynergyDebuffs(target, recipe);
        }
    }
}

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
            var inven = InventoryManager.Instance;
            float finalDamage = recipe.GetScaledValue(damage);

            if (inven != null)
            {
                // [유니크] 선관지형 (ArcherTerrain): 범위 내 적 마리당 던지기 효과 5% 증가
                if (inven.HasUniqueEffect(GemUniqueType.ArcherTerrain) && recipe.state.areaTargets != null)
                {
                    int targetCount = recipe.state.areaTargets.Count;
                    finalDamage *= (1.0f + 0.05f * targetCount);
                }

                // [유니크] 전추태산 (ArcherPush): 광역 피해 15% 증가
                if (inven.HasUniqueEffect(GemUniqueType.ArcherPush))
                {
                    finalDamage *= 1.15f;
                }

                // [유니크] 비정비팔 (ArcherStance) 및 [시너지] 6스택: 중앙(30% 이내) 적 추가 피해
                float dist = Vector2.Distance(target.transform.position, impactPos);
                if (dist <= radius * 0.3f)
                {
                    if (inven.HasUniqueEffect(GemUniqueType.ArcherStance)) finalDamage *= 1.20f;

                    int archerSynLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Archer_ArcheryPrinciples));
                    if (archerSynLevel >= 3) // (6) 스택
                    {
                        finalDamage *= 1.50f;
                    }
                }

                // [유니크] 흉어복실 (ArcherBreath): 비행 거리에 비례하여 최대 33% 증가
                if (inven.HasUniqueEffect(GemUniqueType.ArcherBreath) && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
                {
                    Vector2 playerPos = GameManager.Instance.PLAYERCONTROLLER.transform.position;
                    float flightDist = Vector2.Distance(playerPos, impactPos);
                    float ratio = Mathf.Clamp01(flightDist / 15f); // 15 유닛을 최대 비행 거리로 산정
                    finalDamage *= (1.0f + 0.33f * ratio);
                }
            }

            entity.Stats.Health.GetDamage(new DamageInfo(finalDamage, DamageType.Physical, null, true));
            ApplyCommonSynergyDebuffs(target, recipe);
        }
    }
}

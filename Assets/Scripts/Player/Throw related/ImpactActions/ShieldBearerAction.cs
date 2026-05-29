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

        if (target.TryGetComponent<BaseEntity>(out var entity))
        {
            if (entity.team == Team.Ally)
            {
                targetStat = entity.Stats;
                isAllyOrPlayer = true;
            }
        }
        else if (target.CompareTag("Player"))
        {
            targetStat = target.GetComponentInChildren<CharacterStat>();
            isAllyOrPlayer = true;
        }

        float finalShield = recipe.GetScaledValue(shieldAmount);
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;

        // 2. 아군/플레이어인 경우: 즉시 보호막 부여 (기본 전사+방패병 조합 등)
        if (isAllyOrPlayer && targetStat != null)
        {
            targetStat.Status.AddShield(finalShield, 3.0f);
            
            if (registry != null && registry.shieldAttachVFX != null)
            {
                GameObject vfx = Object.Instantiate(registry.shieldAttachVFX, target.transform.position, Quaternion.identity, target.transform);
                targetStat.Visual.SetShieldVFX(vfx);
            }
        }
        // 3. 적군인 경우: 보호막 아이템 드랍 및 적군 피해(유니크/시너지)
        else if (!isAllyOrPlayer && target.TryGetComponent(out CharacterHealth enemyHealth))
        {
            var inven = InventoryManager.Instance;
            float totalDamage = 0f;

            if (inven != null)
            {
                // [유니크] 육중한 갑옷: 방패 수치의 14% 단일 피해
                if (inven.HasUniqueEffect(GemUniqueType.HeavyArmor))
                {
                    totalDamage += finalShield * 0.14f;
                }

                // [유니크] 뒤틀리는 지반: 방패 수치의 20% 범위 피해
                bool hasTwistedGround = inven.HasUniqueEffect(GemUniqueType.TwistedGround);
                
                // [시너지] 수호신(Shield_Guardian) (2) 스택: 방패 수치의 20% 광역 피해
                int guardianLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Shield_Guardian));
                bool hasGuardianAoE = guardianLevel >= 1; // (2) 스택

                if (hasTwistedGround || hasGuardianAoE)
                {
                    float aoeDamage = finalShield * 0.20f;
                    if (hasTwistedGround && hasGuardianAoE) aoeDamage = finalShield * 0.40f; // 둘 다 있으면 40%

                    float radius = 2.5f;
                    foreach (var status in CharacterStatus.ActiveEnemies)
                    {
                        if (status != null && Vector2.Distance(impactPos, status.transform.position) <= radius)
                        {
                            if (status.TryGetComponent(out CharacterHealth health))
                            {
                                health.GetDamage(new DamageInfo(aoeDamage, DamageType.Physical, null));
                            }
                        }
                    }
                }
            }

            if (totalDamage > 0f)
            {
                enemyHealth.GetDamage(new DamageInfo(totalDamage, DamageType.Physical, null));
            }

            if (registry != null && registry.shieldCollectiblePrefab != null)
            {
                GameObject itemObj = Object.Instantiate(registry.shieldCollectiblePrefab, impactPos, Quaternion.identity);
                ShieldCollectible collectible = itemObj.GetComponent<ShieldCollectible>();
                if (collectible == null) collectible = itemObj.AddComponent<ShieldCollectible>();
                
                collectible.Init(finalShield, 3.0f);
            }
        }
    }
}

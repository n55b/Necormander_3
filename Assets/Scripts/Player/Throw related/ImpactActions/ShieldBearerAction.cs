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
        // 3. 적군인 경우: 보호막 아이템 드랍 (궁수+방패병 등 적군 타겟팅 조합)
        else if (!isAllyOrPlayer && target.CompareTag("Enemy"))
        {
            if (registry != null && registry.shieldCollectiblePrefab != null)
            {
                GameObject itemObj = Object.Instantiate(registry.shieldCollectiblePrefab, impactPos, Quaternion.identity);
                ShieldCollectible collectible = itemObj.GetComponent<ShieldCollectible>();
                if (collectible == null) collectible = itemObj.AddComponent<ShieldCollectible>();
                
                collectible.Init(finalShield, 3.0f);
                Debug.Log($"<color=cyan>[Shield Action]</color> 적군 타격! 보호막 아이템 드랍. (수치: {finalShield:F1})");
            }
        }
    }
}

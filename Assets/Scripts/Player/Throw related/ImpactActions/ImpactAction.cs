using UnityEngine;

/// <summary>
/// 투척 충격 시 실행될 개별 액션의 추상 기반 클래스입니다.
/// </summary>
[System.Serializable]
public abstract class ImpactAction
{
    public abstract void Execute(GameObject target, Vector2 impactPos, Vector2 travelDir, ThrowRecipe recipe);

    protected void ApplyCommonSynergyDebuffs(GameObject target, ThrowRecipe recipe)
    {
        if (target.TryGetComponent<CharacterStatus>(out var status))
        {
            var stat = target.GetComponentInChildren<CharacterStat>();
            if (stat == null || !stat.IsEnemy) return;

            var inven = InventoryManager.Instance;
            if (inven == null) return;
            
            float debuffMult = recipe.GetScaledValue(1f);
            
            // [유니크] 중독 플라스크: 기본값 +1 스택
            float poisonBase = inven.HasUniqueEffect(GemUniqueType.PoisonFlask) ? 2f : 1f;

            if (inven.GetSynergyCount(GemSynergyGroup.Poison) >= 2)
                status.AddDebuffStack(DebuffStackType.Poison, poisonBase * debuffMult);
                
            if (inven.GetSynergyCount(GemSynergyGroup.BloodPop) >= 2)
                status.AddDebuffStack(DebuffStackType.BloodPop, debuffMult);
                
            if (inven.GetSynergyCount(GemSynergyGroup.Execution) >= 2)
                status.AddDebuffStack(DebuffStackType.Execute, debuffMult);
        }
    }
}

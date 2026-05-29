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
        var stat = target.GetComponentInParent<CharacterStat>();
        if (stat == null) stat = target.GetComponentInChildren<CharacterStat>();
        
        if (stat == null || !stat.IsEnemy) return;
        
        var status = stat.Status;
        if (status == null) return;

        var inven = InventoryManager.Instance;
        if (inven == null) return;
            
            float debuffMult = recipe.GetScaledValue(1f);
            
            // [유니크] 중독 플라스크: 기본값 +1 스택
            float poisonBase = inven.HasUniqueEffect(GemUniqueType.PoisonFlask) ? 2f : 1f;

            int poisonSynergy = inven.GetSynergyCount(GemSynergyGroup.Poison);
            int bloodPopSynergy = inven.GetSynergyCount(GemSynergyGroup.BloodPop);
            int executionSynergy = inven.GetSynergyCount(GemSynergyGroup.Execution);

            Debug.Log($"<color=orange>[Synergy Check]</color> 투척 명중! Poison:{poisonSynergy}, BloodPop:{bloodPopSynergy}, Execution:{executionSynergy}, Mult:{debuffMult}");

            if (poisonSynergy >= 2)
                status.AddDebuffStack(DebuffStackType.Poison, poisonBase * debuffMult);
                
            if (bloodPopSynergy >= 2)
                status.AddDebuffStack(DebuffStackType.BloodPop, debuffMult);
                
            if (executionSynergy >= 2)
                status.AddDebuffStack(DebuffStackType.Execute, debuffMult);
    }
}

using UnityEngine;

/// <summary>
/// 보석 시스템의 모든 규칙(시너지 + 유니크)을 통합하여 최종 수치를 제공하는 중앙 핸들러입니다.
/// </summary>
public static class GemRuleSystem
{
    private static InventoryManager Inven => InventoryManager.Instance;

    #region Poison Rules

    public static float GetPoisonInterval(bool isEnemyTarget)
    {
        float baseInterval = 5.0f; // [수정] 기본 틱 주기를 기획에 맞춰 5초로 변경
        if (Inven == null || !isEnemyTarget) return baseInterval;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Poison));
        bool hasLethalDose = Inven.HasUniqueEffect(GemUniqueType.LethalDose);

        float mult = GemSynergyLogic.GetPoisonIntervalMultiplier(level) * 
                     GemUniqueLogic.GetLethalDoseIntervalMultiplier(hasLethalDose);
        
        return baseInterval * mult;
    }

    public static float GetPoisonDuration(bool isEnemyTarget)
    {
        float baseDuration = 10.0f;
        if (Inven == null || !isEnemyTarget) return baseDuration;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Poison));
        return baseDuration + GemSynergyLogic.GetPoisonDurationBonus(level);
    }

    public static float ModifyIncomingPoisonStack(float amount, bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return amount;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Poison));
        return amount + GemSynergyLogic.GetPoisonExtraStack(level);
    }

    public static float GetLethalPoisonBonus(int currentStacks) // 이건 던질 때 사용하므로 유지
    {
        if (Inven == null) return 0f;
        bool hasLethalPoison = Inven.HasUniqueEffect(GemUniqueType.LethalPoison);
        return GemUniqueLogic.GetLethalPoisonBonus(hasLethalPoison, currentStacks);
    }

    #endregion

    #region Chill Rules

    public static float GetChillValuePerStack(bool isEnemyTarget)
    {
        float baseValue = 0.01f; // 1%
        if (Inven == null || !isEnemyTarget) return baseValue;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Chill));
        return baseValue * GemSynergyLogic.GetChillValueMultiplier(level);
    }

    public static float GetMaxChillStack(bool isEnemyTarget)
    {
        float baseMax = 20f;
        if (Inven == null || !isEnemyTarget) return baseMax;

        bool hasFlower = Inven.HasUniqueEffect(GemUniqueType.SlowlyFreezingFlower);
        return baseMax + GemUniqueLogic.GetSlowlyFreezingFlowerMaxBonus(hasFlower);
    }

    public static bool ShouldBlockChill(bool isFrozen, bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return false;
        return GemUniqueLogic.ShouldBlockChillStack(Inven.HasUniqueEffect(GemUniqueType.AchingBones), isFrozen);
    }

    public static float GetFreezeRefundStacks(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return 0f; 
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Chill));
        return GemSynergyLogic.GetChillRefundAmount(level);
    }

    public static bool HasFreezeFixedDamage(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return false;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Chill));
        return GemSynergyLogic.HasChillFreezeDamage(level);
    }

    #endregion

    #region BloodPop Rules

    public static float GetBloodPopDamage(int stacks, bool isEnemyTarget)
    {
        float damage = stacks;
        if (Inven == null || !isEnemyTarget) return damage;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.BloodPop));
        return damage + GemSynergyLogic.GetBloodPopDamageBonus(level);
    }

    public static float GetBloodPopRadiusMultiplier(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return 1.0f;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.BloodPop));
        return GemSynergyLogic.GetBloodPopRadiusMultiplier(level);
    }

    public static float GetBloodPopChainRatio(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return 0f;
        return GemUniqueLogic.GetExplodingFleshStackRatio(Inven.HasUniqueEffect(GemUniqueType.ExplodingFlesh));
    }

    #endregion

    #region Aging Rules

    public static float GetAgingValuePerStack(bool isEnemyTarget)
    {
        float baseValue = 0.01f;
        if (Inven == null || !isEnemyTarget) return baseValue;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Aging));
        return baseValue * GemSynergyLogic.GetAgingValueMultiplier(level);
    }

    public static float GetMaxAgingStack(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return 25f;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Aging));
        bool hasNoCountry = Inven.HasUniqueEffect(GemUniqueType.NoCountryForOldMen);

        float max = GemUniqueLogic.GetNoCountryMaxStack(hasNoCountry);
        max += GemSynergyLogic.GetAgingMaxStackBonus(level);

        return max;
    }

    public static bool ShouldAgingInstaKill(float currentStacks, bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return false;
        return GemUniqueLogic.ShouldAgingInstaKill(Inven.HasUniqueEffect(GemUniqueType.NoCountryForOldMen), currentStacks);
    }

    #endregion

    #region Corrosion Rules

    public static float GetCorrosionDamageAmp(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return 0f;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Corrosion));
        return GemSynergyLogic.GetCorrosionDamageAmp(level);
    }

    #endregion
}

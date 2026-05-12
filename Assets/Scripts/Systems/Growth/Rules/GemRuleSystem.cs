using UnityEngine;

/// <summary>
/// 보석 시스템의 모든 규칙(시너지 + 유니크)을 통합하여 최종 수치를 제공하는 중앙 핸들러입니다.
/// </summary>
public static class GemRuleSystem
{
    private static InventoryManager Inven => InventoryManager.Instance;

    #region Poison Rules

    public static float GetPoisonInterval()
    {
        float baseInterval = 3.0f; // 기본 틱 주기
        if (Inven == null) return baseInterval;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Poison));
        bool hasLethalDose = Inven.HasUniqueEffect(GemUniqueType.LethalDose);

        float mult = GemSynergyLogic.GetPoisonIntervalMultiplier(level) * 
                     GemUniqueLogic.GetLethalDoseIntervalMultiplier(hasLethalDose);
        
        return baseInterval * mult;
    }

    public static float GetPoisonDuration()
    {
        float baseDuration = 10.0f;
        if (Inven == null) return baseDuration;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Poison));
        return baseDuration + GemSynergyLogic.GetPoisonDurationBonus(level);
    }

    public static float ModifyIncomingPoisonStack(float amount)
    {
        if (Inven == null) return amount;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Poison));
        return amount + GemSynergyLogic.GetPoisonExtraStack(level);
    }

    public static float GetLethalPoisonBonus(int currentStacks)
    {
        if (Inven == null) return 0f;
        bool hasLethalPoison = Inven.HasUniqueEffect(GemUniqueType.LethalPoison);
        return GemUniqueLogic.GetLethalPoisonBonus(hasLethalPoison, currentStacks);
    }

    #endregion

    #region Chill Rules

    public static float GetChillValuePerStack()
    {
        float baseValue = 0.01f; // 1%
        if (Inven == null) return baseValue;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Chill));
        return baseValue * GemSynergyLogic.GetChillValueMultiplier(level);
    }

    public static float GetMaxChillStack()
    {
        float baseMax = 20f;
        if (Inven == null) return baseMax;

        bool hasFlower = Inven.HasUniqueEffect(GemUniqueType.SlowlyFreezingFlower);
        return baseMax + GemUniqueLogic.GetSlowlyFreezingFlowerMaxBonus(hasFlower);
    }

    public static bool ShouldBlockChill(bool isFrozen)
    {
        if (Inven == null) return false;
        return GemUniqueLogic.ShouldBlockChillStack(Inven.HasUniqueEffect(GemUniqueType.AchingBones), isFrozen);
    }

    public static float GetFreezeRefundStacks()
    {
        if (Inven == null) return 10f; // 기본값
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Chill));
        return 10f + GemSynergyLogic.GetChillRefundAmount(level);
    }

    public static bool HasFreezeFixedDamage()
    {
        if (Inven == null) return false;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Chill));
        return GemSynergyLogic.HasChillFreezeDamage(level);
    }

    #endregion

    #region BloodPop Rules

    public static float GetBloodPopDamage(int stacks)
    {
        float damage = stacks;
        if (Inven == null) return damage;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.BloodPop));
        return damage + GemSynergyLogic.GetBloodPopDamageBonus(level);
    }

    public static float GetBloodPopRadiusMultiplier()
    {
        if (Inven == null) return 1.0f;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.BloodPop));
        return GemSynergyLogic.GetBloodPopRadiusMultiplier(level);
    }

    public static float GetBloodPopChainRatio()
    {
        if (Inven == null) return 0f;
        return GemUniqueLogic.GetExplodingFleshStackRatio(Inven.HasUniqueEffect(GemUniqueType.ExplodingFlesh));
    }

    #endregion

    #region Aging Rules

    public static float GetAgingValuePerStack()
    {
        float baseValue = 0.01f;
        if (Inven == null) return baseValue;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Aging));
        return baseValue * GemSynergyLogic.GetAgingValueMultiplier(level);
    }

    public static float GetMaxAgingStack()
    {
        if (Inven == null) return 25f;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Aging));
        bool hasNoCountry = Inven.HasUniqueEffect(GemUniqueType.NoCountryForOldMen);

        float max = GemUniqueLogic.GetNoCountryMaxStack(hasNoCountry);
        max += GemSynergyLogic.GetAgingMaxStackBonus(level);

        return max;
    }

    public static bool ShouldAgingInstaKill(float currentStacks)
    {
        if (Inven == null) return false;
        return GemUniqueLogic.ShouldAgingInstaKill(Inven.HasUniqueEffect(GemUniqueType.NoCountryForOldMen), currentStacks);
    }

    #endregion

    #region Corrosion Rules

    public static float GetCorrosionDamageAmp()
    {
        if (Inven == null) return 0f;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Corrosion));
        return GemSynergyLogic.GetCorrosionDamageAmp(level);
    }

    #endregion
}

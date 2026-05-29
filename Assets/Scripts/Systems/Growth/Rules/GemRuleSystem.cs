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

    // [수정] 한기 스택에 따른 구간별 감속 비율 계산
    public static float GetChillSlowReduction(int currentStacks, bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget || currentStacks <= 0) return 0f;

        // 4세트 보너스
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Priest_Chill));
        float bonus = GemSynergyLogic.GetChillSlowBonus(level);

        float baseReduction = 0f;
        if (currentStacks >= 76) baseReduction = 0.25f;
        else if (currentStacks >= 51) baseReduction = 0.20f;
        else if (currentStacks >= 26) baseReduction = 0.10f;
        else if (currentStacks >= 1) baseReduction = 0.05f;

        return baseReduction + bonus;
    }

    public static float GetMaxChillStack(bool isEnemyTarget)
    {
        float baseMax = 100f; // [수정] 한기 최대 스택 100 고정
        if (Inven == null || !isEnemyTarget) return baseMax;

        bool hasFlower = Inven.HasUniqueEffect(GemUniqueType.SlowlyFreezingFlower);
        return baseMax + GemUniqueLogic.GetSlowlyFreezingFlowerMaxBonus(hasFlower);
    }

    public static float GetChillFreezeDamagePercentage(bool isBoss)
    {
        return isBoss ? 0.04f : 0.08f; // 보스 4%, 일반 8%
    }


    public static bool ShouldBlockChill(bool isFrozen, bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return false;
        return GemUniqueLogic.ShouldBlockChillStack(Inven.HasUniqueEffect(GemUniqueType.AchingBones), isFrozen);
    }

    public static float GetFreezeRefundStacks(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return 0f; 
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Priest_Chill));
        return GemSynergyLogic.GetChillRefundAmount(level);
    }

    public static bool HasFreezeFixedDamage(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return false;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Priest_Chill));
        return GemSynergyLogic.HasChillFreezeDamage(level);
    }

    #endregion

    #region BloodPop Rules

    public static float GetBloodPopDamage(int currentStacks, bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return currentStacks;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.BloodPop));
        float ratio = GemSynergyLogic.GetBloodPopDamageRatio(level);
        return currentStacks * ratio;
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

    // [수정] 노화 구간별 감속 비율 계산
    public static float GetAgingSlowReduction(int currentStacks, bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget || currentStacks <= 0) return 0f;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Priest_Aging));
        float bonus = GemSynergyLogic.GetAgingSlowBonus(level);

        float baseReduction = 0f;
        if (currentStacks >= 101) baseReduction = 0.25f;
        else if (currentStacks >= 81) baseReduction = 0.20f;
        else if (currentStacks >= 61) baseReduction = 0.16f;
        else if (currentStacks >= 41) baseReduction = 0.12f;
        else if (currentStacks >= 21) baseReduction = 0.08f;
        else if (currentStacks >= 1) baseReduction = 0.04f;

        return baseReduction + bonus;
    }

    public static float GetMaxAgingStack(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return 100f;

        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Priest_Aging));
        float baseMax = GemSynergyLogic.GetAgingMaxStack(level); // 100 or 120

        bool hasNoCountry = Inven.HasUniqueEffect(GemUniqueType.NoCountryForOldMen);
        return baseMax + GemUniqueLogic.GetNoCountryMaxStack(hasNoCountry);
    }

    public static float GetSenilityDamageAmp(bool isEnemyTarget)
    {
        if (Inven == null || !isEnemyTarget) return 0f;
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Priest_Aging));
        return GemSynergyLogic.GetSenilityDamageAmp(level);
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
        int level = GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Priest_Corrosion));
        return GemSynergyLogic.GetCorrosionDamageAmp(level);
    }

    #endregion
}

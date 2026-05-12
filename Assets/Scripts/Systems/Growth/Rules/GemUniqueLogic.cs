using UnityEngine;

/// <summary>
/// 유니크 보석(Enum) 활성화에 따른 특수 변조 로직을 전담합니다.
/// </summary>
public static class GemUniqueLogic
{
    // --- Poison ---
    public static float GetLethalDoseIntervalMultiplier(bool active) => active ? 0.5f : 1.0f;
    
    public static float GetLethalPoisonBonus(bool active, int currentStacks) 
    {
        return active ? currentStacks : 0;
    }

    // --- Chill ---
    public static bool ShouldBlockChillStack(bool achingBonesActive, bool isFrozen)
    {
        return achingBonesActive && isFrozen;
    }

    public static float GetSlowlyFreezingFlowerMaxBonus(bool active) => active ? 10.0f : 0f;

    // --- Aging ---
    public static float GetNoCountryMaxStack(bool active) => active ? 100f : 25f;
    public static bool ShouldAgingInstaKill(bool active, float currentStacks)
    {
        return active && currentStacks >= 100f;
    }

    // --- BloodPop ---
    public static float GetExplodingFleshStackRatio(bool active) => active ? 0.25f : 0f;
}

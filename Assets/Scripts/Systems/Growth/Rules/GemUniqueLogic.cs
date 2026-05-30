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

    public static float GetSlowlyFreezingFlowerMaxBonus(int count) => count * 10.0f;

    // --- Aging ---
    public static float GetNoCountryMaxStack(int count) => count > 0 ? (25f + count * 100f) : 25f; // Wait, wait. "Aging max stacks +100".
    public static bool ShouldAgingInstaKill(int count, float currentStacks)
    {
        return count > 0 && currentStacks >= 100f;
    }

    // --- BloodPop ---
    public static float GetExplodingFleshStackRatio(bool active) => active ? 0.25f : 0f;
}

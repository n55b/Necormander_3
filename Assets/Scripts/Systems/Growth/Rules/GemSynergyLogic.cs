using UnityEngine;

/// <summary>
/// 시너지 단계(연결된 보석 개수)에 따른 수치 계산을 전담합니다.
/// </summary>
public static class GemSynergyLogic
{
    // [수정] 기획표에 따라 2, 4, 6, 8 시너지를 레벨로 매핑합니다.
    public static int GetLevel(int count)
    {
        if (count >= 8) return 4;
        if (count >= 6) return 3;
        if (count >= 4) return 2;
        if (count >= 2) return 1;
        return 0;
    }

    // --- Poison ---
    public static float GetPoisonDurationBonus(int level) => (level >= 2) ? 5.0f : 0f;
    public static float GetPoisonExtraStack(int level) => (level >= 3) ? 1.0f : 0f;
    public static float GetPoisonIntervalMultiplier(int level) => (level >= 4) ? 0.6f : 1.0f; // [보정] 기본 5초에 0.6을 곱하여 3초로 단축

    // --- Chill ---
    public static float GetChillValueMultiplier(int level) => (level >= 2) ? 1.25f : 1.0f;
    public static float GetChillRefundAmount(int level) => (level >= 3) ? 5.0f : 0f; // [보정] 20스택의 25%인 5스택 환급
    public static bool HasChillFreezeDamage(int level) => level >= 4;

    // --- BloodPop ---
    public static float GetBloodPopDamageBonus(int level) => (level >= 2) ? 10.0f : 0f;
    public static float GetBloodPopRadiusMultiplier(int level) => (level >= 3) ? 1.5f : 1.0f;

    // --- Aging ---
    public static float GetAgingValueMultiplier(int level) => (level >= 2) ? 1.25f : 1.0f;
    public static float GetAgingMaxStackBonus(int level) => (level >= 3) ? 15.0f : 0f;

    // --- Corrosion ---
    public static float GetCorrosionDamageAmp(int level)
    {
        if (level >= 2) return 0.40f;
        if (level >= 1) return 0.25f;
        return 0f;
    }
}

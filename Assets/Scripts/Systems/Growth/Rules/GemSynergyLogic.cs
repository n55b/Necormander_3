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
    // [수정] 한기 시너지
    public static float GetChillSlowBonus(int level) => (level >= 2) ? 0.05f : 0f; // 4세트: 각 구역마다 감속량 5% 추가
    public static float GetChillRefundAmount(int level) => (level >= 3) ? 25.0f : 0f; // 6세트: 동결 후 스택 25 반환
    public static bool HasChillFreezeDamage(int level) => level >= 4; // 8세트: 동결 시 체력 비례 고정 피해

    // --- BloodPop ---
    // [수정] 비폭 시너지: 4세트 시 데미지 배율 0.5 (기본 0.4)
    public static float GetBloodPopDamageRatio(int level) => (level >= 2) ? 0.5f : 0.4f;
    public static float GetBloodPopRadiusMultiplier(int level) => (level >= 3) ? 1.5f : 1.0f;

    // --- Aging ---
    // [수정] 노화 시너지
    public static float GetAgingSlowBonus(int level) => (level >= 2) ? 0.05f : 0f; // 4세트: 각 구역마다 감속량 5% 추가
    public static float GetAgingMaxStack(int level) => (level >= 3) ? 120.0f : 100.0f; // 6세트: 최대 상한선 120으로 증가
    public static float GetSenilityDamageAmp(int level) => (level >= 3) ? 0.12f : 0.08f; // 노쇠 상태일 때 받는 피해 증폭량

    // --- Corrosion ---
    public static float GetCorrosionDamageAmp(int level)
    {
        if (level >= 2) return 0.40f;
        if (level >= 1) return 0.25f;
        return 0f;
    }
}

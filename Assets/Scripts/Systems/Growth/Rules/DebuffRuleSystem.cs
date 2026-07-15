/// <summary>
/// 스택형 디버프(한기/노화)의 기본 수치 테이블.
///
/// 구 GemRuleSystem에서 분리해 나왔습니다. 그 파일은 이름과 달리 보석 규칙이 하나도 살아있지
/// 않았고(모든 보석 질의가 level=0 / false 로 스텁), 실제로 동작하던 것은 아래 테이블뿐이었습니다.
/// 이 테이블을 먹이는 것도 보석이 아니라 플레이어 스킬 SO들입니다
/// (PlayerFracturePunchSO / PlayerCorrosionPunchSO / PlayerDebuffSO).
/// 제거된 보석 보너스의 스펙은 GEM_LEGACY.md 참조.
/// </summary>
public static class DebuffRuleSystem
{
    #region Chill Rules

    /// <summary>
    /// 한기 스택 구간별 감속 비율.
    /// [주의] 호출부는 DebuffStackType.Fracture(골절) 스택을 넘깁니다. (레거시 네이밍 미스매치)
    /// </summary>
    public static float GetChillSlowReduction(int currentStacks, bool isEnemyTarget)
    {
        if (!isEnemyTarget || currentStacks <= 0) return 0f;

        if (currentStacks >= 76) return 0.25f;
        if (currentStacks >= 51) return 0.20f;
        if (currentStacks >= 26) return 0.10f;
        return 0.05f; // 1 ~ 25
    }

    #endregion

    #region Aging Rules

    /// <summary>
    /// 노화 스택 구간별 감속 비율.
    /// [주의] 호출부는 DebuffStackType.Corrosion(부식) 스택을 넘깁니다. (레거시 네이밍 미스매치)
    /// </summary>
    public static float GetAgingSlowReduction(int currentStacks, bool isEnemyTarget)
    {
        if (!isEnemyTarget || currentStacks <= 0) return 0f;

        if (currentStacks >= 101) return 0.25f;
        if (currentStacks >= 81) return 0.20f;
        if (currentStacks >= 61) return 0.16f;
        if (currentStacks >= 41) return 0.12f;
        if (currentStacks >= 21) return 0.08f;
        return 0.04f; // 1 ~ 20
    }

    #endregion
}

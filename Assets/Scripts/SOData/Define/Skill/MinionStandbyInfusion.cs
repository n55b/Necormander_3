using UnityEngine;

/// <summary>
/// 소환수가 필드에 나와 있지 않을 때(대기 슬롯) 플레이어 평타에 묻어나는 효과.
///
/// 소환수 2마리를 장착하면 필드에는 활성 소환수만 나온다. 이때 대기 중인 소환수의 이 효과가
/// 플레이어 평타(1·2·3타)에 확률로 붙어 상태이상(빙결·출혈·취약 등)을 건다.
/// 상태이상 지속시간/수치는 StatusBalance 공통 값을 따르고, duration 을 넣으면 그 값으로 덮는다.
/// 소환수 하나가 여러 효과를 가질 수 있고(MainMinionDataSO.standbyInfusions), 효과마다 확률을 따로 추첨한다.
/// </summary>
[System.Serializable]
public class MinionStandbyInfusion
{
    public enum SwingFilter
    {
        [InspectorName("모든 타")] AllSwings = 0,
        [InspectorName("3타만")] FinalSwingOnly = 1,
    }

    [Tooltip("대기 중일 때 플레이어 평타에 거는 상태이상. 없음이면 효과 없음.")]
    public StatusType status = StatusType.None;

    [Range(0f, 1f), Tooltip("평타 한 번(휘두르기 1회)마다 효과가 붙을 확률. 1 = 항상.")]
    public float chance = 0.5f;

    [Tooltip("어느 타에 붙일지.")]
    public SwingFilter swings = SwingFilter.AllSwings;

    [Min(0f), Tooltip("상태이상 지속시간(초). 0 이면 상태이상 공통 수치(StatusBalance)의 기본 지속시간.")]
    public float duration = 0f;

    public bool IsActive => status != StatusType.None && chance > 0f;

    public bool AppliesToSwing(int swingIndex) => swings == SwingFilter.AllSwings || swingIndex == 2;

    public static string StatusLabel(StatusType s)
    {
        switch (s)
        {
            case StatusType.Stun: return Loc.T("MinionStandbyInfusion.stun", "기절");
            case StatusType.Freeze: return Loc.T("MinionStandbyInfusion.freeze", "빙결");
            case StatusType.Bleed: return Loc.T("MinionStandbyInfusion.bleed", "출혈");
            case StatusType.Poison: return Loc.T("MinionStandbyInfusion.poison", "중독");
            case StatusType.BloodPop: return Loc.T("MinionStandbyInfusion.blood_pop", "혈액 튀어오름");
            case StatusType.Hitstun: return Loc.T("MinionStandbyInfusion.hitstun", "경직");
            case StatusType.Vulnerable: return Loc.T("MinionStandbyInfusion.vulnerable", "취약");
            default: return Loc.T("MinionStandbyInfusion.none", "없음");
        }
    }

    /// <summary>효과 하나의 짧은 설명. 예) "평타 50% 빙결"</summary>
    public string DescribeShort()
    {
        if (!IsActive) return Loc.T("MinionStandbyInfusion.none", "없음");
        string when = swings == SwingFilter.FinalSwingOnly ? Loc.T("MinionStandbyInfusion.final_swing", "평타 3타") : Loc.T("MinionStandbyInfusion.basic_attack", "평타");
        string dur = duration > 0f ? Loc.F("MinionStandbyInfusion.duration", " {0:0.##}초", duration) : "";
        return Loc.F("MinionStandbyInfusion.short", "{0} {1:0}% {2}{3}", when, chance * 100f, StatusLabel(status), dur);
    }

    public string Describe() => IsActive ? Loc.F("MinionStandbyInfusion.standby", "대기 중일 때 {0}.", DescribeShort()) : Loc.T("MinionStandbyInfusion.no_standby", "대기 효과 없음.");

    /// <summary>여러 효과를 한 줄로. 예) "대기 중일 때 평타 50% 빙결, 평타 3타 20% 취약."</summary>
    public static string DescribeAll(MinionStandbyInfusion[] list)
    {
        if (list == null) return Loc.T("MinionStandbyInfusion.no_standby", "대기 효과 없음.");
        var parts = new System.Collections.Generic.List<string>();
        foreach (var e in list) if (e != null && e.IsActive) parts.Add(e.DescribeShort());
        return parts.Count == 0 ? Loc.T("MinionStandbyInfusion.no_standby", "대기 효과 없음.") : Loc.F("MinionStandbyInfusion.standby", "대기 중일 때 {0}.", string.Join(", ", parts));
    }

    public static bool AnyActive(MinionStandbyInfusion[] list)
    {
        if (list == null) return false;
        foreach (var e in list) if (e != null && e.IsActive) return true;
        return false;
    }
}

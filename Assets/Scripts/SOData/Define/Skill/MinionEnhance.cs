using UnityEngine;

/// <summary>
/// 메인 소환수 강화 한 단계(보상방에서 1회 = 1단계). 값은 '기본 대비 총 배율'이다(누적 곱 아님).
/// 지금은 최대 1단계(MAX_LEVEL)라 사실상 칸 하나만 쓴다.
/// 그래야 기획자가 표만 보고 최종 수치를 바로 읽을 수 있다.
///
/// [확장 예정] 특수 능력/확률 상승은 여기에 필드를 추가하고, 읽는 쪽은 <see cref="MinionEnhance"/>
/// 를 통해서만 꺼내 쓰게 한다. 호출부(평타/대쉬/스킬)가 단계 배열을 직접 뒤지지 않게 하려는 것.
/// </summary>
[System.Serializable]
public class MinionEnhanceStep
{
    [Tooltip("보상방 강화 카드에 표시할 설명. 비우면 배율로 자동 생성한다.")]
    [TextArea] public string description;

    [Tooltip("일반 공격(평타 첫 타의 소환수 일격) 피해 총 배율. 1 = 강화 없음.")]
    public float basicAttackMultiplier = 1f;

    [Tooltip("대쉬 피해 총 배율. 1 = 강화 없음.")]
    public float dashDamageMultiplier = 1f;

    [Tooltip("스페이스바 액티브 스킬 피해 총 배율. 1 = 강화 없음.")]
    public float skillDamageMultiplier = 1f;

    public MinionEnhanceStep() { }

    public MinionEnhanceStep(float basic, float dash, float skill)
    {
        basicAttackMultiplier = basic;
        dashDamageMultiplier = dash;
        skillDamageMultiplier = skill;
    }

    /// <summary>새 에셋/기존 에셋 모두 이 값으로 시작한다(필드가 직렬화돼 있지 않으면 이니셜라이저가 남는다).</summary>
    public static MinionEnhanceStep[] DefaultSteps() => new[]
    {
        new MinionEnhanceStep(1.20f, 1.20f, 1.20f),
    };
}

/// <summary>
/// 현재 장착된 메인 소환수의 강화 단계를 읽어 배율을 돌려주는 단일 창구.
/// 단계 자체는 InventoryManager 가 런 단위로 들고 있고(세이브 포함), 수치표는 MainMinionDataSO 가 갖는다.
/// </summary>
public static class MinionEnhance
{
    /// <summary>보상방 강화 최대 횟수.</summary>
    /// [26/10/03] 3 → 1 로 축소. 에셋의 enhanceSteps 가 더 길어도 이 값에서 잘린다.
    public const int MAX_LEVEL = 1;

    /// <summary>현재 메인 소환수 강화 단계(0 = 강화 안 함).</summary>
    public static int CurrentLevel
        => InventoryManager.Instance != null ? InventoryManager.Instance.MainSummonEnhanceLevel : 0;

    /// <summary>이 소환수가 지금 장착된 그 소환수일 때만 단계를 적용한다(다른 데이터로 계산하는 경로 방어).</summary>
    private static MinionEnhanceStep CurrentStep(MainMinionDataSO main)
    {
        if (main == null) return null;
        var inv = InventoryManager.Instance;
        if (inv == null || inv.MainSummon != main) return null;
        return main.GetEnhanceStep(inv.MainSummonEnhanceLevel);
    }

    public static float BasicAttackMult(MainMinionDataSO main) => CurrentStep(main)?.basicAttackMultiplier ?? 1f;
    public static float DashDamageMult(MainMinionDataSO main) => CurrentStep(main)?.dashDamageMultiplier ?? 1f;
    public static float SkillDamageMult(MainMinionDataSO main) => CurrentStep(main)?.skillDamageMultiplier ?? 1f;

    /// <summary>강화 카드 설명. 단계 설명이 있으면 그걸, 없으면 '현재 → 다음' 배율을 만든다.</summary>
    public static string DescribeNext(MainMinionDataSO main, int currentLevel)
    {
        if (main == null) return "";
        var next = main.GetEnhanceStep(currentLevel + 1);
        if (next == null) return "더 이상 강화할 수 없습니다.";
        if (!string.IsNullOrEmpty(next.description)) return next.description;

        var cur = main.GetEnhanceStep(currentLevel);
        float cb = cur?.basicAttackMultiplier ?? 1f, cd = cur?.dashDamageMultiplier ?? 1f, cs = cur?.skillDamageMultiplier ?? 1f;
        return $"일반 공격 피해 x{cb:0.##} → x{next.basicAttackMultiplier:0.##}\n" +
               $"대쉬 피해 x{cd:0.##} → x{next.dashDamageMultiplier:0.##}\n" +
               $"스킬 피해 x{cs:0.##} → x{next.skillDamageMultiplier:0.##}";
    }
}

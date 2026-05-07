using UnityEngine;

public enum StatType
{
    Attack,         // 공격력 강화 (배율: 0.1 = 10% 증가)
    Health,         // 체력 강화 (배율: 0.1 = 10% 증가)
    AttackSpeed,    // 공격 속도 강화 (배율: 1.0 = 공격 빈도 100% 증가)
    RespawnTime,    // 부활 시간 단축 (고정치: 1.0 = 1초 단축)
    ThrowEffect     // 던지기 능력 강화 (전사:데미지+, 궁수:범위+, 법사:횟수+, 기타:배율+)
}

/// <summary>
/// 보석의 최상위 기반 클래스입니다.
/// </summary>
public abstract class GemSO : GrowthItemSO
{
    [Header("직업 타겟팅")]
    [Tooltip("이 보석을 장착할 수 있는 직업들을 선택하세요.")]
    public MinionJobFlags eligibleJobs = MinionJobFlags.All;

    public bool IsEligible(CommandData job)
    {
        if (eligibleJobs == MinionJobFlags.None) return false;
        if (eligibleJobs == MinionJobFlags.All) return true;
        int jobBit = 1 << (int)job;
        return ((int)eligibleJobs & jobBit) != 0;
    }

    public abstract GrowthItemData GetDynamicDisplayData(CommandData job);
}

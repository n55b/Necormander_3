using UnityEngine;

public enum StatType
{
    Attack,         // 공격력 강화 (배율: 0.1 = 10% 증가)
    Health,         // 체력 강화 (배율: 0.1 = 10% 증가)
    AttackSpeed,    // 공격 속도 강화 (배율: 1.0 = 공격 빈도 100% 증가)
    RespawnTime,    // 부활 시간 단축 (고정치: 1.0 = 1초 단축)
    ThrowEffect,    // 던지기 능력 강화 (전사:데미지+, 궁수:범위+, 법사:횟수+, 기타:배율+)
    Debuff          // [추가] 던지기 시 적에게 상태 이상 부여
}

/// <summary>
/// 특정 직업군 또는 전체 직업군을 강화하는 보석 데이터입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewGem", menuName = "Necromancer/Growth/Gem")]
public class GemSO : GrowthItemSO
{
    [Header("직업 타겟팅")]
    [Tooltip("이 보석을 장착할 수 있는 직업들을 선택하세요.")]
    public MinionJobFlags eligibleJobs = MinionJobFlags.All;
    
    [Header("강화 수치")]
    public StatType statType;
    public float baseBonusValue;

    [Header("디버프 설정 (Debuff 타입일 때만)")]
    public DebuffStackType targetDebuffType;
    public float baseDebuffStack = 1.0f;

    /// <summary>
    /// 특정 직업이 이 보석을 사용할 수 있는지 확인합니다.
    /// </summary>
    public bool IsEligible(CommandData job)
    {
        if (eligibleJobs == MinionJobFlags.None) return false;
        if (eligibleJobs == MinionJobFlags.All) return true;

        // CommandData를 플래그 비트로 변환하여 대조
        int jobBit = 1 << (int)job;
        return ((int)eligibleJobs & jobBit) != 0;
    }

    /// <summary>
    /// 특정 직업에 맞춰 수정된 아이템 데이터를 반환합니다.
    /// </summary>
    public GrowthItemData GetDynamicDisplayData(CommandData job)
    {
        string jobName = job.ToString().Replace("Skeleton", "");
        
        string bonusInfo = "";
        if (statType == StatType.Debuff)
        {
            bonusInfo = $"Applies {baseDebuffStack} stacks of {targetDebuffType}";
        }
        else
        {
            bonusInfo = $"Enhances {jobName}'s {GetStatName()} by {baseBonusValue * 100}%";
        }
        
        string finalDesc = string.IsNullOrEmpty(this.description) ? bonusInfo : $"{this.description}\n({bonusInfo})";

        return new GrowthItemData {
            itemName = $"[{jobName}] {itemName}",
            description = finalDesc,
            icon = this.icon,
            rarity = this.rarity
        };
    }

    private string GetStatName()
    {
        switch (statType)
        {
            case StatType.Attack: return "Attack Damage";
            case StatType.Health: return "Max Health";
            case StatType.AttackSpeed: return "Attack Speed";
            case StatType.RespawnTime: return "Respawn Speed";
            case StatType.ThrowEffect: return "Throw Ability";
            case StatType.Debuff: return "Debuff Effect";
            default: return "Movement Speed";
        }
    }
}

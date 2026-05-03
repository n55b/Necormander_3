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
/// 특정 직업군 또는 전체 직업군을 강화하는 보석 데이터입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewGem", menuName = "Necromancer/Growth/Gem")]
public class GemSO : GrowthItemSO
{
    [Header("보석 설정")]
    public bool isUniversal = true; // true일 경우 모든 직업용 바리에이션 생성 가능
    
    [Tooltip("isUniversal이 false일 때만 사용됩니다.")]
    public CommandData targetJob; 
    
    [Header("강화 수치")]
    public StatType statType;
    public float baseBonusValue;

    [Header("도움말: 계산 가이드")]
    [TextArea(10, 20)]
#pragma warning disable 0414
    [SerializeField] private string gemCalculationGuide = 
        "[보석 타입별 계산 방식]\n" +
        "▶ 공격력 (Attack): 배율 가산 (예: 0.1 입력 시 10% 증가)\n" +
        "▶ 체력 (Health): 배율 가산 (예: 0.2 입력 시 20% 증가)\n" +
        "▶ 공격 속도 (AttackSpeed): 배율 가산 (예: 1.0 입력 시 빈도 100% 증가)\n" +
        "▶ 부활 시간 (RespawnTime): 고정치 감산 (예: 2.0 입력 시 2초 단축)\n" +
        "▶ 투척 효과 (ThrowEffect): 아래 직업별 세부 규칙 참조\n\n" +
        "[ThrowEffect 직업별 세부 규칙]\n" +
        " * 전사 (Warrior): 데미지 고정치 추가 (예: 10.0 입력 시 데미지 +10)\n" +
        " * 궁수 (Archer): 폭발 반지름 고정치 추가 (예: 1.5 입력 시 반지름 +1.5m)\n" +
        " * 마법사 (Magician): 실행 횟수 추가 (예: 1.0 입력 시 +1회 추가)\n" +
        " * 기타 (사제/방패병 등): 효과 위력 배율 가산 (예: 0.5 입력 시 효과 50% 강화)";
#pragma warning restore 0414

    /// <summary>
    /// 특정 직업에 맞춰 수정된 아이템 데이터를 반환합니다.
    /// </summary>
    public GrowthItemData GetDynamicDisplayData(CommandData job)
    {
        string jobName = job.ToString().Replace("Skeleton", "");
        return new GrowthItemData {
            itemName = $"[{jobName}] {itemName}",
            description = $"{jobName}의 {GetStatName()} 효과를 {baseBonusValue * 100}%만큼 강화합니다.",
            icon = this.icon,
            rarity = this.rarity
        };
    }

    private string GetStatName()
    {
        switch (statType)
        {
            case StatType.Attack: return "공격력";
            case StatType.Health: return "최대 체력";
            case StatType.AttackSpeed: return "공격 속도";
            case StatType.RespawnTime: return "부활 시간";
            case StatType.ThrowEffect: return "투척 능력";
            default: return "이동 속도";
        }
    }
}

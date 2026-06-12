using UnityEngine;

/// <summary>
/// 미니언(아군/적군 공용)의 마스터 데이터입니다. 
/// 스탯과 고유 공격 패턴(Attack State)을 정의합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewMinionData", menuName = "Necromancer/Data/MinionData")]
public class MinionDataSO : ScriptableObject
{
    [Header("Skill Information")]
    public Necromancer.Skills.PlayerSkillSO playerSkill;
    public Necromancer.Skills.MinionSkillSO minionSkill;

    [Header("Basic Information")]
    public CommandData minionType;
    public string minionName;
    public Sprite minionIcon;   // 대가리만 달린 이미지

    [Header("투척 효과 설정")]
    public float baseEffectValue; // 전사: 추가 데미지, 궁수: 범위, 사제/방패/창병: 고유 효과(CC/쉴드/넉백) 수치

    [Header("기본 능력치")]
    public float maxHP = 100f;
    public float attack = 10f;
    public float attackSpeed = 1f;
    public float attackRange = 2f;
    public float detectRange = 10f;
    public float defense = 0f; // 일반 방어력 (퍼센트 데미지 감소)
    public float flatDefense = 0f; // 고정 수치 방어력 (고정 데미지 차감)
    public float moveSpeed = 5f;
    [Range(0f, 1f)] public float baseEvasion = 0f; // 회피율 (0~1)
    [Range(0f, 1f)] public float baseMissChance = 0f; // 공격 시 기본 미스 확률 (0~1)
    public int cost;
    [UnityEngine.Serialization.FormerlySerializedAs("isBoss")]
    public bool isElite; // [추가] 엘리트 유닛 여부
    public bool canSpawnRandomly = true; // [추가] 일반 방에서 랜덤하게 스폰될 수 있는지 여부

    [Header("AI 행동 패턴")]
    public AIPatternSO aiPattern; // 이 유닛의 전체적인 AI 행동 (대기/추격/공격 통합)

    [Header("특수 투척 설정")]
    [Range(0f, 1f), Tooltip("투척 1회당 소모할 최대 체력 비율 (1.0 = 100%)")]
    public float hpCostRatioPerThrow = 0f;

    [Header("사운드 설정")]
    public AudioClip AttackSound;
    public AudioClip DeathSound;

    [Header("프리팹 설정")]
    public GameObject minionPrefab;
}

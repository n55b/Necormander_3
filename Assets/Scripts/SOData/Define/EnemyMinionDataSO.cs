using UnityEngine;

/// <summary>
/// 필드에 실제로 스폰되는 적/엘리트/보스의 마스터 데이터.
/// 소환수와 달리 전투 스탯과 AI 패턴, 프리팹이 필요하다.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyMinion", menuName = "Necromancer/Data/Enemy Minion")]
public class EnemyMinionDataSO : MinionDataSO
{
    [Header("투척 효과 설정")]
    public float baseEffectValue; // 전사: 추가 데미지, 궁수: 범위, 사제/방패/창병: 고유 효과(CC/쉴드/넉백) 수치

    [Header("기본 능력치")]
    [Tooltip("공격력. 소환수와 달리 적은 이 값을 CharacterStat 이 그대로 쓴다.")]
    public float attack = 10f;
    public float maxHP = 100f;
    [Tooltip("공격 속도(회/초). 높을수록 빠르다. [26/07/17] 예전엔 '간격(초)'이라 낮을수록 빨랐다 — 뒤집혔으니 주의.")]
    public float attackSpeed = 1f;
    public float attackRange = 2f;
    public float detectRange = 10f;
    [Tooltip("방어력(%). 그 자체가 감소율이다. 20 = 20% 감소. 감소율은 75%에서 잘린다.")]
    public float defense = 0f;
    public float moveSpeed = 5f;
    [Tooltip("회피율(0~1). 기본 0 = 절대 회피 안 함.")]
    [Range(0f, 1f)] public float baseEvasion = 0f;
    [Tooltip("적중률(0~1). 기본 1 = 무조건 맞힘.")]
    [Range(0f, 1f)] public float baseAccuracy = 1f;
    [Tooltip("이 유닛의 공격 속성. 마법 적도 데미지는 attack 으로 계산하고 태그만 다르다.\n" +
             "유닛 단위라 한 유닛이 물리/마법을 섞을 수는 없다 — 필요해지면 AI 패턴별 오버라이드를 열면 된다.")]
    public AttackType attackType = AttackType.Physical;
    [UnityEngine.Serialization.FormerlySerializedAs("isBoss")]
    public bool isElite; // [추가] 엘리트 유닛 여부

    [Header("AI 행동 패턴")]
    public AIPatternSO aiPattern; // 이 유닛의 전체적인 AI 행동 (대기/추격/공격 통합)

    [Header("사운드 설정")]
    public AudioClip AttackSound;

    [Header("프리팹 설정")]
    public GameObject minionPrefab;
}

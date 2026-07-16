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
    public float maxHP = 100f;
    public float attackSpeed = 1f;
    public float attackRange = 2f;
    public float detectRange = 10f;
    public float defense = 0f; // 일반 방어력 (퍼센트 데미지 감소)
    public float flatDefense = 0f; // 고정 수치 방어력 (고정 데미지 차감)
    public float moveSpeed = 5f;
    [Range(0f, 1f)] public float baseEvasion = 0f; // 회피율 (0~1)
    [Range(0f, 1f)] public float baseMissChance = 0f; // 공격 시 기본 미스 확률 (0~1)
    [UnityEngine.Serialization.FormerlySerializedAs("isBoss")]
    public bool isElite; // [추가] 엘리트 유닛 여부

    [Header("AI 행동 패턴")]
    public AIPatternSO aiPattern; // 이 유닛의 전체적인 AI 행동 (대기/추격/공격 통합)

    [Header("사운드 설정")]
    public AudioClip AttackSound;

    [Header("프리팹 설정")]
    public GameObject minionPrefab;
}

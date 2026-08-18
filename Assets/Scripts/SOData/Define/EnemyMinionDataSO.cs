using UnityEngine;

/// <summary>
/// 유닛 등급. 피해 갈래(<see cref="DamageCategory"/>)·엘리트 방 풀·미니맵 마커가 전부 이 값 하나에서 온다.
///
/// [26/08/18] 예전엔 bool isElite 였고 보스는 프리팹 태그 "Boss" 또는 <b>이름에 "Boss" 포함</b>으로
/// 판정했다. 그래서 두 가지가 동시에 깨져 있었다:
///  · 'Summoner Boss Minions'(사실상 일반 몹)가 이름 때문에 보스로 잡혀 <b>카운터로 막을 수 없었다</b>.
///  · 엘리트 4종이 전부 Boss 태그를 달고 있어서 DamageCategory.EnemyElite 가 아예 도달 불가였다
///    (게다가 isElite 를 읽던 경로는 CharacterStatus 가 자식 오브젝트에 있어 항상 null 이었다).
/// 등급은 이제 데이터에서만 온다 — 데이터가 없는 유닛(보스가 소환한 잡몹 등)은 자동으로 Normal.
/// </summary>
public enum EnemyTier
{
    Normal = 0,
    Elite  = 1,
    Boss   = 2,
}

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
    [Tooltip("유닛 등급. 피해 갈래(EnemyMinion/EnemyElite/EnemyBoss)·엘리트 방 풀·미니맵 마커가 "
             + "이 값 하나만 본다. 프리팹 태그나 이름은 등급 판정에 쓰이지 않는다.")]
    public EnemyTier tier = EnemyTier.Normal;

    [Tooltip("페이즈 전환 전용 데이터인가. 켜면 등급은 그대로 유지되지만 스폰 풀에서만 빠진다. "
             + "체력이 바닥났을 때 스탯/AI 를 통째로 갈아끼우는 용도라(ArcherBossAIPatternSO.phase2Data, "
             + "BoneMasterController.phase2Data), 이게 층 엘리트로 뽑히면 2페이즈 몸으로 1페이즈를 시작한다.")]
    public bool isPhaseVariant = false;

    /// <summary>층 엘리트/엘리트 방에 실제로 뽑혀도 되는가. 등급이 Elite 이고 페이즈 전용이 아니어야 한다.
    /// 풀을 만드는 곳이 두 군데(EliteRoomEvent, MapGenerator)라 조건을 여기 한 곳에만 둔다.</summary>
    public bool IsSpawnableElite => tier == EnemyTier.Elite && !isPhaseVariant;

    [Header("AI 행동 패턴")]
    public AIPatternSO aiPattern; // 이 유닛의 전체적인 AI 행동 (대기/추격/공격 통합)

    [Header("사운드 설정")]
    public AudioClip AttackSound;

    [Header("프리팹 설정")]
    public GameObject minionPrefab;

    [Header("엘리트/보스 전용 방")]
    [Tooltip("이 엘리트가 나오는 방 프리팹. 맵 생성 때 이 층의 엘리트로 뽑히면 이 방이 배치된다.\n" +
             "비워두면 RoomPrefabData 에 등록된 일반 Elite 방으로 폴백한다. 일반 적은 안 쓰는 칸.")]
    public GameObject dedicatedRoomPrefab;
}

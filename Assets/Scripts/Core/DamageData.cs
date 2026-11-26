using UnityEngine;

/// <summary>
/// 공격의 속성. 물리는 ATK, 마법은 MAGIC 을 베이스로 피해를 낸다.
/// 상태이상 피해와는 직교한다 — 상태이상은 자기 DamageType 을 따로 갖는다.
/// 적은 magic 스탯 없이 atk 으로 계산하고 이 태그만 단다.
/// </summary>
public enum AttackType
{
    [UnityEngine.InspectorName("물리")] Physical,
    [UnityEngine.InspectorName("마법")] Magic
}

/// <summary>
/// [축1 = 속성] 피해의 종류. 이제 팝업 색/식별 전용이다 — 방무/크리/출혈 트리거 규칙은
/// 갈래(축2, DamageCategory)가 판정한다. Freeze/Poison/Bleed/BloodPop/Fixed 는 상태이상 팝업 색을 가른다.
/// </summary>
public enum DamageType
{
    [UnityEngine.InspectorName("물리")] Physical,   // ATK 기반 직접 피해
    [UnityEngine.InspectorName("마법")] Magic,      // MAGIC 기반 직접 피해
    [UnityEngine.InspectorName("빙결")] Freeze,     // 빙결 해제 시 터지는 고정 피해
    [UnityEngine.InspectorName("중독")] Poison,     // 중독 틱
    [UnityEngine.InspectorName("출혈")] Bleed,      // 출혈 틱 피해(공격력 비례, 취약 적용)
    [UnityEngine.InspectorName("혈액 튀어오름")] BloodPop,   // 비폭 10스택 폭발
    [UnityEngine.InspectorName("고정")] Fixed       // 어느 상태이상에도 속하지 않는 고정 피해
}

/// <summary>
/// [축2 = 갈래] 피해가 '어디서 나왔는가'. 속성(DamageType)과 직교하는 두 번째 축이다.
/// 유물/아이템 효과가 갈래별로 데미지를 증감할 때 이걸로 필터한다
/// (예: "평타 데미지 +10%" → 평타 갈래 전체에 적용).
///
/// 플레이어발(1~5)은 '행동'으로, 적발(7~10)은 '티어'로 나뉜다.
/// 적 티어(EnemyMinion/Elite/Boss)는 사이트마다 태그하지 않고 피격 시점에 공격자에서 자동 유도한다.
/// 값 순서는 직렬화에 영향 없으니(런타임 struct) 그냥 읽기 좋게 둔다.
/// </summary>
public enum DamageCategory
{
    [UnityEngine.InspectorName("없음")] None = 0,       // 미분류(기본값). 어떤 갈래 효과도 안 받는다.
    [UnityEngine.InspectorName("평타 공격")] BasicAttack,    // 평타 (플레이어 3타 + 소환수 마무리 일격)
    [UnityEngine.InspectorName("스킬")] Skill,          // 스킬 (Q/E + R 소환수 액티브)
    [UnityEngine.InspectorName("대시 공격")] DashAttack,     // 대쉬 공격
    [UnityEngine.InspectorName("패링")] Parry,          // 패링으로 반사한 투사체
    [UnityEngine.InspectorName("디버프")] Debuff,         // 아군발 상태이상 피해(중독/출혈/비폭/빙결해제 등)
    [UnityEngine.InspectorName("함정")] Trap,           // 함정/환경 피해
    [UnityEngine.InspectorName("적 소환수")] EnemyMinion,    // 적 일반
    [UnityEngine.InspectorName("적 엘리트")] EnemyElite,     // 적 엘리트
    [UnityEngine.InspectorName("적 보스")] EnemyBoss,      // 적 보스
    [UnityEngine.InspectorName("적 디버프")] EnemyDebuff     // 적발 상태이상 (지금은 없지만 예약)
}

[System.Serializable]
public struct DamageInfo
{
    private static long nextImpactId;
    [System.NonSerialized] public long impactId;
    [System.NonSerialized] public int impactPulse;
    [System.NonSerialized] public int pulseTargetCount;
    [System.NonSerialized] public float impactStopSeconds;
    [System.NonSerialized] public float? impactStrength;
    [System.NonSerialized] public int feedbackHitIndex;
    [System.NonSerialized] public bool feedbackComboFinish;
    [System.NonSerialized] public MinionSkillCaster minionSource;
    // 구조체 복사/다중 대상/추가 타격 모두 같은 토큰을 공유한다. 소환수 평타에는 넣지 않는다.
    [System.NonSerialized] public PlayerBasicAttackRoll playerBasicAttack;
    public float amount;
    public DamageType type;          // 축1: 속성
    public DamageCategory category;   // 축2: 갈래(유물 효과 필터용). None 이면 피격 시점에 자동 유도.
    public GameObject attacker;
    public float debuffMultiplier;
    public string popupText;
    public bool isRedirected;
    public bool causesHitstun;
    public float knockbackForce;
    public float superArmorDamage; // 슈퍼아머 깎는 수치

    // 타격 시 부여할 상태이상. nullable 이라 default(DamageInfo) 도 안전하게 '없음'(null)이다.
    // CharacterHealth 가 명중 확정 뒤 한 곳에서 건다(슈퍼아머는 BlockedBySuperArmor CC 를 막는다).
    public StatusType? applyStatus;
    public float statusDuration;   // 0 이면 StatusRules.DefaultDuration 을 쓴다.

    /// <summary>
    /// [축3 = 사거리] 원거리(투사체)에서 온 피해인가. 기본 false = 근접.
    ///
    /// 갈래(축2)로 표현할 수 없어서 별도 축으로 뒀다 — 적 피해의 갈래 칸은 이미 '티어'
    /// (EnemyMinion/Elite/Boss)가 쓰고 있고, 그것도 소스가 아니라 CharacterHealth 가 피격 순간에
    /// 자동으로 채운다. EnemyRanged 를 갈래에 넣으면 "보스가 쏜 화살"이 티어와 사거리 중 하나만
    /// 될 수 있어서 티어가 증발한다.
    ///
    /// 지금 이 값을 읽는 곳은 없다(옛 우클릭 카운터·가드가 근접만 받아칠 때 읽었다).
    /// 태그하는 쪽도 Projectile 뿐이라, 투사체가 아닌 적 피해(보스 장판 등)는 전부 '근접'이 된다.
    /// </summary>
    public bool isRanged;
    // 읽는 곳 없음, 호출부 호환용. AI 패턴·검사가 이름 있는 인자로 넘기므로 지우지 않는다(옛 가드 무시 표식).
    public bool bypassGuard;

    /// <summary>
    /// 피해가 '날아온 지점'(월드 좌표). 투사체만 채우고 나머지는 null 이다.
    ///
    /// attacker 로 대신할 수 없어서 따로 둔다 — 투사체는 attacker 에 '쏜 본체'를 싣기 때문에,
    /// 유도탄처럼 발사체와 시전자가 갈라지면 방향이 전혀 달라진다(마법사는 오른쪽에 있는데
    /// 파이어볼은 왼쪽에서 날아오는 상황). 피격 연출이 이 값으로 방향을 정한다.
    /// </summary>
    public Vector2? hitFrom;

    // 피해 갈래를 명시하는 호출은 category를 이름 있는 인자로 전달한다.
    public DamageInfo(float amount, DamageType type = DamageType.Physical, GameObject attacker = null, float debuffMultiplier = 1f, string popupText = "", bool isRedirected = false, bool causesHitstun = false, float knockbackForce = 0f, float superArmorDamage = 0f, DamageCategory category = DamageCategory.None, StatusType? applyStatus = null, float statusDuration = 0f, bool isRanged = false, Vector2? hitFrom = null, bool bypassGuard = false)
    {
        this.bypassGuard = bypassGuard;
        impactId = ++nextImpactId;
        impactPulse = 0;
        pulseTargetCount = 0;
        impactStopSeconds = 0;
        impactStrength = null;
        feedbackHitIndex = 0;
        feedbackComboFinish = false;
        minionSource = null;
        playerBasicAttack = null;
        this.isRanged = isRanged;
        this.hitFrom = hitFrom;
        this.amount = amount;
        this.type = type;
        this.category = category;
        this.attacker = attacker;
        this.debuffMultiplier = debuffMultiplier;
        this.popupText = popupText;
        this.isRedirected = isRedirected;
        this.causesHitstun = causesHitstun;
        this.knockbackForce = knockbackForce;
        this.superArmorDamage = superArmorDamage;
        this.applyStatus = applyStatus;
        this.statusDuration = statusDuration;
    }
}

/// <summary>플레이어 평타 한 번의 첫 실제 적중에서만 장비 확률을 추첨한다.</summary>
public sealed class PlayerBasicAttackRoll
{
    public readonly EquipmentSO Equipment;
    private bool _resolved;
    public PlayerBasicAttackRoll(EquipmentSO equipment) => Equipment = equipment;
    public bool TryResolve()
    {
        if (_resolved) return false;
        _resolved = true;
        return true;
    }
}

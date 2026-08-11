using UnityEngine;

/// <summary>
/// 공격의 속성. 물리는 ATK, 마법은 MAGIC 을 베이스로 피해를 낸다.
/// 상태이상 피해와는 직교한다 — 상태이상은 자기 DamageType 을 따로 갖는다.
/// 적은 magic 스탯 없이 atk 으로 계산하고 이 태그만 단다.
/// </summary>
public enum AttackType
{
    Physical,
    Magic
}

/// <summary>
/// [축1 = 속성] 피해의 종류. 이제 팝업 색/식별 전용이다 — 방무/크리/출혈 트리거 규칙은
/// 갈래(축2, DamageCategory)가 판정한다. Freeze/Poison/Bleed/BloodPop/Fixed 는 상태이상 팝업 색을 가른다.
/// </summary>
public enum DamageType
{
    Physical,   // ATK 기반 직접 피해
    Magic,      // MAGIC 기반 직접 피해
    Freeze,     // 빙결 해제 시 터지는 고정 피해
    Poison,     // 중독 틱
    Bleed,      // 출혈이 피격에 얹는 추가 고정 피해
    BloodPop,   // 비폭 10스택 폭발
    Fixed       // 어느 상태이상에도 속하지 않는 고정 피해
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
    None = 0,       // 미분류(기본값). 어떤 갈래 효과도 안 받는다.
    BasicAttack,    // 평타 (플레이어 3타 + 소환수 마무리 일격)
    Skill,          // 스킬 (Q/E + R 소환수 액티브)
    DashAttack,     // 대쉬 공격
    Parry,          // 패링으로 반사한 투사체
    Debuff,         // 아군발 상태이상 피해(중독/출혈/비폭/빙결해제 등)
    Trap,           // 함정/환경 피해
    EnemyMinion,    // 적 일반
    EnemyElite,     // 적 엘리트
    EnemyBoss,      // 적 보스
    EnemyDebuff     // 적발 상태이상 (지금은 없지만 예약)
}

/// <summary>
/// 상태이상 피해의 철칙을 한곳에 모아둔다. 여기저기 흩어놓으면 반드시 어긋난다.
///
/// 특히 '출혈을 트리거하지 않는다'가 중요하다 — 출혈은 "피격할 때마다 +2 고정 피해"인데,
/// 그 2 자체가 피해라서 스스로를 다시 트리거하면 무한 재귀로 스택 오버플로우가 난다.
/// 같은 이유로 비폭 폭발이 비폭 스택을 쌓아서도 안 된다(연쇄 폭발).
/// </summary>
public static class DamageRules
{
    /// <summary>
    /// 방어력을 무시하는가 = '고정피해 성격'인가. 세 경우를 OR 로 묶는다:
    ///   · 속성이 Fixed(순수 고정피해)  · 갈래가 Debuff(아군 상태이상)  · 갈래가 EnemyDebuff(적 상태이상)
    /// Fixed 는 속성 축, Debuff/EnemyDebuff 는 갈래 축이라 두 축을 같이 본다.
    /// 크리·출혈트리거도 이 하나가 같이 가른다(고정피해는 크리 안 뜨고, 출혈도 안 얹는다 — 무한 재귀 방지).
    /// (쉴드는 별개 — 고정피해도 쉴드는 못 뚫는다.)
    /// </summary>
    public static bool IgnoresDefense(DamageInfo info)
        => info.type == DamageType.Fixed
        || info.category == DamageCategory.Debuff
        || info.category == DamageCategory.EnemyDebuff;

    /// <summary>치명타가 터질 수 있는가. 고정피해엔 안 붙는다.</summary>
    public static bool CanCrit(DamageInfo info) => !IgnoresDefense(info);

    /// <summary>출혈의 추가 피해를 트리거하는가. 고정피해가 스스로를 트리거하면 무한 재귀라 막는다.</summary>
    public static bool TriggersBleed(DamageInfo info) => !IgnoresDefense(info);

    /// <summary>유닛의 공격 속성을 직접 피해의 DamageType 으로 옮긴다.</summary>
    public static DamageType FromAttackType(AttackType t)
        => t == AttackType.Magic ? DamageType.Magic : DamageType.Physical;

    /// <summary>적발 피해 갈래(티어)인가. 명중/회피 판정과 피격 방향 효과가 이걸로 갈린다.</summary>
    public static bool IsEnemyTier(DamageCategory c)
        => c == DamageCategory.EnemyMinion || c == DamageCategory.EnemyElite
        || c == DamageCategory.EnemyBoss || c == DamageCategory.EnemyDebuff;

    /// <summary>플레이어 진영이 낸 피해 갈래인가. 시전자에 스탯이 없을 때(미니언 스킬 등)
    /// 크리·증폭 스탯을 플레이어에서 빌려도 되는지를 이걸로 가른다.</summary>
    public static bool IsPlayerSourced(DamageCategory c)
        => c == DamageCategory.BasicAttack || c == DamageCategory.Skill
        || c == DamageCategory.DashAttack || c == DamageCategory.Parry;
}

[System.Serializable]
public struct DamageInfo
{
    public float amount;
    public DamageType type;          // 축1: 속성
    public DamageCategory category;   // 축2: 갈래(유물 효과 필터용). None 이면 피격 시점에 자동 유도.
    public GameObject attacker;
    public float debuffMultiplier;
    public string popupText;
    public bool isRedirected;
    public bool causesHitstun;
    public float knockbackForce;
    public float superArmorDamage; // [추가] 슈퍼아머 깎는 수치

    // [26/07/21] 타격 시 부여할 상태이상. nullable 이라 default(DamageInfo) 도 안전하게 '없음'(null)이다.
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
    /// 지금 이걸 읽는 건 우클릭 카운터/가드 하나뿐이다(근접만 받아친다).
    /// 태그하는 쪽도 Projectile 뿐이라, 투사체가 아닌 적 피해(보스 장판 등)는 전부 '근접'이 된다.
    /// </summary>
    public bool isRanged;

    /// <summary>
    /// 피해가 '날아온 지점'(월드 좌표). 투사체만 채우고 나머지는 null 이다.
    ///
    /// attacker 로 대신할 수 없어서 따로 둔다 — 투사체는 attacker 에 '쏜 본체'를 싣기 때문에,
    /// 유도탄처럼 발사체와 시전자가 갈라지면 방향이 전혀 달라진다(마법사는 오른쪽에 있는데
    /// 파이어볼은 왼쪽에서 날아오는 상황). 우클릭 가드의 방어 부채꼴이 이걸 본다.
    /// </summary>
    public Vector2? hitFrom;

    // [26/07/18] 옛 isThrowDamage / isBasicAttack 위치 인자는 완전히 제거했다(각각 투척 철거·갈래 도입으로 의미 상실).
    // 위치 인자 50+곳을 스크립트로 일괄 이관했다. category 는 갈래(축2) — 태그할 곳에서만 `category:` 로 넘긴다.
    public DamageInfo(float amount, DamageType type = DamageType.Physical, GameObject attacker = null, float debuffMultiplier = 1f, string popupText = "", bool isRedirected = false, bool causesHitstun = false, float knockbackForce = 0f, float superArmorDamage = 0f, DamageCategory category = DamageCategory.None, StatusType? applyStatus = null, float statusDuration = 0f, bool isRanged = false, Vector2? hitFrom = null)
    {
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

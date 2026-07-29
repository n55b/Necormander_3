using UnityEngine;

public enum CommandData
{
    SkeletonWarrior = 0,
    SkeletonShieldbearer = 1,
    SkeletonArcher = 2,
    SkeletonPriest = 3,
    SkeletonBomber = 4,
    SkeletonSpearman = 5,
    SkeletonMagician = 6,
    SkeletonThief = 7,
    None = 100
}


/// <summary>
/// 상태이상. 26/07/17 재설계 — 구 취약/스택 트리거 구조(SkillKeyword, DebuffType,
/// DebuffStackType, DebuffCategory)는 전부 폐기했다. 그 구조는 소환수 여러 마리가 각자
/// 다른 트리거에 응답하는 전제였는데 메인 소환수가 1개로 고정되면서 안 맞게 됐다.
///
/// 5종은 전부 '독립적으로 동시 존재'한다. 구 구조는 슬롯이 하나라 다른 걸 걸면 기존 게
/// 터지고 새 건 증발했는데, 그 규칙이 통째로 사라졌다.
///
/// [26/07/21] 소환수 공격도 상태이상을 걸 수 있다(대쉬/피니셔/R 의 onHitStatus 필드).
/// DamageInfo.applyStatus 에 실려 CharacterHealth 명중 시 한 곳에서 부여된다. 유물/아이템도 같은 길.
/// </summary>
public enum StatusType
{
    Stun,      // 기절 — 행동 완전 불가. 피해 없음
    Freeze,    // 빙결 — 행동 불가. 직접 피해를 맞으면 고정 피해 + 즉시 해제
    Bleed,     // 출혈 — 직접 피해를 받을 때마다 추가 고정 피해
    Poison,    // 중독 — 초당 고정 피해
    BloodPop,  // 비폭 — 스택형. 임계치에 닿으면 자신과 주변에 폭발
    Hitstun,   // 경직 — 평타에 묻는 짧은 행동 불가. 기절과 '별개'로 둔 이유는
               // '기절 시간 증가' 같은 증감 요소가 여기에 묻지 않게 하기 위함이다.
    None,      // 상태이상 없음. 인스펙터 드롭다운 sentinel — 반드시 맨 끝(기존 인덱스 직렬화 보존).
}

/// <summary>
/// 상태이상 수치와 규칙. 전부 기획 임시값이라 밸런싱 때 여기만 고치면 된다.
/// (SO 로 빼는 건 실제로 튜닝을 시작할 때 — 지금 만들면 인스펙터만 늘고 쓸 일이 없다.)
/// </summary>
public static class StatusRules
{
    /// <summary>출혈·중독·비폭 기본 지속시간(초). 소스가 더 길게 명시할 수 있다. 재적중하면 갱신된다.</summary>
    public const float DURATION = 5f;

    /// <summary>기절 기본 1초. 소스가 명시하면 0.5~2초로 clamp 된다.</summary>
    public const float STUN_DURATION = 1f;
    public const float STUN_MIN = 0.5f;
    public const float STUN_MAX = 2f;

    /// <summary>빙결 기본 2.5초. 재적중하면 갱신된다.</summary>
    public const float FREEZE_DURATION = 2.5f;

    /// <summary>기절이 '자연 종료'된 뒤 다시 안 걸리는 내성 시간(초).</summary>
    public const float STUN_IMMUNITY = 3f;
    /// <summary>빙결이 '피해로 깨진' 뒤 다시 안 걸리는 내성 시간(초). 자연 만료엔 안 생긴다.</summary>
    public const float FREEZE_IMMUNITY = 1f;

    /// <summary>타입별 기본 지속시간. duration 을 안 넘겼을(0 이하) 때 쓴다.</summary>
    public static float DefaultDuration(StatusType t)
        => t == StatusType.Stun ? STUN_DURATION
         : t == StatusType.Freeze ? FREEZE_DURATION
         : DURATION;

    /// <summary>빙결이 깨질 때 터지는 고정 피해.</summary>
    public const float FREEZE_BREAK_DAMAGE = 20f;

    /// <summary>출혈 중 피격 1회당 추가되는 고정 피해.</summary>
    public const float BLEED_HIT_DAMAGE = 2f;

    /// <summary>중독 틱 피해와 주기(초).</summary>
    public const float POISON_TICK_DAMAGE = 5f;
    public const float POISON_TICK_INTERVAL = 1f;

    /// <summary>비폭: 이 스택에 닿으면 터지고 스택이 0 으로 돌아간다.</summary>
    public const int BLOODPOP_THRESHOLD = 10;
    public const float BLOODPOP_DAMAGE = 20f;
    public const float BLOODPOP_RADIUS = 2.5f;
    /// <summary>폭발 예고 시간(초). 보이긴 하되 못 피할 정도.</summary>
    public const float BLOODPOP_FUSE = 0.5f;

    /// <summary>
    /// 슈퍼아머가 막는 상태이상인가. 슈퍼아머 = 강인함이라, 있는 동안은 이동 방해 계열이
    /// 통째로 씹힌다(밀치기 포함). 씹힌 CC 는 저장되지 않는다 — 깨진 뒤 다시 걸어야 한다.
    /// 출혈/중독/비폭은 이동을 방해하지 않으므로 그대로 통과한다.
    /// </summary>
    public static bool BlockedBySuperArmor(StatusType t)
        => t == StatusType.Stun || t == StatusType.Freeze;

    /// <summary>행동을 막는 상태이상인가.</summary>
    public static bool PreventsAction(StatusType t)
        => t == StatusType.Stun || t == StatusType.Freeze || t == StatusType.Hitstun;

    /// <summary>스택을 쌓는 상태이상인가.</summary>
    public static bool IsStacking(StatusType t) => t == StatusType.BloodPop;
    /// <summary>
    /// 걸리는 '순간' 팝업 텍스트를 띄우는 상태이상인가.
    ///
    /// 경직(Hitstun)은 절대 넣지 말 것 — 평타 한 대마다 묻으므로 화면이 텍스트로 덮인다.
    /// 비폭(BloodPop)도 없다. 걸릴 때가 아니라 10스택에서 '터질 때' DetonateBloodPop 이 따로 띄운다.
    /// 지금은 빙결만 띄운다. 다른 상태이상도 알리고 싶으면 여기에 || 로 한 줄 붙이면 되고,
    /// 라벨과 색은 StatusEffectPalette 항목이 들고 있으므로 이 파일은 더 안 건드려도 된다.
    /// </summary>
    public static bool AnnouncesOnApply(StatusType t) => t == StatusType.Freeze;

}


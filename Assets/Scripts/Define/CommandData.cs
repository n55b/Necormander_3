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
/// 신규 5종(기절/빙결/출혈/중독/비폭)은 Phase 5 에서 들어온다. 지금은 철거만 끝난 상태라
/// 행동불가에 필요한 두 개만 남아 있다.
/// </summary>
public enum DebuffBoolType
{
    Stunned,       // 기절 — 행동 완전 불가
    Hitstunned     // 경직 — 평타에 묻는 짧은 행동 불가. 기절과 '별개'로 둔 이유는
                   // '기절 시간 증가' 같은 증감 요소가 여기에 묻지 않게 하기 위함이다.
}

[System.Flags]
public enum MinionJobFlags
{
    None = 0,
    Warrior = 1 << 0,
    Shieldbearer = 1 << 1,
    Archer = 1 << 2,
    Priest = 1 << 3,
    Bomber = 1 << 4,
    Spearman = 1 << 5,
    Magician = 1 << 6,
    Thief = 1 << 7,
    All = ~0
}
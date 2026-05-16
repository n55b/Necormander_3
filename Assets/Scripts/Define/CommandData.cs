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

public enum ThrowEffectCategory
{
    None,
    Target,      // 타겟팅 (단일)
    Area,        // 광역 (범위)
    CC,          // 상태이상 / 버프
    Shield,      // 보호막
    Formation,   // 진형파괴 / 돌진
    Repeat       // 되풀이 (증폭)
}

public enum DebuffCategory
{
    Stack,
    Bool
}

public enum DebuffStackType
{
    Poison,     // 중독
    Chill,      // 한기
    Execute,    // 처형
    BloodPop,   // 비폭
    Aging       // 노화
}

public enum DebuffBoolType
{
    Frozen,     // 동결
    Stunned,    // 기절
    Corroded    // 부식
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
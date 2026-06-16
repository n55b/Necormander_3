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
    Aging,      // 노화
    Vulnerability // 취약 (추가)
}

public enum DebuffBoolType
{
    Frozen,     // 동결
    Stunned,    // 기절
    Corroded,   // 부식
    Senility,   // 노쇠
    Feared,     // 공포
    BloodPopVulnerable, // 비폭 폭발 약점
    PoisonHost  // 숙주
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
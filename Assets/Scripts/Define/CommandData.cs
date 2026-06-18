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
    Vulnerability, // 취약
    BloodPop,      // 비폭
    Bleed,         // 출혈
    Wound,         // 상처
    Corrosion,     // 부식
    Fracture       // 골절
}

public enum DebuffBoolType
{
    Stunned,       // 기절 (취약 소모)
    Bleeding,      // 출혈 상태
    Wounded,       // 상처 상태
    Corroded,      // 부식 상태
    Fractured      // 골절 상태
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
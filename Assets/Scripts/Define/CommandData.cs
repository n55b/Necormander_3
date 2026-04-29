using UnityEngine;

public enum CommandData
{
    SkeletonWarrior,
    SkeletonShieldbearer,
    SkeletonArcher,
    SkeletonPriest,
    SkeletonBomber,
    SkeletonSpearman,
    SkeletonMagician,
    SkeletonThief
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
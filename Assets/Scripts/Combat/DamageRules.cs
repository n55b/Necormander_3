using UnityEngine;

/// <summary>Damage modifiers and the rules shared by combat callers.</summary>
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
        || c == DamageCategory.DashAttack;

    internal static float CalculateBeforeDefense(DamageInfo info, CharacterStat attacker, bool vulnerable, out bool isCritical)
    {
        float amount = info.amount;
        // Bleed scales as physical damage while retaining its status-specific damage type.
        if (vulnerable && info.type is DamageType.Physical or DamageType.Bleed)
            amount *= 1f + StatusRules.VULNERABLE_DAMAGE_BONUS;

        if (attacker != null)
        {
            float amplification = info.type == DamageType.Physical ? attacker.PHYS_AMP
                : info.type == DamageType.Magic ? attacker.MAGIC_AMP : 0f;
            if (amplification != 0f) amount *= 1f + amplification;
        }

        if (info.category == DamageCategory.Debuff && info.debuffMultiplier != 1f)
            amount *= info.debuffMultiplier;

        isCritical = attacker != null && CanCrit(info) && attacker.CRIT_CHANCE > 0f &&
            Random.value * 100f < attacker.CRIT_CHANCE;
        return isCritical ? amount * (attacker.CRIT_DAMAGE / 100f) : amount;
    }

    internal static float ApplyDefense(float amount, DamageInfo info, float defensePercent)
    {
        if (amount <= 0f) return 0f;
        return IgnoresDefense(info) ? amount : Mathf.Max(amount * (1f - defensePercent / 100f), 1f);
    }
}

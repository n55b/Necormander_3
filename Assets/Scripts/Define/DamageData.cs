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
/// 피해의 종류. 팝업 색을 가르고, 아래 DamageRules 가 이걸로 규칙을 판정한다.
/// 앞의 둘(Physical/Magic)이 '직접 피해', 나머지가 '상태이상 피해'다.
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
/// 상태이상 피해의 철칙을 한곳에 모아둔다. 여기저기 흩어놓으면 반드시 어긋난다.
///
/// 특히 '출혈을 트리거하지 않는다'가 중요하다 — 출혈은 "피격할 때마다 +2 고정 피해"인데,
/// 그 2 자체가 피해라서 스스로를 다시 트리거하면 무한 재귀로 스택 오버플로우가 난다.
/// 같은 이유로 비폭 폭발이 비폭 스택을 쌓아서도 안 된다(연쇄 폭발).
/// </summary>
public static class DamageRules
{
    /// <summary>상태이상이 주는 피해인가.</summary>
    public static bool IsStatusDamage(DamageType t)
        => t == DamageType.Freeze || t == DamageType.Poison || t == DamageType.Bleed
        || t == DamageType.BloodPop || t == DamageType.Fixed;

    /// <summary>방어력을 무시하는가. (쉴드는 무시하지 못한다 — 쉴드는 임시 체력에 가깝다)</summary>
    public static bool IgnoresDefense(DamageType t) => IsStatusDamage(t);

    /// <summary>치명타가 터질 수 있는가. 상태이상 고정 피해엔 안 붙는다.</summary>
    public static bool CanCrit(DamageType t) => !IsStatusDamage(t);

    /// <summary>이 피해가 출혈의 추가 피해를 트리거하는가.</summary>
    public static bool TriggersBleed(DamageType t) => !IsStatusDamage(t);
}

[System.Serializable]
public struct DamageInfo 
{
    public float amount;
    public DamageType type;
    public GameObject attacker;
    public bool isThrowDamage;
    public float debuffMultiplier;
    public bool isBasicAttack;
    public string popupText;
    public bool isRedirected;
    public bool causesHitstun;
    public float knockbackForce;
    public float superArmorDamage; // [추가] 슈퍼아머 깎는 수치

    public DamageInfo(float amount, DamageType type = DamageType.Physical, GameObject attacker = null, bool isThrowDamage = false, float debuffMultiplier = 1f, bool isBasicAttack = false, string popupText = "", bool isRedirected = false, bool causesHitstun = false, float knockbackForce = 0f, float superArmorDamage = 0f)
    {
        this.amount = amount;
        this.type = type;
        this.attacker = attacker;
        this.isThrowDamage = isThrowDamage;
        this.debuffMultiplier = debuffMultiplier;
        this.isBasicAttack = isBasicAttack;
        this.popupText = popupText;
        this.isRedirected = isRedirected;
        this.causesHitstun = causesHitstun;
        this.knockbackForce = knockbackForce;
        this.superArmorDamage = superArmorDamage;
    }
}

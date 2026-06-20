using UnityEngine;

public enum DamageType
{
    Physical,
    BloodPop,
    Bleed,
    Wound,
    Corrosion,
    Fracture,
    Fixed
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

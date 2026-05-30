using UnityEngine;

public enum DamageType
{
    Physical,
    Ice,
    Fire,
    Shadow,
    Magical,
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

    public DamageInfo(float amount, DamageType type = DamageType.Physical, GameObject attacker = null, bool isThrowDamage = false, float debuffMultiplier = 1f, bool isBasicAttack = false, string popupText = "")
    {
        this.amount = amount;
        this.type = type;
        this.attacker = attacker;
        this.isThrowDamage = isThrowDamage;
        this.debuffMultiplier = debuffMultiplier;
        this.isBasicAttack = isBasicAttack;
        this.popupText = popupText;
    }
}

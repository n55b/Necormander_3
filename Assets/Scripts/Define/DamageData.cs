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

    public DamageInfo(float amount, DamageType type = DamageType.Physical, GameObject attacker = null, bool isThrowDamage = false)
    {
        this.amount = amount;
        this.type = type;
        this.attacker = attacker;
        this.isThrowDamage = isThrowDamage;
    }
}

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

    public DamageInfo(float amount, DamageType type = DamageType.Physical, GameObject attacker = null)
    {
        this.amount = amount;
        this.type = type;
        this.attacker = attacker;
    }
}

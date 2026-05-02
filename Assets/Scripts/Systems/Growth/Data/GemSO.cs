using UnityEngine;

/// <summary>
/// 특정 직업군을 강화하는 보석 데이터입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewGem", menuName = "Necromancer/Growth/Gem")]
public class GemSO : GrowthItemSO
{
    public CommandData targetJob; // 적용될 직업군
    
    [Header("강화 수치")]
    public StatType statType;
    public float baseBonusValue;
}

public enum StatType
{
    Attack,
    Health,
    AttackSpeed,
    MoveSpeed,
    RespawnTime,
    ThrowDamage
}

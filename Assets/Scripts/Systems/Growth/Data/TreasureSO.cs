using UnityEngine;

/// <summary>
/// 전역 효과를 주는 중첩 가능한 보물 데이터입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewTreasure", menuName = "Necromancer/Growth/Treasure")]
public class TreasureSO : GrowthItemSO
{
    public TreasureEffectType effectType;
    public float valuePerStack;
}

public enum TreasureEffectType
{
    GoldBonus,
    ThrowRange,
    GlobalDamage,
    RepeatChance,
    RicochetCount // 튕기기 등
}

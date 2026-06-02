using UnityEngine;

/// <summary>
/// 모든 성장 아이템(소환수, 보석, 보물 등)의 최상위 데이터 클래스입니다.
/// </summary>
public abstract class GrowthItemSO : ScriptableObject
{
    [Header("기본 정보")]
    public string itemName;
    [TextArea] public string description;
    public Sprite icon;
    public ItemRarity rarity;

    [Header("상점 정보")]
    public int shopCost = 100; // 상점에서 판매 시 가격
}

public enum ItemRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

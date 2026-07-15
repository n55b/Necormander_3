using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// 모든 성장 아이템(소환수, 보석, 보물 등)의 최상위 데이터 클래스입니다.
/// </summary>
public abstract class GrowthItemSO : ScriptableObject
{
    [Header("기본 정보")]
    public string itemName;
    [TextArea] public string description;
    
    [Header("번역 데이터")]
    public LocalizedString localizedItemName;
    public LocalizedString localizedDescription;

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

/// <summary>
/// 모든 성장 아이템의 기본 데이터 구조 (클래스형)
/// </summary>
[System.Serializable]
public class GrowthItemData
{
    public string itemName;
    [TextArea] public string description;

    [Header("번역 데이터")]
    public LocalizedString localizedItemName;
    public LocalizedString localizedDescription;

    public Sprite icon;
    public ItemRarity rarity;
}

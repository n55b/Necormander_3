using UnityEngine;

/// <summary>
/// 모든 성장 아이템(소환수, 보석, 보물 등)의 최상위 데이터 클래스입니다.
/// </summary>
public abstract class GrowthItemSO : ScriptableObject
{
    [Header("기본 정보")]
    public LocText itemName;
    public LocText description;

    public Sprite icon;
    public ItemRarity rarity;

    [Header("상점 정보")]
    public int shopCost = 100; // 상점에서 판매 시 가격
}

public enum ItemRarity
{
    [UnityEngine.InspectorName("공통")] Common,
    [UnityEngine.InspectorName("희귀")] Rare,
    [UnityEngine.InspectorName("영웅")] Epic,
    [UnityEngine.InspectorName("전설")] Legendary
}

/// <summary>
/// 모든 성장 아이템의 기본 데이터 구조 (클래스형)
/// </summary>
[System.Serializable]
public class GrowthItemData
{
    // 런타임 카드 DTO 로도 쓴다 — 코드가 이미 번역한 문장은 key 없이 ko 에만 담는다(빈 key = 그대로 표시).
    public LocText itemName;
    public LocText description;

    public Sprite icon;
    public ItemRarity rarity;
}

using UnityEngine;

/// <summary>
/// 모든 성장 아이템의 기본 데이터 구조 (클래스형)
/// </summary>
[System.Serializable]
public class GrowthItemData
{
    public string itemName;
    [TextArea] public string description;
    public Sprite icon;
    public ItemRarity rarity;
}

/// <summary>
/// 특정 직업군의 계보와 보상 정보를 한곳에서 관리하는 마스터 데이터입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewLineage", menuName = "Necromancer/Growth/Minion System/Lineage Master")]
public class MinionLineageSO : ScriptableObject
{
    [Header("계보 기본 정보")]
    public CommandData jobType;
    public string lineageName;

    [Header("1. 기본 형태 (코어 아이템 정보)")]
    public MinionDataSO baseForm;
    public GrowthItemData baseItemData;

    [Header("2. 진화 경로 A (환골탈태 정보)")]
    public MinionDataSO techA;
    public GrowthItemData techAItemData;

    [Header("3. 진화 경로 B (환골탈태 정보)")]
    public MinionDataSO techB;
    public GrowthItemData techBItemData;

    /// <summary>
    /// 인덱스에 따른 미니언 전투 데이터 반환
    /// </summary>
    public MinionDataSO GetForm(int index)
    {
        switch (index)
        {
            case 1: return techA;
            case 2: return techB;
            default: return baseForm;
        }
    }

    /// <summary>
    /// 인덱스에 따른 아이템 표시 정보 반환 (UI용)
    /// </summary>
    public GrowthItemData GetItemData(int index)
    {
        switch (index)
        {
            case 1: return techAItemData;
            case 2: return techBItemData;
            default: return baseItemData;
        }
    }
}

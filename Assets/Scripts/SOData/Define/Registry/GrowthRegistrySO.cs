using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 모든 성장 데이터 계보와 보석, 보물들을 보관하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "GrowthRegistry", menuName = "Necromancer/Registry/GrowthRegistry")]
public class GrowthRegistrySO : ScriptableObject
{
    [Header("소환수 직업 계보 (이곳에 직업별 마스터 파일을 등록하세요)")]
    public List<MinionLineageSO> minionLineages = new List<MinionLineageSO>();

    [Header("강화 보석")]
    public List<GemSO> gems = new List<GemSO>();

    [Header("중첩 보물")]
    public List<TreasureSO> treasures = new List<TreasureSO>();

    [Header("특수 능력 (추후 확장용)")]
    public List<GrowthItemSO> specialAbilities = new List<GrowthItemSO>();

    /// <summary>
    /// 모든 아이템을 하나의 리스트로 합쳐서 반환합니다. (보상 생성용)
    /// </summary>
    public List<GrowthItemSO> GetAllItems()
    {
        List<GrowthItemSO> allItems = new List<GrowthItemSO>();
        // 계보는 직접 아이템이 아니므로 제외하거나, 필요시 변환 로직 추가
        allItems.AddRange(gems);
        allItems.AddRange(treasures);
        allItems.AddRange(specialAbilities);
        return allItems;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 프로젝트 내의 모든 관련 SO를 검색하여 리스트를 자동으로 갱신합니다.
    /// </summary>
    public void RefreshRegistry()
    {
        minionLineages.Clear();
        gems.Clear();
        treasures.Clear();

        // 1. 계보(Lineage) 검색
        string[] lineageGuids = UnityEditor.AssetDatabase.FindAssets("t:MinionLineageSO");
        foreach (var guid in lineageGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MinionLineageSO>(path);
            if (asset != null) minionLineages.Add(asset);
        }

        // 2. 보석(Gem) 검색
        string[] gemGuids = UnityEditor.AssetDatabase.FindAssets("t:GemSO");
        foreach (var guid in gemGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GemSO>(path);
            if (asset != null) gems.Add(asset);
        }

        // 3. 보물(Treasure) 검색
        string[] treasureGuids = UnityEditor.AssetDatabase.FindAssets("t:TreasureSO");
        foreach (var guid in treasureGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TreasureSO>(path);
            if (asset != null) treasures.Add(asset);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        
        Debug.Log($"<color=cyan>[GrowthRegistry]</color> 자동 갱신 완료: 계보({minionLineages.Count}), 보석({gems.Count}), 보물({treasures.Count})");
    }
#endif
}

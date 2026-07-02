using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 모든 성장 데이터 계보와 보석, 보물들을 보관하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "GrowthRegistry", menuName = "Necromancer/Registry/GrowthRegistry")]
public class GrowthRegistrySO : ScriptableObject
{
    [Header("소환수 데이터")]
    public List<MinionDataSO> minionDatas = new List<MinionDataSO>();

    [Header("강화 보석")]
    public List<GemSO> gems = new List<GemSO>();

    [Header("중첩 보물")]
    public List<TreasureSO> treasures = new List<TreasureSO>();

    [Header("특수 능력 (추후 확장용)")]
    public List<GrowthItemSO> specialAbilities = new List<GrowthItemSO>();

    [Header("플레이어 스킬 (Q/E/R 장착용)")]
    public List<PlayerSkillSO> playerSkills = new List<PlayerSkillSO>();


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
        minionDatas.Clear();
        gems.Clear();
        treasures.Clear();

        // 1. 소환수 데이터(MinionDataSO) 검색
        string[] minionGuids = UnityEditor.AssetDatabase.FindAssets("t:MinionDataSO");
        foreach (var guid in minionGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MinionDataSO>(path);
            if (asset != null) minionDatas.Add(asset);
        }

        // 2. 보석(Gem) 검색
        string[] gemGuids = UnityEditor.AssetDatabase.FindAssets("t:GemSO");
        foreach (var guid in gemGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue; // [추가] Deprecated 보석 제외
            
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

        // 4. 던지기 능력(ThrowAbilitySO) 검색 [추가]
        specialAbilities.Clear();
        string[] abilityGuids = UnityEditor.AssetDatabase.FindAssets("t:ThrowAbilitySO");
        foreach (var guid in abilityGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ThrowAbilitySO>(path);
            if (asset != null) specialAbilities.Add(asset);
        }

        // 5. 플레이어 스킬(PlayerSkillSO) 검색 [추가]
        playerSkills.Clear();
        string[] playerSkillGuids = UnityEditor.AssetDatabase.FindAssets("t:PlayerSkillSO");
        foreach (var guid in playerSkillGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var asset2 = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerSkillSO>(path);
            if (asset2 != null) playerSkills.Add(asset2);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        
        Debug.Log($"<color=cyan>[GrowthRegistry]</color> 자동 갱신 완료: 소환수({minionDatas.Count}), 보석({gems.Count}), 보물({treasures.Count}), 능력({specialAbilities.Count})");
    }
#endif
}

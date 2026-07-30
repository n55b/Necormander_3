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

    [Header("중첩 보물")]
    public List<TreasureSO> treasures = new List<TreasureSO>();

    [Header("특수 능력 (추후 확장용)")]
    public List<GrowthItemSO> specialAbilities = new List<GrowthItemSO>();

    [Header("플레이어 스킬 (장비 풀 구성 + 로드 시 이름→SO 해석용)")]
    public List<PlayerSkillSO> playerSkills = new List<PlayerSkillSO>();

    [Header("장비 (플레이어 스킬은 이제 장비로만 획득)")]
    public List<EquipmentSO> equipments = new List<EquipmentSO>();

    [Header("아이템 (주머니. 장비와 별개 시스템)")]
    [Tooltip("세이브에 이름으로 저장되므로 로드 시 이름→SO 해석에 이 목록이 필요하다.")]
    public List<ItemSO> items = new List<ItemSO>();


    /// <summary>
    /// 모든 아이템을 하나의 리스트로 합쳐서 반환합니다. (보상 생성용)
    /// </summary>
    public List<GrowthItemSO> GetAllItems()
    {
        List<GrowthItemSO> allItems = new List<GrowthItemSO>();
        // 계보는 직접 아이템이 아니므로 제외하거나, 필요시 변환 로직 추가
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
        treasures.Clear();

        // 1. 소환수 데이터 검색. t: 필터는 상속을 따르므로 파생 타입만 콕 집으면
        // 적/엘리트/보스(EnemyMinionDataSO)는 애초에 걸리지 않는다 — 경로에 의존하지 않는다.
        foreach (var typeFilter in new[] { "t:MainMinionDataSO", "t:SubMinionDataSO" })
        {
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets(typeFilter))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Deprecated/")) continue;

                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MinionDataSO>(path);
                if (asset != null) minionDatas.Add(asset);
            }
        }

        // 2. 보물(Treasure) 검색
        string[] treasureGuids = UnityEditor.AssetDatabase.FindAssets("t:TreasureSO");
        foreach (var guid in treasureGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TreasureSO>(path);
            if (asset != null) treasures.Add(asset);
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

        // 6. 장비(EquipmentSO) 검색
        equipments.Clear();
        string[] equipmentGuids = UnityEditor.AssetDatabase.FindAssets("t:EquipmentSO");
        foreach (var guid in equipmentGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;
            var asset3 = UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentSO>(path);
            if (asset3 != null) equipments.Add(asset3);
        }

        // 7. 아이템(ItemSO) 검색 — 주머니. 장비와 별개.
        items.Clear();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:ItemSO"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;
            var asset4 = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemSO>(path);
            if (asset4 != null) items.Add(asset4);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"<color=cyan>[GrowthRegistry]</color> 자동 갱신 완료: 소환수({minionDatas.Count}), 보물({treasures.Count}), 능력({specialAbilities.Count}), 스킬({playerSkills.Count}), 장비({equipments.Count}), 아이템({items.Count})");
    }
#endif
}

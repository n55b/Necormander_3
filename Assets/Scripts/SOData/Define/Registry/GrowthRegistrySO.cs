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

    [Header("우클릭 (패링/카운터/가드)")]
    [Tooltip("마을 NPC 교체 목록에 뜨는 우클릭들. 영구 선택이 이름으로 저장되므로 이름→SO 해석에도 쓰인다.")]
    public List<RightClickDataSO> rightClicks = new List<RightClickDataSO>();

    [Tooltip("아무것도 안 골랐을 때 플레이어가 기본으로 드는 우클릭(설계상 패링).\n" +
             "비워두면 rightClicks 에서 Parry 타입을 찾아 쓰고, 그것도 없으면 첫 유효 항목을 쓴다 — " +
             "'우클릭이 없는 플레이어'는 만들지 않는다.")]
    public RightClickDataSO defaultRightClick;

    /// <summary>
    /// 기본 우클릭. 인스펙터 지정 → Parry 타입 탐색 → 첫 유효 항목 순으로 떨어진다.
    /// 배선을 깜빡해도 플레이어가 우클릭 없이 게임을 시작하는 일은 없게 한다.
    /// </summary>
    public RightClickDataSO ResolveDefaultRightClick()
    {
        if (defaultRightClick != null && defaultRightClick.IsValid) return defaultRightClick;

        var parry = rightClicks.Find(r => r != null && r.IsValid && r.Type == RightClickType.Parry);
        if (parry != null) return parry;

        return rightClicks.Find(r => r != null && r.IsValid);
    }


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
        // [26/08/15] 서브 소환수 삭제로 메인만 남았다.
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:MainMinionDataSO"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;

            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MinionDataSO>(path);
            if (asset != null) minionDatas.Add(asset);
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

        // 8. 우클릭(RightClickDataSO) 검색 — 패링/카운터/가드.
        rightClicks.Clear();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:RightClickDataSO"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;
            var asset5 = UnityEditor.AssetDatabase.LoadAssetAtPath<RightClickDataSO>(path);
            if (asset5 != null) rightClicks.Add(asset5);
        }
        // 기본값이 안 잡혀 있으면 패링을 자동으로 물려준다(배선 깜빡 방지).
        if (defaultRightClick == null)
            defaultRightClick = rightClicks.Find(r => r != null && r.Type == RightClickType.Parry);

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"<color=cyan>[GrowthRegistry]</color> 자동 갱신 완료: 소환수({minionDatas.Count}), 보물({treasures.Count}), 능력({specialAbilities.Count}), 스킬({playerSkills.Count}), 장비({equipments.Count}), 아이템({items.Count}), 우클릭({rightClicks.Count})");
    }
#endif
}

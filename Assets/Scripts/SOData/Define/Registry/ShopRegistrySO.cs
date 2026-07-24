using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 내 상점에서 등장할 수 있는 모든 아이템 풀을 관리하는 레지스트리입니다.
/// 상점 아이템은 전부 여기(SOData)에서 관리한다: 서브 소환수 + 장비 + 강화 아이템.
/// (젬 효과가 전부 제거되면서 gemPool 도 함께 내렸습니다 — GEM_LEGACY.md 참조.
///  젬 에셋 자체는 SOData/Rewards/Gems 에 그대로 남아 있고, 소켓 배관도 살아 있습니다.)
/// </summary>
[CreateAssetMenu(fileName = "ShopRegistry", menuName = "Necromancer/Registry/ShopRegistry")]
public class ShopRegistrySO : ScriptableObject
{
    [Header("상점에 등장할 미니언 목록(서브 소환수)")]
    public List<MinionDataSO> minionPool = new List<MinionDataSO>();

    [Header("상점에 등장할 장비 목록")]
    [Tooltip("상점에서 살 수 있는 장비. 구매 시 현재 장비를 교체하고 스킬 2개를 새로 굴린다.")]
    public List<EquipmentSO> equipmentPool = new List<EquipmentSO>();

    [Header("장비 강화 아이템(소모성)")]
    [Tooltip("강화 아이템 1개 가격(골드). 전역 고정값.")]
    public int enhanceCost = 250;
    [Tooltip("상점 풀에 넣을 강화 아이템 개수. 랜덤 진열이라 실제 노출 수는 이보다 적을 수 있다.")]
    public int enhanceStock = 3;
    [Tooltip("강화 아이템 아이콘(없으면 비움).")]
    public Sprite enhanceIcon;

#if UNITY_EDITOR
    [ContextMenu("Refresh Registry (Load All Active Items)")]
    public void RefreshRegistry()
    {
        minionPool.Clear();
        equipmentPool.Clear();

        // 설계 4: 상점에 서는 건 서브 소환수뿐이다. 타입으로 직접 거른다.
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:SubMinionDataSO"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;

            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MinionDataSO>(path);
            if (asset != null) minionPool.Add(asset);
        }

        // 장비: 전부 스캔(Deprecated 제외). GrowthRegistry.equipments 와 동일 규칙.
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:EquipmentSO"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;

            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentSO>(path);
            if (asset != null) equipmentPool.Add(asset);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"<color=yellow>[ShopRegistry]</color> 자동 갱신 완료: 미니언({minionPool.Count}), 장비({equipmentPool.Count})");
    }
#endif
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 내 상점에서 등장할 수 있는 모든 아이템 풀을 관리하는 레지스트리입니다.
/// 상점은 주머니 아이템만 판다. 장비는 강화 상점에서 강화로만 얻는다.
/// (젬 효과가 전부 제거되면서 gemPool 도 함께 내렸습니다 — Legacy/History/Docs/GEM_LEGACY.md 참조.
///  젬 에셋 자체는 SOData/Rewards/Gems 에 그대로 남아 있고, 소켓 배관도 살아 있습니다.)
/// </summary>
[CreateAssetMenu(fileName = "ShopRegistry", menuName = "Necromancer/Registry/ShopRegistry")]
public class ShopRegistrySO : ScriptableObject
{
    [Header("상점에 등장할 아이템 목록(주머니)")]
    [Tooltip("가격은 티어에서 자동으로 나오므로(ItemTierRules) 따로 적을 게 없다.\n" +
             "구매하면 바로 주머니에 들어간다. 중첩 한도면 골드를 내기 전에 구매가 막히고, 주머니가 없을 때만 바닥에 떨어진다.")]
    public List<ItemSO> itemPool = new List<ItemSO>();

    // 강화는 전용 상점(Room_ShopEnhance)의 EnhanceShopNPC 에게 F 로만 한다.
    // 일반 상점에 소모성 강화 카드를 같이 진열하던 건 걷어냈다(역할 중복 + 그쪽이 고정가라 더 쌌음).
    // 그래서 남은 두 값은 전부 강화 상점 전용이다.
    [Header("장비 강화 (강화 상점 전용)")]
    [Tooltip("0강에서 1강으로 올릴 때의 가격(골드).")]
    public int enhanceCost = 250;
    [Tooltip("현재 강화레벨 1당 이만큼 비싸진다. 예: 250 + 25/레벨 → 250 / 275 / 300 / 325 / 350.\n" +
             "강화 상점은 재고가 없어서 골드가 유일한 제동장치라 여기서 곡선을 잡는다.")]
    public int enhanceCostPerLevel = 25;

#if UNITY_EDITOR
    [ContextMenu("Refresh Registry (Load All Active Items)")]
    public void RefreshRegistry()
    {
        itemPool.Clear();

        // 아이템: 전부 스캔(Deprecated 제외).
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:ItemSO"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;

            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemSO>(path);
            if (asset != null && asset.availableAsReward) itemPool.Add(asset);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"<color=yellow>[ShopRegistry]</color> 자동 갱신 완료: 아이템({itemPool.Count})");
    }
#endif
}

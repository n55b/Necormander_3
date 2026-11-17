using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 내 상점에서 등장할 수 있는 모든 아이템 풀을 관리하는 레지스트리입니다.
/// 상점 아이템은 전부 여기(SOData)에서 관리한다: 장비 + 주머니 아이템 + 강화.
/// 경제 수치(방 클리어 골드, 아이템 등급별 가격 · 방 보상 등장 비중)도 여기 둔다 — 코드를 고치지 않고 에셋에서 밸런싱한다.
/// 런타임은 DataManager.SHOP_REGISTRY 로 읽는다.
/// ([26/08/15] 서브 소환수 삭제로 소환수 진열이 빠졌다 — 그만큼 아이템 에셋을 늘려야 한다.)
/// (젬 효과가 전부 제거되면서 gemPool 도 함께 내렸습니다 — Docs/Archive/GEM_LEGACY.md 참조.
///  젬 에셋 자체는 SOData/Rewards/Gems 에 그대로 남아 있고, 소켓 배관도 살아 있습니다.)
/// </summary>
[CreateAssetMenu(fileName = "ShopRegistry", menuName = "Necromancer/Registry/ShopRegistry")]
public class ShopRegistrySO : ScriptableObject
{
    [Header("상점에 등장할 미니언 목록")]
    [Tooltip("[26/08/15] 서브 소환수 삭제로 지금은 비어 있다. Refresh 해도 안 채워진다 — " +
             "메인 소환수는 보상 방 전용이라 상점에 올리지 않기 때문. 수동으로 넣으면 동작은 한다.")]
    public List<MinionDataSO> minionPool = new List<MinionDataSO>();

    [Header("상점에 등장할 장비 목록")]
    [Tooltip("상점에서 살 수 있는 장비. 가격은 장비마다(장비의 상점 판매가). 강화 전용 장비는 진열하지 않는다.\n" +
             "구매하면 지금 장비를 이 장비(0강)로 바꾼다 — 이전 장비와 그 강화 단계는 사라진다(장비는 패시브만 준다).")]
    public List<EquipmentSO> equipmentPool = new List<EquipmentSO>();

    [Header("상점에 등장할 아이템 목록(주머니)")]
    [Tooltip("장비와 별개 시스템. 가격은 아래 '아이템 등급별 가격'에서 등급으로 정해지므로 아이템마다 적을 게 없다.\n" +
             "구매하면 바로 주머니에 들어간다. 중첩 한도면 골드를 내기 전에 구매가 막히고, 주머니가 없을 때만 바닥에 떨어진다.")]
    public List<ItemSO> itemPool = new List<ItemSO>();

    // 칸 순서 = 등급 순서. [0] = 1등급 … [3] = 4등급. 아이템 편집기 위쪽 '티어별 가격 · 보상 등장 비중'에서 고친다.
    [Header("주머니 아이템 등급별 (칸 0 = 1등급 … 칸 3 = 4등급)")]
    [Tooltip("상점 구매가(골드). 칸 0 = 1등급 … 칸 3 = 4등급. 아이템마다 적지 않고 등급으로 정한다.")]
    public int[] itemTierPrice = { 600, 400, 200, 100 };
    [Tooltip("방 보상(일반 방 · 엘리트 · 증강 아이템 보상)으로 아이템이 떨어질 때, 이 등급 아이템 '한 개'가 뽑힐 상대 비중. 칸 0 = 1등급 … 칸 3 = 4등급.\n" +
             "모두 같으면 후보 아이템마다 같은 확률(기본 1,1,1,1). 예: 4등급만 3 으로 올리면 4등급 아이템 하나가 다른 아이템보다 3배 잘 나온다.\n" +
             "0 이면 그 등급은 방 보상으로 안 나온다. 상점 진열에는 영향이 없다.")]
    public int[] itemTierRewardWeight = { 1, 1, 1, 1 };

    // [26/08/03] 강화는 전용 상점(Room_ShopEnhance)의 EnhanceShopNPC 에게 F 로만 한다.
    // 일반 상점에 소모성 강화 카드를 같이 진열하던 건 걷어냈다(역할 중복 + 그쪽이 고정가라 더 쌌음).
    // 그래서 남은 두 값은 전부 강화 상점 전용이다.
    [Header("장비 강화 (강화 상점 전용)")]
    [Tooltip("0강에서 1강으로 올릴 때의 가격(골드).")]
    public int enhanceCost = 250;
    [Tooltip("현재 강화레벨 1당 이만큼 비싸진다. 예: 250 + 25/레벨 → 250 / 275 / 300 / 325 / 350.\n" +
             "강화 상점은 재고가 없어서 골드가 유일한 제동장치라 여기서 곡선을 잡는다.")]
    public int enhanceCostPerLevel = 25;

    // 방 클리어 골드는 어울리는 편집 창이 없어 이 에셋 인스펙터에서 고친다.
    [Header("방 클리어 보상")]
    [Tooltip("일반 방 · 증강 방을 깼을 때 기본으로 주는 골드. 증강 '골드' 보상은 이 값에 추가분을 더해 주고, " +
             "증강 카드 문구('N 골드 획득', '일반 방과 동일: N골드 + 아이템 1개')도 이 값으로 계산된다.")]
    public int normalClearGold = 200;

    /// <summary>아이템 등급의 상점 구매가(골드).</summary>
    public int ItemPrice(ItemTier tier) => ByTier(itemTierPrice, tier);

    /// <summary>방 보상으로 뽑힐 상대 비중(아이템 한 개당). 0 이면 방 보상에서 빠진다.</summary>
    public int ItemRewardWeight(ItemTier tier) => ByTier(itemTierRewardWeight, tier);

    private static int ByTier(int[] values, ItemTier tier)
    {
        int i = Mathf.Clamp((int)tier, 1, 4) - 1;
        return values != null && i < values.Length ? Mathf.Max(0, values[i]) : 0;
    }

    // 인스펙터에서 칸 수를 바꿔도 등급 4칸을 유지한다(남는 칸은 버리고 모자란 칸은 0).
    private void OnValidate()
    {
        if (itemTierPrice == null || itemTierPrice.Length != 4) System.Array.Resize(ref itemTierPrice, 4);
        if (itemTierRewardWeight == null || itemTierRewardWeight.Length != 4) System.Array.Resize(ref itemTierRewardWeight, 4);
    }

#if UNITY_EDITOR
    [ContextMenu("Refresh Registry (Load All Active Items)")]
    public void RefreshRegistry()
    {
        minionPool.Clear();
        equipmentPool.Clear();
        itemPool.Clear();

        // [26/08/15] 상점에 서던 건 서브 소환수뿐이었고, 서브가 삭제되면서 이 스캔이 통째로 없어졌다.
        // minionPool 필드 자체는 남긴다 — 나중에 메인 소환수를 상점에 올릴 여지가 있고,
        // 지우면 이 SO 를 참조하는 에셋의 직렬화가 흔들린다.

        // 장비: 전부 스캔(Deprecated 제외). GrowthRegistry.equipments 와 동일 규칙.
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:EquipmentSO"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;

            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentSO>(path);
            if (asset != null && !asset.upgradeOnly) equipmentPool.Add(asset);
        }

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

        Debug.Log($"<color=yellow>[ShopRegistry]</color> 자동 갱신 완료: 미니언({minionPool.Count}), 장비({equipmentPool.Count}), 아이템({itemPool.Count})");
    }
#endif
}

using System.Collections.Generic;
using UnityEngine;

// RoomPrefabDataSO와 방 프리팹은 enum의 정수값을 저장한다. 새 값은 끝에 추가한다.
// 증강 방의 보상은 전투 전에 고른 카드가 정한다. 강화 상점은 일반 상점과 층당 수를 따로 관리한다.
public enum RoomType { [UnityEngine.InspectorName("생성")] Spawn, [UnityEngine.InspectorName("일반")] Normal, [UnityEngine.InspectorName("엘리트")] Elite, [UnityEngine.InspectorName("보상")] Reward, [UnityEngine.InspectorName("상점")] Shop, [UnityEngine.InspectorName("보스")] Boss, [UnityEngine.InspectorName("증강")] Augment, [UnityEngine.InspectorName("강화 상점")] EnhanceShop }
// Item은 주머니 아이템이다. MinionEnhance와 MinionEvolve는 rawData에 대상 MainMinionDataSO를 담는다.
public enum RewardCategory { [UnityEngine.InspectorName("소환수")] Minion, [UnityEngine.InspectorName("변이")] Metamorphosis, [UnityEngine.InspectorName("보물")] Treasure, [UnityEngine.InspectorName("골드")] Gold, [UnityEngine.InspectorName("장비")] Equipment = 5, [UnityEngine.InspectorName("아이템")] Item, [UnityEngine.InspectorName("소환수 강화")] MinionEnhance, [UnityEngine.InspectorName("소환수 진화")] MinionEvolve }

/// <summary>
/// 보상으로 제안될 아이템 정보를 담는 구조체입니다.
/// </summary>
public struct RewardCandidate
{
    public bool equipped;
    public GrowthItemData displayData; 
    public object rawData;             
    public int techIndex;              // 계보일 경우 진화 단계
    public RewardCategory category;
    public int goldAmount;             
}

/// <summary>
/// [역할: 계산기] 보상 시스템의 수학적/논리적 판단을 담당하는 정적 클래스입니다.
/// - 담당: 인벤토리 분석, 유효한 보상 필터링, 확률 기반 랜덤 추출.
/// - 활용: RewardManager나 상점 시스템에서 "제공할 보상 리스트"가 필요할 때 호출합니다.
/// - 특징: UI나 게임 상태를 직접 수정하지 않으며, 오직 데이터(RewardCandidate)만 생성합니다.
/// </summary>
public static class RewardProcessor
{
    /// <summary>주머니 아이템의 상점 카드 표시 데이터. 등급 표기가 설명 앞에 붙는다.</summary>
    public static GrowthItemData BuildItemDisplayData(ItemSO so)
        => new GrowthItemData
        {
            itemName = so.DisplayName,
            description = so.TooltipBody,
            icon = so.icon
        };

    /// <summary>최대 중첩에 도달하지 않은 아이템만 상점/보상 후보로 사용한다.</summary>
    public static bool CanOfferItem(ItemSO item)
        => item != null && item.availableAsReward && (ItemPouch.Instance == null || ItemPouch.Instance.CanAdd(item));

    /// <summary>중첩 여유가 있는 아이템 하나를 랜덤으로. 일반/엘리트/증강 보상 공용. 후보가 없으면 null.</summary>
    public static ItemSO PickRandomItem(DataManager data, ItemTier maximumTier = ItemTier.Tier4,
        Dictionary<ItemSO, int> reserved = null)
    {
        var pool = new List<ItemSO>();
        CollectDropCandidates(data, maximumTier, reserved, pool);
        return pool.Count == 0 ? null : pool[Random.Range(0, pool.Count)];
    }

    private static void CollectDropCandidates(DataManager data, ItemTier maximumTier, Dictionary<ItemSO, int> reserved, List<ItemSO> pool)
    {
        var items = data != null ? data.GET_GROWTH_REGISTRY()?.items : null;
        if (items == null) return;
        foreach (var it in items)
        {
            if (it == null || !it.availableAsReward || it.tier > maximumTier) continue;
            int pending = reserved != null && reserved.TryGetValue(it, out int amount) ? amount : 0;
            if ((ItemPouch.Instance != null ? ItemPouch.Instance.CountOf(it) : 0) + pending < it.MaxStack) pool.Add(it);
        }
    }

    public static bool HasRewardItem(DataManager data, ItemTier maximumTier)
    {
        var items = data != null ? data.GET_GROWTH_REGISTRY()?.items : null;
        if (items == null) return false;
        foreach (var item in items) if (CanOfferItem(item) && item.tier <= maximumTier) return true;
        return false;
    }

    /// <summary>보상 한 묶음에서 최대 중첩을 초과해 뽑지 않고, 기존 바닥 아이템 경로로 지급한다.</summary>
    public static int DropItems(DataManager data, Vector3 position, int count, ItemTier maximumTier = ItemTier.Tier4)
    {
        if (count <= 0) return 0;
        var pool = new List<ItemSO>();
        CollectDropCandidates(data, maximumTier, null, pool);
        var reserved = count > 1 ? new Dictionary<ItemSO, int>() : null;
        int dropped = 0;
        while (dropped < count && pool.Count > 0)
        {
            var item = pool[Random.Range(0, pool.Count)];
            if (GroundItem.Drop(item, position) == null) break;
            dropped++;
            if (dropped == count) break;
            reserved.TryGetValue(item, out int pending);
            reserved[item] = ++pending;
            int owned = ItemPouch.Instance != null ? ItemPouch.Instance.CountOf(item) : 0;
            if (owned + pending >= item.MaxStack)
                for (int i = pool.Count - 1; i >= 0; i--)
                    if (pool[i] == item) pool.RemoveAt(i);
        }
        return dropped;
    }

    // --- 1-A''. 보상방용: 메인 소환수 강화 카드 ---
    /// <summary>
    /// 지금 든 메인 소환수의 다음 강화 단계 카드. 더 강화할 수 없으면(소환수 없음/최대) 빈 리스트.
    /// </summary>
    public static List<RewardCandidate> GenerateMinionEnhanceReward(InventoryManager inven)
    {
        var results = new List<RewardCandidate>();
        if (inven == null || !inven.CanEnhanceMainSummon) return results;

        var main = inven.MainSummon;
        int cur = inven.MainSummonEnhanceLevel;
        string minionName = string.IsNullOrEmpty(main.minionName) ? main.name : main.minionName;

        results.Add(new RewardCandidate
        {
            category = RewardCategory.MinionEnhance,
            rawData = main,
            displayData = new GrowthItemData
            {
                itemName = $"{minionName} 강화 +{cur + 1}/{main.MaxEnhanceLevel}",
                description = MinionEnhance.DescribeNext(main, cur),
                icon = main.minionIcon != null ? main.minionIcon : main.ResolveIcon(),
            }
        });
        return results;
    }

    // --- 1-A'''. 보상방용: 메인 소환수 진화 카드(갈래당 1장) ---
    /// <summary>
    /// 지금 든 메인 소환수의 진화 갈래 카드. 진화할 수 없으면(갈래 없음/요구 강화 미달) 빈 리스트.
    /// </summary>
    public static List<RewardCandidate> GenerateMinionEvolveRewards(InventoryManager inven)
    {
        var results = new List<RewardCandidate>();
        if (inven == null || !inven.CanEvolveMainSummon) return results;

        var main = inven.MainSummon;
        foreach (var evo in main.evolutions)
        {
            if (evo == null || evo == main) continue;
            var display = BuildMinionDisplayData(evo);
            display.itemName = $"진화: {display.itemName}";
            results.Add(new RewardCandidate
            {
                category = RewardCategory.MinionEvolve,
                rawData = evo,
                displayData = display,
            });
        }
        return results;
    }

    // --- 1-B. 미니언 스킬 방용: 소환수 코어 배출 ---
    /// <summary>지정한 역할의 소환수 카드 3장을 뽑는다. 풀은 타입으로만 갈린다.</summary>
    /// <param name="roleType">typeof(MainMinionDataSO). null 이면 전부. (서브 삭제 후 사실상 메인뿐)</param>
    public static List<RewardCandidate> GenerateSummonRewards(InventoryManager inven, DataManager data, System.Type roleType, int count = 3)
    {
        List<RewardCandidate> results = new List<RewardCandidate>();
        var registry = data.GET_GROWTH_REGISTRY();

        List<RewardCandidate> combinedPool = new List<RewardCandidate>();
        combinedPool.AddRange(GetValidCores(inven, registry.minionDatas, true, roleType));

        // 랜덤하게 count 개 선택 (중복 제거)
        for (int i = 0; i < count; i++)
        {
            if (combinedPool.Count > 0)
            {
                int idx = Random.Range(0, combinedPool.Count);
                results.Add(combinedPool[idx]);
                combinedPool.RemoveAt(idx); 
            }
            else
            {
                // 보상이 고갈되었을 때 빈 보상 추가 방어 로직
                results.Add(new RewardCandidate { 
                    category = RewardCategory.Minion, 
                    displayData = new GrowthItemData { itemName = "None", description = "No more minion rewards available." },
                    rawData = null 
                });
            }
        }

        return results;
    }

    // --- 2. 엘리트/보상 방용: 카테고리별 후보 생성 (무조건 3개 슬롯 반환) ---
    public static List<RewardCandidate> GenerateCandidatesByCategory(InventoryManager inven, DataManager data, RewardCategory category, int count = 3)
    {
        List<RewardCandidate> allPossible = new List<RewardCandidate>();
        var registry = data.GET_GROWTH_REGISTRY();

        switch (category)
        {
            case RewardCategory.Minion:
                allPossible.AddRange(GetValidCores(inven, registry.minionDatas, true)); // 중복 제외
                break;
            case RewardCategory.Metamorphosis:
                // 변이/승급 시스템 삭제로 아무것도 생성 안함
                break;
            case RewardCategory.Treasure:
                // [참고] 보물은 다른 방식으로 획득할 예정이므로 여기서 제안하지 않을 수 있음
                allPossible.AddRange(GetValidTreasures(registry.treasures));
                break;
        }

        List<RewardCandidate> results = new List<RewardCandidate>();
        
        // 실제 데이터가 있는 만큼 랜덤으로 추출
        for (int i = 0; i < count; i++)
        {
            if (allPossible.Count > 0)
            {
                int idx = Random.Range(0, allPossible.Count);
                results.Add(allPossible[idx]);
                allPossible.RemoveAt(idx);
            }
            else
            {
                // [예외 처리] 데이터가 부족할 경우 "비어있는 슬롯" 후보 추가
                results.Add(new RewardCandidate { 
                    category = category, 
                    displayData = new GrowthItemData { itemName = "None", description = "No more rewards available." },
                    rawData = null 
                });
            }
        }
        return results;
    }

    // --- 3. 상점용: 꾸러미 생성 ---
    public static List<RewardCandidate> GenerateShopRoom(DataManager data)
    {
        List<RewardCandidate> results = new List<RewardCandidate>();
        var shopRegistry = data.SHOP_REGISTRY;

        if (shopRegistry == null) return results;

        List<RewardCandidate> combinedPool = new List<RewardCandidate>();

        // 상점은 아이템만 판다. 장비는 강화 상점(Room_ShopEnhance) NPC 에게 F 로 강화해서만 얻는다.

        // [아이템] 주머니 아이템. 가격은 티어에서 자동으로 나온다(ItemTierRules).
        // 사면 바로 주머니에 들어간다. 중첩 한도면 골드를 내기 전에 구매 자체가 막히고(ShopNPC.TryPurchase 의 CanOfferItem),
        // 주머니가 없을 때만 바닥에 떨어진다 — RewardManager.TryGivePurchase 참조.
        // ponytail: 티어별 등장 확률 가중치는 없다(전 티어 동일 확률). 필요해지면 여기에 가중치를 건다.
        if (shopRegistry.itemPool != null)
        {
            foreach (var so in shopRegistry.itemPool)
            {
                if (!CanOfferItem(so)) continue;
                combinedPool.Add(new RewardCandidate
                {
                    displayData = BuildItemDisplayData(so),
                    rawData = so,
                    category = RewardCategory.Item,
                    goldAmount = so.Price
                });
            }
        }

        // 젬 효과가 전부 제거되어 상점 젬 풀도 내렸다. (Legacy/History/Docs/GEM_LEGACY.md)

        // 랜덤하게 최대 5개 선택. 중복 제거 — 같은 카드를 여러 장 팔면 사는 족족 덮어쓰기만 되는
        // 항목이 있다. (예전엔 RemoveAt 이 주석 처리돼 있어서 5장 전부 같은 카드가 뜰 수 있었다.)
        for(int i = 0; i < 5 && combinedPool.Count > 0; i++)
        {
            int idx = Random.Range(0, combinedPool.Count);
            results.Add(combinedPool[idx]);
            combinedPool.RemoveAt(idx);
        }

        return results;
    }

    // --- 세부 필터링 로직 ---

    /// <summary>
    /// 소환수 보상 후보. roleType 을 주면 그 타입만 걸러낸다.
    /// GrowthRegistry 의 리스트를 역할별로 쪼개지 않고 필터만 건다 —
    /// 레지스트리 새로고침(RegistryMenuTools)이 새 메인 소환수를 이 한 목록 맨 뒤에 붙이므로 리스트를 쪼개지 않는다.
    /// </summary>
    /// <param name="roleType">typeof(MainMinionDataSO). null 이면 전부. (서브 삭제 후 사실상 메인뿐)</param>
    private static List<RewardCandidate> GetValidCores(InventoryManager inven, List<MinionDataSO> minions, bool filterOwned = true, System.Type roleType = null)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var m in minions)
        {
            if (m == null) continue;
            if (roleType != null && !roleType.IsInstanceOfType(m)) continue;
            if (m is MainMinionDataSO mm && mm.isEvolvedForm) continue; // 진화형은 진화로만 얻는다
            if (!filterOwned || !inven.HasMinionInSlots(m))
                candidates.Add(new RewardCandidate { displayData = BuildMinionDisplayData(m), rawData = m, techIndex = 0, category = RewardCategory.Minion });
        }
        return candidates;
    }

    // 소환수 보상 카드의 표시 데이터. 무엇을 보여줄지는 소환수 타입이 스스로 답한다
    // (메인=액티브 스킬 설명, 서브=패시브 수치에서 생성). MinionDataSO.ResolveDescription 참조.
    private static GrowthItemData BuildMinionDisplayData(MinionDataSO minion)
    {
        var baseData = minion.rewardItemData;

        // [안전장치] baseData가 존재하더라도 필드가 비어있으면 에셋 기본정보로 대체(Fallback)
        string finalName = (baseData != null && !string.IsNullOrEmpty(baseData.itemName)) ? baseData.itemName : minion.minionName;
        Sprite finalIcon = (baseData != null && baseData.icon != null) ? baseData.icon : minion.minionIcon;

        return new GrowthItemData
        {
            itemName = finalName,
            description = minion.ResolveDescription(),
            localizedItemName = baseData != null ? baseData.localizedItemName : null,
            localizedDescription = baseData != null ? baseData.localizedDescription : null,
            icon = finalIcon,
            rarity = baseData != null ? baseData.rarity : default
        };
    }

    private static List<RewardCandidate> GetValidTreasures(List<TreasureSO> treasures)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var t in treasures)
            candidates.Add(new RewardCandidate { displayData = new GrowthItemData { itemName = t.itemName, description = t.description, icon = t.icon, rarity = t.rarity, localizedItemName = t.localizedItemName, localizedDescription = t.localizedDescription }, rawData = t, category = RewardCategory.Treasure });
        return candidates;
    }

}

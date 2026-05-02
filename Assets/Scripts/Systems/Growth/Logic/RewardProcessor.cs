using System.Collections.Generic;
using UnityEngine;

public enum RoomType { Normal, Elite, Reward }
public enum RewardCategory { Minion, Metamorphosis, Gem, Treasure, Gold }

/// <summary>
/// 보상으로 제안될 아이템 정보를 담는 구조체입니다.
/// </summary>
public struct RewardCandidate
{
    public GrowthItemData displayData; 
    public object rawData;             
    public int techIndex;              // 계보일 경우 진화 단계
    public RewardCategory category;
    public int goldAmount;             
}

public static class RewardProcessor
{
    // --- 1. 일반 방용: 고정 꾸러미 생성 ---
    public static List<RewardCandidate> GenerateNormalRoomRewards(InventoryManager inven, DataManager data)
    {
        List<RewardCandidate> results = new List<RewardCandidate>();
        var registry = data.GET_GROWTH_REGISTRY();

        // 1. 보석 (확정 1개)
        var gemPool = GetValidGems(inven, registry.gems);
        if (gemPool.Count > 0) results.Add(gemPool[Random.Range(0, gemPool.Count)]);

        // 2. 골드 (확정)
        results.Add(new RewardCandidate { category = RewardCategory.Gold, goldAmount = Random.Range(30, 51) });

        // 3. 보물 (확률 30%)
        if (Random.value < 0.3f)
        {
            var treasurePool = GetValidTreasures(registry.treasures);
            if (treasurePool.Count > 0) results.Add(treasurePool[Random.Range(0, treasurePool.Count)]);
        }

        return results;
    }

    // --- 2. 엘리트/보상 방용: 카테고리별 후보 생성 (3개 추출) ---
    public static List<RewardCandidate> GenerateCandidatesByCategory(InventoryManager inven, DataManager data, RewardCategory category, int count)
    {
        List<RewardCandidate> allPossible = new List<RewardCandidate>();
        var registry = data.GET_GROWTH_REGISTRY();

        switch (category)
        {
            case RewardCategory.Minion:
                allPossible.AddRange(GetValidCores(inven, registry.minionLineages));
                break;
            case RewardCategory.Metamorphosis:
                allPossible.AddRange(GetValidMetamorphoses(inven, registry.minionLineages));
                break;
            case RewardCategory.Gem:
                allPossible.AddRange(GetValidGems(inven, registry.gems));
                break;
            case RewardCategory.Treasure:
                allPossible.AddRange(GetValidTreasures(registry.treasures));
                break;
        }

        // 랜덤 셔플 및 count만큼 추출
        List<RewardCandidate> results = new List<RewardCandidate>();
        for (int i = 0; i < count; i++)
        {
            if (allPossible.Count == 0) break;
            int idx = Random.Range(0, allPossible.Count);
            results.Add(allPossible[idx]);
            allPossible.RemoveAt(idx);
        }
        return results;
    }

    // --- 세부 필터링 로직 ---

    private static List<RewardCandidate> GetValidCores(InventoryManager inven, List<MinionLineageSO> lineages)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var lin in lineages)
        {
            // 부대에 없는 직업만 코어로 제안
            if (!inven.HasLineageInSlots(lin))
                candidates.Add(new RewardCandidate { displayData = lin.baseItemData, rawData = lin, techIndex = 0, category = RewardCategory.Minion });
        }
        return candidates;
    }

    private static List<RewardCandidate> GetValidMetamorphoses(InventoryManager inven, List<MinionLineageSO> lineages)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var lin in lineages)
        {
            // 이미 부대에 있고, 아직 진화 전인 경우만 환골탈태 제안
            var slot = inven.Slots.Find(s => s.EquippedLineage == lin);
            if (slot != null && slot.EvolutionIndex == 0)
            {
                if (lin.techA != null) candidates.Add(new RewardCandidate { displayData = lin.techAItemData, rawData = lin, techIndex = 1, category = RewardCategory.Metamorphosis });
                if (lin.techB != null) candidates.Add(new RewardCandidate { displayData = lin.techBItemData, rawData = lin, techIndex = 2, category = RewardCategory.Metamorphosis });
            }
        }
        return candidates;
    }

    private static List<RewardCandidate> GetValidGems(InventoryManager inven, List<GemSO> gems)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var gem in gems)
        {
            if (inven.HasJobInSlots(gem.targetJob))
                candidates.Add(new RewardCandidate { displayData = new GrowthItemData { itemName = gem.itemName, description = gem.description, icon = gem.icon, rarity = gem.rarity }, rawData = gem, category = RewardCategory.Gem });
        }
        return candidates;
    }

    private static List<RewardCandidate> GetValidTreasures(List<TreasureSO> treasures)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var t in treasures)
            candidates.Add(new RewardCandidate { displayData = new GrowthItemData { itemName = t.itemName, description = t.description, icon = t.icon, rarity = t.rarity }, rawData = t, category = RewardCategory.Treasure });
        return candidates;
    }
}

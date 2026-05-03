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
    public CommandData targetJob;      // [추가] 보석이나 특정 직업 전용 아이템일 경우 사용
}

/// <summary>
/// [역할: 계산기] 보상 시스템의 수학적/논리적 판단을 담당하는 정적 클래스입니다.
/// - 담당: 인벤토리 분석, 유효한 보상 필터링, 확률 기반 랜덤 추출.
/// - 활용: RewardManager나 상점 시스템에서 "제공할 보상 리스트"가 필요할 때 호출합니다.
/// - 특징: UI나 게임 상태를 직접 수정하지 않으며, 오직 데이터(RewardCandidate)만 생성합니다.
/// </summary>
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

    // --- 2. 엘리트/보상 방용: 카테고리별 후보 생성 (무조건 3개 슬롯 반환) ---
    public static List<RewardCandidate> GenerateCandidatesByCategory(InventoryManager inven, DataManager data, RewardCategory category, int count = 3)
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
                    displayData = new GrowthItemData { itemName = "없음", description = "더 이상 획득할 수 있는 보상이 없습니다." },
                    rawData = null 
                });
            }
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
        
        // 현재 플레이어가 슬롯에 가지고 있는 모든 직업 리스트 추출
        HashSet<CommandData> playerJobs = new HashSet<CommandData>();
        foreach(var slot in inven.Slots)
        {
            if (slot.EquippedLineage != null) playerJobs.Add(slot.EquippedLineage.jobType);
        }

        foreach (var gem in gems)
        {
            if (gem.isUniversal)
            {
                // 범용 보석은 플레이어가 가진 각 직업별로 후보를 생성 (6가지 바리에이션 가능)
                foreach (var job in playerJobs)
                {
                    candidates.Add(new RewardCandidate { 
                        displayData = gem.GetDynamicDisplayData(job), 
                        rawData = gem, 
                        category = RewardCategory.Gem,
                        targetJob = job
                    });
                }
            }
            else
            {
                // 전용 보석은 해당 직업을 플레이어가 가지고 있을 때만 생성
                if (playerJobs.Contains(gem.targetJob))
                {
                    candidates.Add(new RewardCandidate { 
                        displayData = gem.GetDynamicDisplayData(gem.targetJob), 
                        rawData = gem, 
                        category = RewardCategory.Gem,
                        targetJob = gem.targetJob
                    });
                }
            }
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

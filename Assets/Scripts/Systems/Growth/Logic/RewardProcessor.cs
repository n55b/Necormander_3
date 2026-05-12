using System.Collections.Generic;
using UnityEngine;

public enum RoomType { Normal, Elite, Reward }
public enum RewardCategory { Minion, Metamorphosis, Gem, Treasure, Gold, Ability }

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

        // [사용자 요청] 소환수(Minion) + 보석(Gem)을 합친 풀에서 랜덤으로 3개 추출
        List<RewardCandidate> combinedPool = new List<RewardCandidate>();
        
        // 1. 소환수 풀 (이제 이미 있어도 제안함)
        combinedPool.AddRange(GetValidCores(inven, registry.minionLineages, false));
        
        // 2. 보석 풀
        combinedPool.AddRange(GetValidGems(inven, registry.gems));

        // 랜덤하게 3개 선택
        for (int i = 0; i < 3; i++)
        {
            if (combinedPool.Count > 0)
            {
                int idx = Random.Range(0, combinedPool.Count);
                results.Add(combinedPool[idx]);
                // 소환수/보석 종류 중복 노출을 피하고 싶다면 아래 주석 해제
                // combinedPool.RemoveAt(idx); 
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
                allPossible.AddRange(GetValidCores(inven, registry.minionLineages, false)); // [수정] 중복 허용
                break;
            case RewardCategory.Metamorphosis:
                allPossible.AddRange(GetValidMetamorphoses(inven, registry.minionLineages));
                break;
            case RewardCategory.Gem:
                allPossible.AddRange(GetValidGems(inven, registry.gems));
                break;
            case RewardCategory.Treasure:
                // [참고] 보물은 다른 방식으로 획득할 예정이므로 여기서 제안하지 않을 수 있음
                allPossible.AddRange(GetValidTreasures(registry.treasures));
                break;
            case RewardCategory.Ability:
                allPossible.AddRange(GetValidAbilities(inven, registry));
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
    public static List<PrizeDataSO> GenerateShopRoom(DataManager data)
    {
        List<PrizeDataSO> results = new List<PrizeDataSO>();
        var registry = data.GET_GROWTH_REGISTRY();

        List<PrizeDataSO> combinedPool = new List<PrizeDataSO>();

        foreach(var prize in data.PRIZE_DATA)
        {
            combinedPool.Add(prize);
        }

        // 랜덤하게 5개 선택
        for(int i = 0; i < 5; i++)
        {
            if(combinedPool.Count > 0)
            {
                int idx = Random.Range(0, combinedPool.Count);
                results.Add(combinedPool[idx]);
            }
        }

        return results;
    }

    // --- 세부 필터링 로직 ---

    private static List<RewardCandidate> GetValidCores(InventoryManager inven, List<MinionLineageSO> lineages, bool filterOwned = true)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var lin in lineages)
        {
            // [수정] filterOwned가 false면 이미 가지고 있어도 후보에 포함
            if (!filterOwned || !inven.HasLineageInSlots(lin))
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
            // [수정] 보석은 이제 전역 효과이므로, 특정 직업에 구애받지 않고 모든 보석을 후보에 포함합니다.
            // 다만 내부 데이터 생성을 위해 기본 직업(SkeletonWarrior)을 사용하며, UI에는 직업이 노출되지 않도록 합니다.
            candidates.Add(new RewardCandidate { 
                displayData = gem.GetDynamicDisplayData(CommandData.SkeletonWarrior), 
                rawData = gem, 
                category = RewardCategory.Gem,
                targetJob = CommandData.SkeletonWarrior // 내부 우회용 기본값
            });
        }
        return candidates;
    }

    // --- 3. 엘리트 방용: 여러 카테고리를 섞어서 후보 생성 (예: Minion + Ability) ---
    public static List<RewardCandidate> GenerateMixedCandidates(InventoryManager inven, DataManager data, List<RewardCategory> categories, int count = 3)
    {
        List<RewardCandidate> allPossible = new List<RewardCandidate>();
        var registry = data.GET_GROWTH_REGISTRY();

        // [디버깅 로그 추가]
        int mCount = GetValidCores(inven, registry.minionLineages).Count;
        int aCount = GetValidAbilities(inven, registry).Count;
        int tCount = GetValidMetamorphoses(inven, registry.minionLineages).Count;
        Debug.Log($"<color=white>[Reward:Pool]</color> Valid Pool Size -> Minions: {mCount}, Abilities: {aCount}, Metamorphosis: {tCount}");

        foreach (var category in categories)
        {
            switch (category)
            {
                case RewardCategory.Minion:
                    allPossible.AddRange(GetValidCores(inven, registry.minionLineages));
                    break;
                case RewardCategory.Ability:
                    allPossible.AddRange(GetValidAbilities(inven, registry));
                    break;
                case RewardCategory.Metamorphosis:
                    allPossible.AddRange(GetValidMetamorphoses(inven, registry.minionLineages));
                    break;
                case RewardCategory.Gem:
                    allPossible.AddRange(GetValidGems(inven, registry.gems));
                    break;
            }
        }

        List<RewardCandidate> results = new List<RewardCandidate>();
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
                results.Add(new RewardCandidate { 
                    category = RewardCategory.Gold, // 폴백으로 골드 
                    displayData = new GrowthItemData { itemName = "None", description = "No more rewards available." },
                    rawData = null 
                });
            }
        }
        return results;
    }

    private static List<RewardCandidate> GetValidTreasures(List<TreasureSO> treasures)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var t in treasures)
            candidates.Add(new RewardCandidate { displayData = new GrowthItemData { itemName = t.itemName, description = t.description, icon = t.icon, rarity = t.rarity }, rawData = t, category = RewardCategory.Treasure });
        return candidates;
    }

    private static List<RewardCandidate> GetValidAbilities(InventoryManager inven, GrowthRegistrySO registry)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var item in registry.specialAbilities)
        {
            if (item is ThrowAbilitySO ability)
            {
                // 이미 장착 중인 능력은 제외 (중복 장착 방지)
                if (inven.ActiveAbilities.Exists(a => a.GetType() == ability.GetType())) continue;

                candidates.Add(new RewardCandidate { 
                    displayData = new GrowthItemData { itemName = ability.itemName, description = ability.description, icon = ability.icon, rarity = ability.rarity }, 
                    rawData = ability, 
                    category = RewardCategory.Ability 
                });
            }
        }
        return candidates;
    }
}

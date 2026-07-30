using System.Collections.Generic;
using UnityEngine;

public enum RoomType { Spawn, Normal, Elite, Reward, Shop, Boss }
// [26/07/17] Ability 는 투척 능력 전용이라 투척 철거와 함께 사라졌다.
// [26/07/30] Item = 주머니 아이템. 장비(Equipment)와 별개 시스템이다. 새 항목은 반드시 맨 끝에 붙인다.
public enum RewardCategory { Minion, Metamorphosis, Treasure, Gold, PlayerSkill, Equipment, EquipmentEnhance, Item }

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

/// <summary>
/// [역할: 계산기] 보상 시스템의 수학적/논리적 판단을 담당하는 정적 클래스입니다.
/// - 담당: 인벤토리 분석, 유효한 보상 필터링, 확률 기반 랜덤 추출.
/// - 활용: RewardManager나 상점 시스템에서 "제공할 보상 리스트"가 필요할 때 호출합니다.
/// - 특징: UI나 게임 상태를 직접 수정하지 않으며, 오직 데이터(RewardCandidate)만 생성합니다.
/// </summary>
public static class RewardProcessor
{
    /// <summary>강화 아이템의 rawData 센티넬(비-null 이어야 빈 슬롯 취급을 피한다). ApplyReward 는 category 로 분기하니 값 자체는 안 쓴다.</summary>
    public static readonly object EnhanceMarker = new object();

    // --- 1-A. 플레이어 스킬 방용: 플레이어 스킬 배출 ---
    public static List<RewardCandidate> GeneratePlayerSkillRewards(InventoryManager inven, DataManager data)
    {
        List<RewardCandidate> results = new List<RewardCandidate>();
        var registry = data.GET_GROWTH_REGISTRY();

        List<RewardCandidate> combinedPool = new List<RewardCandidate>();

        // 플레이어 스킬 풀 (이미 장착되어 있거나 보유 중인 스킬은 제외)
        combinedPool.AddRange(GetValidPlayerSkills(registry.playerSkills));

        // 랜덤하게 3개 선택 (중복 제거)
        for (int i = 0; i < 3; i++)
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
                    category = RewardCategory.PlayerSkill, 
                    displayData = new GrowthItemData { itemName = "None", description = "No more player rewards available." },
                    rawData = null 
                });
            }
        }

        return results;
    }

    // --- 1-A'. 장비 방용: 장비 배출 (각 후보는 뜨는 순간 스킬을 굴려 굳힌다) ---
    /// <summary>서로 다른 장비 최대 3개를 뽑는다. 각 후보는 그 자리에서 skillPool 을 굴려 EquipmentInstance 로 확정된다("장비 뜰 때 고정").</summary>
    public static List<RewardCandidate> GenerateEquipmentRewards(InventoryManager inven, DataManager data)
    {
        List<RewardCandidate> results = new List<RewardCandidate>();
        var registry = data.GET_GROWTH_REGISTRY();

        List<EquipmentSO> pool = new List<EquipmentSO>();
        if (registry != null && registry.equipments != null)
            foreach (var e in registry.equipments) if (e != null) pool.Add(e);

        for (int i = 0; i < 3; i++)
        {
            if (pool.Count > 0)
            {
                int idx = Random.Range(0, pool.Count);
                var so = pool[idx];
                pool.RemoveAt(idx); // 같은 장비 중복 노출 방지
                var inst = EquipmentInstance.Roll(so); // 뜨는 순간 스킬 고정
                results.Add(new RewardCandidate
                {
                    displayData = BuildEquipmentDisplayData(so, inst),
                    rawData = inst,
                    category = RewardCategory.Equipment
                });
            }
            else
            {
                results.Add(new RewardCandidate {
                    category = RewardCategory.Equipment,
                    displayData = new GrowthItemData { itemName = "None", description = "No more equipment available." },
                    rawData = null
                });
            }
        }
        return results;
    }

    private static GrowthItemData BuildEquipmentDisplayData(EquipmentSO so, EquipmentInstance inst)
    {
        string skills = "";
        if (inst != null && inst.rolledSkills.Count > 0)
        {
            var names = new List<string>();
            foreach (var s in inst.rolledSkills)
                if (s != null) names.Add(string.IsNullOrEmpty(s.skillName) ? s.name : s.skillName);
            if (names.Count > 0) skills = "\n[" + string.Join(" / ", names) + "]";
        }
        return new GrowthItemData
        {
            itemName = string.IsNullOrEmpty(so.equipmentName) ? so.name : so.equipmentName,
            description = so.description + skills,
            icon = so.icon
        };
    }

    /// <summary>주머니 아이템의 상점 카드 표시 데이터. 등급 표기가 설명 앞에 붙는다.</summary>
    public static GrowthItemData BuildItemDisplayData(ItemSO so)
        => new GrowthItemData
        {
            itemName = so.DisplayName,
            description = so.TooltipBody,
            icon = so.icon
        };

    /// <summary>레지스트리 전체에서 아이템 하나를 랜덤으로. 엘리트 드랍용. 없으면 null.</summary>
    public static ItemSO PickRandomItem(DataManager data)
    {
        var registry = data != null ? data.GET_GROWTH_REGISTRY() : null;
        if (registry == null || registry.items == null) return null;

        var pool = new List<ItemSO>();
        foreach (var it in registry.items) if (it != null) pool.Add(it);
        return pool.Count == 0 ? null : pool[Random.Range(0, pool.Count)];
    }

    // --- 1-B. 미니언 스킬 방용: 소환수 코어 배출 ---
    /// <summary>지정한 역할의 소환수 카드 3장을 뽑는다. 메인/서브 풀은 타입으로만 갈린다.</summary>
    /// <param name="roleType">typeof(MainMinionDataSO) 또는 typeof(SubMinionDataSO).</param>
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
                allPossible.AddRange(GetValidCores(inven, registry.minionDatas, true)); // [수정] 중복 제외
                break;
            case RewardCategory.Metamorphosis:
                // 변이/승급 시스템 삭제로 아무것도 생성 안함
                break;
            case RewardCategory.Treasure:
                // [참고] 보물은 다른 방식으로 획득할 예정이므로 여기서 제안하지 않을 수 있음
                allPossible.AddRange(GetValidTreasures(registry.treasures));
                break;
            case RewardCategory.PlayerSkill:
                allPossible.AddRange(GetValidPlayerSkills(registry.playerSkills));
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

        // 설계 4: 서브 소환수는 상점에서도 등장한다. 메인은 보상 방 전용.
        if (shopRegistry.minionPool != null)
        {
            foreach(var minion in shopRegistry.minionPool)
            {
                if (minion == null || minion is not SubMinionDataSO) continue;
                combinedPool.Add(new RewardCandidate
                {
                    displayData = BuildMinionDisplayData(minion),
                    rawData = minion,
                    techIndex = 0,
                    category = RewardCategory.Minion,
                    goldAmount = minion.shopCost
                });
            }
        }

        // [장비] 상점에서 장비 구매 = 현재 장비 교체(구매 시 스킬 2개 새로 리롤). 뜰 때 인스턴스로 굳힌다.
        if (shopRegistry.equipmentPool != null)
        {
            foreach (var so in shopRegistry.equipmentPool)
            {
                if (so == null) continue;
                var inst = EquipmentInstance.Roll(so);
                combinedPool.Add(new RewardCandidate
                {
                    displayData = BuildEquipmentDisplayData(so, inst),
                    rawData = inst,
                    category = RewardCategory.Equipment,
                    goldAmount = so.shopCost
                });
            }
        }

        // [강화] 소모성 강화 아이템 — 사면 착용 장비 +1강. enhanceStock 개를 풀에 넣어 랜덤으로 0~N개 진열된다.
        for (int e = 0; e < shopRegistry.enhanceStock; e++)
        {
            combinedPool.Add(new RewardCandidate
            {
                displayData = new GrowthItemData
                {
                    itemName = "장비 강화 +1",
                    description = "착용한 장비의 강화 레벨을 1 올린다(스킬·패시브 강화). 장비가 없으면 살 수 없다.",
                    icon = shopRegistry.enhanceIcon
                },
                rawData = EnhanceMarker,       // 비-null 센티넬(빈 슬롯 취급 방지). 실제 동작은 category 로 분기.
                category = RewardCategory.EquipmentEnhance,
                goldAmount = shopRegistry.enhanceCost
            });
        }

        // [아이템] 주머니 아이템. 가격은 티어에서 자동으로 나온다(ItemTierRules).
        // 사면 즉시 장착되지 않고 바닥에 떨어진다 — RewardManager 의 Item 분기 참조.
        // ponytail: 티어별 등장 확률 가중치는 없다(전 티어 동일 확률). 필요해지면 여기에 가중치를 건다.
        if (shopRegistry.itemPool != null)
        {
            foreach (var so in shopRegistry.itemPool)
            {
                if (so == null) continue;
                combinedPool.Add(new RewardCandidate
                {
                    displayData = BuildItemDisplayData(so),
                    rawData = so,
                    category = RewardCategory.Item,
                    goldAmount = so.Price
                });
            }
        }

        // 젬 효과가 전부 제거되어 상점 젬 풀도 내렸다. (GEM_LEGACY.md)

        // 랜덤하게 최대 5개 선택. 중복 제거 — 서브 슬롯이 1칸뿐이라 같은 카드를 여러 장 팔면
        // 사는 족족 덮어쓰기만 된다. (예전엔 RemoveAt 이 주석 처리돼 있어서 5장 전부 같은 카드가
        // 뜰 수 있었다.)
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
    /// ponytail: GrowthRegistry 의 리스트를 메인/서브로 쪼개지 않고 필터만 건다 —
    /// RefreshRegistry 가 t:MinionDataSO 를 자동 스캔하는데, 리스트를 쪼개면 그 자동화가 깨진다.
    /// </summary>
    /// <param name="roleType">typeof(MainMinionDataSO) / typeof(SubMinionDataSO). null 이면 전부.</param>
    private static List<RewardCandidate> GetValidCores(InventoryManager inven, List<MinionDataSO> minions, bool filterOwned = true, System.Type roleType = null)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        foreach (var m in minions)
        {
            if (m == null) continue;
            if (roleType != null && !roleType.IsInstanceOfType(m)) continue;
            if (!filterOwned || !inven.HasMinionInSlots(m))
                candidates.Add(new RewardCandidate { displayData = BuildMinionDisplayData(m), rawData = m, techIndex = 0, category = RewardCategory.Minion });
        }
        return candidates;
    }

        // 이미 장착되어 있거나(equippedSkills) 보유 중인(ownedSkills) 스킬은 제외하고 후보만 제안합니다.합니다.
    private static List<RewardCandidate> GetValidPlayerSkills(List<PlayerSkillSO> playerSkills)
    {
        List<RewardCandidate> candidates = new List<RewardCandidate>();
        if (playerSkills == null) return candidates;

        var pInven = PlayerSkillInventoryManager.Instance;

        foreach (var skill in playerSkills)
        {
            if (skill == null) continue;

            if (pInven != null)
            {
                bool alreadyOwned = pInven.GetOwnedSkills().Contains(skill);
                bool alreadyEquipped = false;
                for (int i = 0; i < 3; i++)
                {
                    if (pInven.GetEquipped(i) == skill) { alreadyEquipped = true; break; }
                }
                if (alreadyOwned || alreadyEquipped) continue;
            }

            candidates.Add(new RewardCandidate
            {
                displayData = BuildPlayerSkillDisplayData(skill),
                rawData = skill,
                category = RewardCategory.PlayerSkill
            });
        }
        return candidates;
    }

    private static GrowthItemData BuildPlayerSkillDisplayData(PlayerSkillSO skill)
    {
        return new GrowthItemData
        {
            itemName = skill.skillName,
            description = skill.description,
            icon = skill.icon
        };
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

    // --- 3. 엘리트 방용: 여러 카테고리를 섞어서 후보 생성 ---
    public static List<RewardCandidate> GenerateMixedCandidates(InventoryManager inven, DataManager data, List<RewardCategory> categories, int count = 3)
    {
        List<RewardCandidate> allPossible = new List<RewardCandidate>();
        var registry = data.GET_GROWTH_REGISTRY();

        foreach (var category in categories)
        {
            switch (category)
            {
                case RewardCategory.Minion:
                    allPossible.AddRange(GetValidCores(inven, registry.minionDatas));
                    break;
                case RewardCategory.PlayerSkill:
                    allPossible.AddRange(GetValidPlayerSkills(registry.playerSkills));
                    break;
                case RewardCategory.Metamorphosis:
                    // 변이/승급 삭제
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
            candidates.Add(new RewardCandidate { displayData = new GrowthItemData { itemName = t.itemName, description = t.description, icon = t.icon, rarity = t.rarity, localizedItemName = t.localizedItemName, localizedDescription = t.localizedDescription }, rawData = t, category = RewardCategory.Treasure });
        return candidates;
    }

}

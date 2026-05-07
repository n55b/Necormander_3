using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 슬롯과 보물 인벤토리를 관리하는 핵심 매니저입니다.
/// </summary>
public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance;

    [System.Serializable]
    public class CoreSlot
    {
        public bool IsShattered; 
        public MinionLineageSO EquippedLineage; 
        public ThrowAbilitySO EquippedThrowAbility; // [추가] 던지기 능력 장착 슬롯
        public int EvolutionIndex; 
        public int Quantity; 
        
        public bool IsEmpty => !IsShattered && EquippedLineage == null && EquippedThrowAbility == null;

        public MinionDataSO GetCurrentMinionData() => EquippedLineage != null ? EquippedLineage.GetForm(EvolutionIndex) : null;
        public GrowthItemData GetCurrentItemData()
        {
            if (EquippedLineage != null) return EquippedLineage.GetItemData(EvolutionIndex);
            if (EquippedThrowAbility != null) return new GrowthItemData { itemName = EquippedThrowAbility.itemName, description = EquippedThrowAbility.description, icon = EquippedThrowAbility.icon, rarity = EquippedThrowAbility.rarity };
            return null;
        }
    }

    [Header("자원 관리")]
    [SerializeField] private int gold = 0;
    public int GOLD => gold;

    [Header("슬롯 시스템 (10개 고정)")]
    public List<CoreSlot> Slots = new List<CoreSlot>(10);

    [Header("Debug Settings (Starting Items)")]
    [SerializeField] private bool useDebugStartingInventory = true;
    [SerializeField] private List<DebugSlotConfig> debugStartingSlots = new List<DebugSlotConfig>();

    [System.Serializable]
    public struct DebugSlotConfig
    {
        public MinionLineageSO minion;
        public ThrowAbilitySO ability;
        public int quantity;
    }

    [Header("보석 보관함 (직업별)")]
    public Dictionary<CommandData, List<GemSO>> EquippedGems = new Dictionary<CommandData, List<GemSO>>();

    [Header("보물 인벤토리 (중첩)")]
    public Dictionary<TreasureSO, int> TreasureStacks = new Dictionary<TreasureSO, int>();

    // [추가] 현재 활성화된 던지기 능력 리스트 (투척 시스템에서 참조용)
    private List<ThrowAbilitySO> _activeAbilities = new List<ThrowAbilitySO>();
    public List<ThrowAbilitySO> ActiveAbilities => _activeAbilities;

    public void Initialize()
    {
        Instance = this;
        // [수정] 0개일 때만 추가하는 게 아니라, 부족하면 10개가 될 때까지 채웁니다.
        while (Slots.Count < 10)
        {
            Slots.Add(new CoreSlot());
        }
        UpdateActiveAbilities();
        Debug.Log("<color=cyan>[InventoryManager]</color> Initialized.");

        // [디버깅용] 설정된 인벤토리 아이템 지급
        if (useDebugStartingInventory)
        {
            Debug_InitializeInventory();
        }
    }

    private void Debug_InitializeInventory()
    {
        if (debugStartingSlots == null || debugStartingSlots.Count == 0)
        {
            // 설정이 없으면 기본 전사 지급
            bool success = AddMinionOrIncreaseQuantity(CommandData.SkeletonWarrior);
            Debug.Log($"<color=white>[Inventory:Debug]</color> Default Warrior initialization {(success ? "Success" : "Failed")}");
            return;
        }

        foreach (var config in debugStartingSlots)
        {
            // 1. 미니언 추가
            if (config.minion != null)
            {
                bool success = AddMinionOrIncreaseQuantity(config.minion.jobType, Mathf.Max(1, config.quantity));
                Debug.Log($"<color=white>[Inventory:Debug]</color> {config.minion.jobType} initialization {(success ? "Success" : "Failed")}");
            }
            
            // 2. 능력 추가 (미니언이 방금 추가되었다면 다음 빈 슬롯을 찾음)
            if (config.ability != null)
            {
                // 빈 슬롯 중 가장 빠른 곳에 장착
                int emptyIdx = Slots.FindIndex(s => s.IsEmpty);
                if (emptyIdx != -1)
                {
                    bool success = EquipThrowAbility(emptyIdx, config.ability);
                    Debug.Log($"<color=white>[Inventory:Debug]</color> {config.ability.itemName} initialization {(success ? "Success" : "Failed")} (Slot: {emptyIdx})");
                }
            }
        }

        // [추가] 유저 요청: 디버그 설정을 다 마친 후에도 인벤토리에 미니언이 단 한 마리도 없다면,
        // 최소한의 플레이를 위해 기본 전사를 빈 슬롯에 추가합니다.
        if (!Slots.Exists(s => s.EquippedLineage != null))
        {
            AddMinionOrIncreaseQuantity(CommandData.SkeletonWarrior);
            Debug.Log("<color=white>[Inventory:Debug]</color> No minions found after initialization. Added default Warrior as fallback.");
        }
    }

    private void Debug_InitializeDefaultMinion() { } // 기존 메서드 호환용 (사용 안함)

    // 슬롯 변경 시마다 활성화된 능력 리스트를 갱신합니다.
    public void UpdateActiveAbilities()
    {
        _activeAbilities.Clear();
        foreach (var slot in Slots)
        {
            if (slot.EquippedThrowAbility != null)
                _activeAbilities.Add(slot.EquippedThrowAbility);
        }
    }

    #region Gold System
    public void AddGold(int amount)
    {
        gold += amount;
        Debug.Log($"<color=yellow>[Economy]</color> 골드 획득: {amount}. 현재 골드: {gold}");
    }

    public bool SpendGold(int amount)
    {
        if (gold >= amount)
        {
            gold -= amount;
            return true;
        }
        return false;
    }
    #endregion

    #region Gem System
    public float GetGemBonus(CommandData job, StatType stat)
    {
        if (!EquippedGems.ContainsKey(job)) return 0f;

        float totalBonus = 0f;
        foreach (var gem in EquippedGems[job])
        {
            // [수정] 슬롯이 비어있을(null) 수 있으므로 체크 추가
            if (gem != null && gem.statType == stat)
            {
                totalBonus += gem.baseBonusValue;
            }
        }
        return totalBonus;
    }

    public bool EquipGem(CommandData job, GemSO gem, int gemSlotIndex)
    {
        if (!HasJobInSlots(job))
        {
            Debug.LogWarning($"<color=orange>[Inventory]</color> {job} unit is not in slots, cannot equip gem.");
            return false;
        }

        // [추가] 보석의 직업 적합성 체크
        if (!gem.IsEligible(job))
        {
            Debug.LogWarning($"<color=orange>[Inventory]</color> {job} cannot equip {gem.itemName} due to job restrictions.");
            return false;
        }

        if (!EquippedGems.ContainsKey(job)) 
        {
            EquippedGems[job] = new List<GemSO> { null, null };
        }
        else if (EquippedGems[job].Count < 2)
        {
            // 리스트 크기가 2가 아니면 보정
            while (EquippedGems[job].Count < 2) EquippedGems[job].Add(null);
        }

        if (gemSlotIndex >= 0 && gemSlotIndex < 2)
        {
            EquippedGems[job][gemSlotIndex] = gem;
            Debug.Log($"<color=green>[Inventory]</color> Equipped {gem.itemName} to {job}'s Gem Slot {gemSlotIndex + 1}");
            return true;
        }
        
        return false;
    }

    public List<GemSO> GetEquippedGems(CommandData job)
    {
        if (EquippedGems.TryGetValue(job, out var gems)) return gems;
        return null;
    }
    #endregion

    #region Slot Management
    public bool AddMinionOrIncreaseQuantity(CommandData job, int amount = 1)
    {
        var existingSlot = Slots.Find(s => !s.IsShattered && s.EquippedLineage != null && s.EquippedLineage.jobType == job);
        
        if (existingSlot != null)
        {
            existingSlot.Quantity += amount;
            Debug.Log($"<color=green>[Inventory]</color> {job} 수량 증가: {existingSlot.Quantity} (추가: {amount})");
            return true;
        }

        var registry = GameManager.Instance.dataManager.GET_GROWTH_REGISTRY();
        if (registry == null) return false;

        MinionLineageSO targetLineage = registry.minionLineages.Find(lin => lin.jobType == job);
        if (targetLineage == null) return false;

        int emptyIdx = Slots.FindIndex(s => s.IsEmpty);
        if (emptyIdx != -1)
        {
            EquipLineage(emptyIdx, targetLineage);
            Slots[emptyIdx].Quantity = amount;
            return true;
        }
        return false;
    }

    public bool EquipLineage(int slotIndex, MinionLineageSO lineage)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count || Slots[slotIndex].IsShattered) return false;
        
        Slots[slotIndex].EquippedThrowAbility = null;
        Slots[slotIndex].EquippedLineage = lineage;
        Slots[slotIndex].EvolutionIndex = 0;
        Slots[slotIndex].Quantity = 1;
        
        UpdateActiveAbilities();
        return true;
    }

    /// <summary>
    /// 던지기 능력을 슬롯에 장착합니다.
    /// </summary>
    public bool EquipThrowAbility(int slotIndex, ThrowAbilitySO ability)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count || Slots[slotIndex].IsShattered) return false;

        // [중복 체크] 클래스 타입을 기반으로 이미 같은 능력을 장착하고 있는지 확인
        if (ActiveAbilities.Exists(a => a.GetType() == ability.GetType()))
        {
            Debug.LogWarning($"<color=orange>[Inventory]</color> 이미 동일한 종류의 능력을 장착하고 있습니다 ({ability.itemName}).");
            return false;
        }

        // 기존에 미니언이 있었다면 제거
        Slots[slotIndex].EquippedLineage = null;
        Slots[slotIndex].Quantity = 0;

        Slots[slotIndex].EquippedThrowAbility = ability;

        UpdateActiveAbilities();
        Debug.Log($"<color=green>[Inventory]</color> 던지기 능력 {ability.itemName} 장착 완료 (Slot: {slotIndex})");
        return true;
    }

    public void ApplyMetamorphosis(MinionLineageSO lineage, int index)
    {
        var slot = Slots.Find(s => s.EquippedLineage == lineage);
        if (slot != null)
        {
            slot.EvolutionIndex = index;
            Debug.Log($"<color=purple>[Growth]</color> {lineage.lineageName} 환골탈태! 단계: {index}");
        }
    }

    public void ShatterSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Slots.Count)
        {
            Slots[slotIndex].IsShattered = true;
            Slots[slotIndex].EquippedLineage = null;
            Slots[slotIndex].EquippedThrowAbility = null;
        }
    }
    #endregion

    public bool HasLineageInSlots(MinionLineageSO lineage) => Slots.Exists(s => s.EquippedLineage == lineage);
    public bool HasJobInSlots(CommandData job) => Slots.Exists(s => s.EquippedLineage != null && s.EquippedLineage.jobType == job);

    public void AddTreasure(TreasureSO treasure)
    {
        if (TreasureStacks.ContainsKey(treasure)) TreasureStacks[treasure]++;
        else TreasureStacks[treasure] = 1;
    }

    public float GetTreasureBonus(TreasureEffectType type)
    {
        float totalBonus = 0f;
        foreach (var kvp in TreasureStacks)
        {
            if (kvp.Key.effectType == type)
            {
                totalBonus += kvp.Key.valuePerStack * kvp.Value;
            }
        }
        return totalBonus;
    }
}

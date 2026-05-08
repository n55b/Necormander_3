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
        public ThrowAbilitySO EquippedThrowAbility;
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
        [Tooltip("이 미니언에게 장착할 첫 번째 보석")]
        public GemSO gem1;
        [Tooltip("이 미니언에게 장착할 두 번째 보석")]
        public GemSO gem2;
    }

    [Header("보석 보관함 (직업별)")]
    public Dictionary<CommandData, List<GemSO>> EquippedGems = new Dictionary<CommandData, List<GemSO>>();

    [Header("보물 인벤토리 (중첩)")]
    public Dictionary<TreasureSO, int> TreasureStacks = new Dictionary<TreasureSO, int>();

    private List<ThrowAbilitySO> _activeAbilities = new List<ThrowAbilitySO>();
    public List<ThrowAbilitySO> ActiveAbilities => _activeAbilities;

    public void Initialize()
    {
        Instance = this;
        while (Slots.Count < 10)
        {
            Slots.Add(new CoreSlot());
        }
        UpdateActiveAbilities();
        Debug.Log("<color=cyan>[InventoryManager]</color> Initialized.");

        if (useDebugStartingInventory)
        {
            Debug_InitializeInventory();
        }
    }

    private void Debug_InitializeInventory()
    {
        if (debugStartingSlots == null || debugStartingSlots.Count == 0)
        {
            AddMinionOrIncreaseQuantity(CommandData.SkeletonWarrior);
            return;
        }

        foreach (var config in debugStartingSlots)
        {
            if (config.minion != null)
            {
                bool success = AddMinionOrIncreaseQuantity(config.minion.jobType, Mathf.Max(1, config.quantity));
                Debug.Log($"<color=white>[Inventory:Debug]</color> {config.minion.jobType} initialization {(success ? "Success" : "Failed")}");
                
                if (success)
                {
                    if (config.gem1 != null) EquipGem(config.minion.jobType, config.gem1, 0);
                    if (config.gem2 != null) EquipGem(config.minion.jobType, config.gem2, 1);
                }
            }
            
            if (config.ability != null)
            {
                int emptyIdx = Slots.FindIndex(s => s.IsEmpty);
                if (emptyIdx != -1)
                {
                    bool success = EquipThrowAbility(emptyIdx, config.ability);
                    Debug.Log($"<color=white>[Inventory:Debug]</color> {config.ability.itemName} initialization {(success ? "Success" : "Failed")} (Slot: {emptyIdx})");
                }
            }
        }

        if (!Slots.Exists(s => s.EquippedLineage != null))
        {
            AddMinionOrIncreaseQuantity(CommandData.SkeletonWarrior);
            Debug.Log("<color=white>[Inventory:Debug]</color> No minions found after initialization. Added default Warrior as fallback.");
        }
    }

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
            if (gem is GemAttributeSO attrGem && attrGem.statType == stat)
            {
                totalBonus += attrGem.baseBonusValue;
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

    public bool EquipThrowAbility(int slotIndex, ThrowAbilitySO ability)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count || Slots[slotIndex].IsShattered) return false;

        if (ActiveAbilities.Exists(a => a.GetType() == ability.GetType()))
        {
            Debug.LogWarning($"<color=orange>[Inventory]</color> 이미 동일한 종류의 능력을 장착하고 있습니다 ({ability.itemName}).");
            return false;
        }

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

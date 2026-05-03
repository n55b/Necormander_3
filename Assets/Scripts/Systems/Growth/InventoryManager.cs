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
        if (Slots.Count == 0)
        {
            for (int i = 0; i < 10; i++) Slots.Add(new CoreSlot());
        }
        UpdateActiveAbilities();
        Debug.Log("<color=cyan>[InventoryManager]</color> Initialized.");

        // [디버깅용] 기본 전사 미니언 지급
        Debug_InitializeDefaultMinion();
    }

    private void Debug_InitializeDefaultMinion()
    {
        AddMinionOrIncreaseQuantity(CommandData.SkeletonWarrior);
    }

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
            if (gem.statType == stat)
            {
                totalBonus += gem.baseBonusValue;
            }
        }
        return totalBonus;
    }

    public bool EquipGem(CommandData job, GemSO gem)
    {
        if (!HasJobInSlots(job))
        {
            Debug.LogWarning($"<color=orange>[Inventory]</color> {job} 유닛이 슬롯에 없어 보석을 장착할 수 없습니다.");
            return false;
        }

        if (!EquippedGems.ContainsKey(job)) EquippedGems[job] = new List<GemSO>();

        if (EquippedGems[job].Count < 2)
        {
            EquippedGems[job].Add(gem);
            Debug.Log($"<color=green>[Inventory]</color> {job}에 보석 {gem.itemName} 장착 완료.");
            return true;
        }
        
        Debug.LogWarning($"<color=orange>[Inventory]</color> {job}의 보석 슬롯이 가득 찼습니다.");
        return false;
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

        if (ActiveAbilities.Exists(a => a.abilityType == ability.abilityType))
        {
            Debug.LogWarning($"<color=orange>[Inventory]</color> 이미 {ability.abilityType} 능력을 장착하고 있습니다.");
            return false;
        }

        Slots[slotIndex].EquippedLineage = null;
        Slots[slotIndex].Quantity = 0;
        Slots[slotIndex].EquippedThrowAbility = ability;
        
        UpdateActiveAbilities();
        Debug.Log($"<color=green>[Inventory]</color> 던지기 능력 {ability.abilityType} 장착 완료 (Slot: {slotIndex})");
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

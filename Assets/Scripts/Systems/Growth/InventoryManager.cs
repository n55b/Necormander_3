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
        public MinionLineageSO EquippedLineage; // [수정] 아이템 파일 대신 계보 마스터를 직접 장착
        public int EvolutionIndex; 
        
        public bool IsEmpty => !IsShattered && EquippedLineage == null;

        public MinionDataSO GetCurrentMinionData() => EquippedLineage != null ? EquippedLineage.GetForm(EvolutionIndex) : null;
        public GrowthItemData GetCurrentItemData() => EquippedLineage != null ? EquippedLineage.GetItemData(EvolutionIndex) : null;
    }

    [Header("슬롯 시스템 (10개 고정)")]
    public List<CoreSlot> Slots = new List<CoreSlot>(10);

    [Header("보석 보관함 (직업별)")]
    public Dictionary<CommandData, List<GemSO>> EquippedGems = new Dictionary<CommandData, List<GemSO>>();

    [Header("보물 인벤토리 (중첩)")]
    public Dictionary<TreasureSO, int> TreasureStacks = new Dictionary<TreasureSO, int>();

    public void Initialize()
    {
        Instance = this;
        if (Slots.Count == 0)
        {
            for (int i = 0; i < 10; i++) Slots.Add(new CoreSlot());
        }
        Debug.Log("<color=cyan>[InventoryManager]</color> Initialized.");
    }

    public bool EquipLineage(int slotIndex, MinionLineageSO lineage)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count || Slots[slotIndex].IsShattered) return false;
        Slots[slotIndex].EquippedLineage = lineage;
        Slots[slotIndex].EvolutionIndex = 0;
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
        }
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
            return true;
        }
        return false;
    }

    public bool HasLineageInSlots(MinionLineageSO lineage) => Slots.Exists(s => s.EquippedLineage == lineage);
    public bool HasJobInSlots(CommandData job) => Slots.Exists(s => s.EquippedLineage != null && s.EquippedLineage.jobType == job);

    public void AddTreasure(TreasureSO treasure)
    {
        if (TreasureStacks.ContainsKey(treasure)) TreasureStacks[treasure]++;
        else TreasureStacks[treasure] = 1;
    }
}

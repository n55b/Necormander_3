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

    [Header("자원 관리")]
    [SerializeField] private int gold = 0;
    public int GOLD => gold;

    [Header("슬롯 시스템 (10개 고정)")]
    public List<CoreSlot> Slots = new List<CoreSlot>(10);

    [Header("보석 보관함 (직업별)")]
    // [수정] Dictionary를 사용하여 각 직업(CommandData)이 가진 보석 리스트를 관리합니다.
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
    /// <summary>
    /// 특정 직업의 특정 능력치에 대한 보석 보너스 합계를 계산합니다.
    /// 유닛의 실제 소환 여부와 관계없이 인벤토리 데이터를 직접 참조합니다.
    /// </summary>
    /// <param name="job">대상 직업</param>
    /// <param name="stat">계산할 능력치 타입</param>
    /// <returns>합산된 보너스 값 (배율 또는 고정치)</returns>
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
        // [규칙] 해당 직업 유닛이 슬롯에 하나라도 있어야 장착 가능
        if (!HasJobInSlots(job))
        {
            Debug.LogWarning($"<color=orange>[Inventory]</color> {job} 유닛이 슬롯에 없어 보석을 장착할 수 없습니다.");
            return false;
        }

        if (!EquippedGems.ContainsKey(job)) EquippedGems[job] = new List<GemSO>();

        // [규칙] 직업당 최대 2개까지 장착 가능
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
    #endregion

    public bool HasLineageInSlots(MinionLineageSO lineage) => Slots.Exists(s => s.EquippedLineage == lineage);
    public bool HasJobInSlots(CommandData job) => Slots.Exists(s => s.EquippedLineage != null && s.EquippedLineage.jobType == job);

    public void AddTreasure(TreasureSO treasure)
    {
        if (TreasureStacks.ContainsKey(treasure)) TreasureStacks[treasure]++;
        else TreasureStacks[treasure] = 1;
    }
}

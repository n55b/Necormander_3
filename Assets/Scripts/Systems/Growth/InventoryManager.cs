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

    // ======================================================
    // [에러 방지를 위한 병렬 리스트 디버그 설정]
    // ======================================================
    [Header("Debug Settings (Starting Items)")]
    [SerializeField] private bool useDebugStartingInventory = true;
    
    [Space(10)]
    [Tooltip("시작 시 지급할 미니언 리스트")]
    [SerializeField] private List<MinionLineageSO> debugStartingMinions = new List<MinionLineageSO>();
    [Tooltip("위 미니언들의 수량 (순서대로 매칭)")]
    [SerializeField] private List<int> debugStartingMinionQuantities = new List<int>();

    [Space(10)]
    [Tooltip("시작 시 지급할 보석 리스트")]
    [SerializeField] private List<GemSO> debugStartingGems_Data = new List<GemSO>();
    [Tooltip("위 보석들의 대상 직업 (순서대로 매칭)")]
    [SerializeField] private List<CommandData> debugStartingGems_TargetJobs = new List<CommandData>();
    // ======================================================

    public System.Action OnGemTreeUpdated;

    [Header("보물 인벤토리 (중첩)")]
    public Dictionary<TreasureSO, int> TreasureStacks = new Dictionary<TreasureSO, int>();

    [Header("Gem Tree System")]
    public GemTreeNode GemTreeRoot { get; private set; }
    private Dictionary<string, GemTreeNode> _gemNodeIndex; 
    private Dictionary<CommandData, GemAggregatedStats> _jobGemStats = new Dictionary<CommandData, GemAggregatedStats>();
    public List<GemInstance> AvailableGemInstances { get; private set; } = new List<GemInstance>(); 
    [SerializeField] private GemSO _defaultRootGemSO; 

    public class GemAggregatedStats
    {
        public float AttackBonus = 0f;
        public float HealthBonus = 0f;
        public float AttackSpeedBonus = 0f;
        public float RespawnTimeBonus = 0f;
        public Dictionary<DebuffStackType, float> AggregatedDebuffStacks = new Dictionary<DebuffStackType, float>();

        public void Clear()
        {
            AttackBonus = 0f;
            HealthBonus = 0f;
            AttackSpeedBonus = 0f;
            RespawnTimeBonus = 0f;
            AggregatedDebuffStacks.Clear();
        }
    }

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
        InitializeGemTree(); 
        Debug.Log("<color=cyan>[InventoryManager]</color> Initialized.");

        if (useDebugStartingInventory)
        {
            Debug_InitializeInventory();
        }
    }

    private void InitializeGemTree()
    {
        _gemNodeIndex = new Dictionary<string, GemTreeNode>();
        _jobGemStats.Clear();

        if (_defaultRootGemSO == null)
        {
            Debug.LogError("InventoryManager: _defaultRootGemSO is not assigned.");
            return;
        }

        GemInstance rootInstance = new GemInstance(_defaultRootGemSO, CommandData.SkeletonWarrior, 2); 
        GemTreeRoot = new GemTreeNode(rootInstance);

        _gemNodeIndex.Add(GemTreeRoot.Gem.InstanceId, GemTreeRoot);

        RecalculateGemTreeStats(); 
        Debug.Log($"<color=cyan>[InventoryManager]</color> Gem Tree Initialized with Root: {GemTreeRoot.Gem.BaseData.itemName}");
    }

    public GemTreeNode GetNodeById(string instanceId)
    {
        if (_gemNodeIndex.TryGetValue(instanceId, out var node)) return node;
        return null;
    }

    private void RecalculateGemTreeStats()
    {
        foreach (var stats in _jobGemStats.Values) stats.Clear();
        if (GemTreeRoot == null) return;

        Queue<GemTreeNode> nodesToVisit = new Queue<GemTreeNode>();
        nodesToVisit.Enqueue(GemTreeRoot);

        while (nodesToVisit.Count > 0)
        {
            GemTreeNode currentNode = nodesToVisit.Dequeue();
            if (currentNode.Gem != null)
            {
                CommandData job = currentNode.Gem.TargetJob;
                if (!_jobGemStats.ContainsKey(job)) _jobGemStats[job] = new GemAggregatedStats();
                GemAggregatedStats targetStats = _jobGemStats[job];

                foreach (var modifier in currentNode.Gem.BaseData.GetStatModifiers())
                    ApplyStatModifier(targetStats, modifier);

                foreach (var modifier in currentNode.Gem.RandomModifiers)
                    ApplyStatModifier(targetStats, modifier);

                if (currentNode.Gem.BaseData is GemDebuffSO debuffGem)
                {
                    if (!targetStats.AggregatedDebuffStacks.ContainsKey(debuffGem.targetDebuffType))
                        targetStats.AggregatedDebuffStacks[debuffGem.targetDebuffType] = 0f;
                    targetStats.AggregatedDebuffStacks[debuffGem.targetDebuffType] += debuffGem.baseDebuffStack;
                }

                foreach (var child in currentNode.Children)
                {
                    if (child != null) nodesToVisit.Enqueue(child);
                }
            }
        }
    }

    private void ApplyStatModifier(GemAggregatedStats targetStats, StatModifier modifier)
    {
        switch (modifier.Type)
        {
            case StatType.Attack: targetStats.AttackBonus += modifier.Value; break;
            case StatType.Health: targetStats.HealthBonus += modifier.Value; break;
            case StatType.AttackSpeed: targetStats.AttackSpeedBonus += modifier.Value; break;
            case StatType.RespawnTime: targetStats.RespawnTimeBonus += modifier.Value; break;
            default: break;
        }
    }

    public float GetAggregatedGemBonus(CommandData job, StatType type)
    {
        if (!_jobGemStats.TryGetValue(job, out var stats)) return 0f;
        switch (type)
        {
            case StatType.Attack: return stats.AttackBonus;
            case StatType.Health: return stats.HealthBonus;
            case StatType.AttackSpeed: return stats.AttackSpeedBonus;
            case StatType.RespawnTime: return stats.RespawnTimeBonus;
            default: return 0f;
        }
    }

    public GemAggregatedStats GetJobGemStats(CommandData job)
    {
        if (_jobGemStats.TryGetValue(job, out var stats)) return stats;
        return null;
    }

    public bool SocketGem(string parentNodeId, int slotIndex, GemInstance gemToSocket)
    {
        if (gemToSocket == null) return false;
        GemTreeNode parentNode = GetNodeById(parentNodeId);
        if (parentNode == null) return false;

        GemTreeNode newChildNode = parentNode.SocketChild(slotIndex, gemToSocket);
        if (newChildNode != null)
        {
            _gemNodeIndex.Add(newChildNode.Gem.InstanceId, newChildNode);
            AvailableGemInstances.Remove(gemToSocket);
            RecalculateGemTreeStats();
            OnGemTreeUpdated?.Invoke(); 
            Debug.Log($"<color=green>[InventoryManager]</color> Socketed {gemToSocket.BaseData.itemName} for {gemToSocket.TargetJob}.");
            return true;
        }
        return false;
    }

    public bool UnsocketGem(string nodeId)
    {
        GemTreeNode nodeToUnsocket = GetNodeById(nodeId);
        if (nodeToUnsocket == null || nodeToUnsocket.Parent == null) return false;

        int slotIndex = nodeToUnsocket.Parent.Children.IndexOf(nodeToUnsocket);
        if (slotIndex == -1) return false;

        List<GemInstance> collectedInstances = nodeToUnsocket.Parent.UnsocketChild(slotIndex);
        foreach (var instance in collectedInstances)
        {
            _gemNodeIndex.Remove(instance.InstanceId);
            AvailableGemInstances.Add(instance);
        }
        RecalculateGemTreeStats();
        OnGemTreeUpdated?.Invoke(); 
        return true;
    }

    public void AddGemToAvailable(GemSO gemData, CommandData targetJob)
    {
        if (gemData == null) return;
        if (!gemData.IsEligible(targetJob))
        {
            Debug.LogError($"<color=red>[InventoryManager]</color> Generation Failed: {gemData.itemName} not eligible for {targetJob}.");
            return;
        }
        if (!HasJobInSlots(targetJob))
        {
            Debug.LogError($"<color=red>[InventoryManager]</color> Generation Failed: Player does not own {targetJob}.");
            return;
        }

        GemInstance newGemInstance = new GemInstance(gemData, targetJob);
        AvailableGemInstances.Add(newGemInstance);
        Debug.Log($"<color=green>[InventoryManager]</color> Generated {gemData.itemName} for {targetJob}.");
    }

    private void Debug_InitializeInventory()
    {
        // 1. 미니언 먼저 생성
        for (int i = 0; i < debugStartingMinions.Count; i++)
        {
            if (debugStartingMinions[i] == null) continue;
            int qty = (i < debugStartingMinionQuantities.Count) ? debugStartingMinionQuantities[i] : 1;
            AddMinionOrIncreaseQuantity(debugStartingMinions[i].jobType, Mathf.Max(1, qty));
        }

        // 2. 보석 생성 (직업이 없으면 스마트 자동 할당)
        for (int i = 0; i < debugStartingGems_Data.Count; i++)
        {
            GemSO gem = debugStartingGems_Data[i];
            if (gem == null) continue;

            CommandData job;
            if (i < debugStartingGems_TargetJobs.Count)
            {
                job = debugStartingGems_TargetJobs[i];
            }
            else
            {
                // [변경] 지정되지 않았다면 소유한 유닛 중 낄 수 있는 첫 번째 놈 자동 선택
                job = FindFirstEligibleOwnedJob(gem);
            }

            AddGemToAvailable(gem, job);
        }

        if (!Slots.Exists(s => s.EquippedLineage != null)) AddMinionOrIncreaseQuantity(CommandData.SkeletonWarrior);
    }

    /// <summary>
    /// 현재 소유한 유닛 중 해당 보석을 장착할 수 있는 첫 번째 직업을 반환합니다.
    /// </summary>
    private CommandData FindFirstEligibleOwnedJob(GemSO gem)
    {
        foreach (var slot in Slots)
        {
            if (slot.EquippedLineage != null)
            {
                CommandData ownedJob = slot.EquippedLineage.jobType;
                if (gem.IsEligible(ownedJob)) return ownedJob;
            }
        }
        // 찾지 못했다면 기본값 반환
        return CommandData.SkeletonWarrior;
    }

    public void UpdateActiveAbilities()
    {
        _activeAbilities.Clear();
        foreach (var slot in Slots)
        {
            if (slot.EquippedThrowAbility != null) _activeAbilities.Add(slot.EquippedThrowAbility);
        }
    }

    #region Gold System
    public void AddGold(int amount) { gold += amount; }
    public bool SpendGold(int amount)
    {
        if (gold >= amount) { gold -= amount; return true; }
        return false;
    }
    #endregion

    #region Slot Management
    public bool AddMinionOrIncreaseQuantity(CommandData job, int amount = 1)
    {
        var existingSlot = Slots.Find(s => !s.IsShattered && s.EquippedLineage != null && s.EquippedLineage.jobType == job);
        if (existingSlot != null) { existingSlot.Quantity += amount; return true; }

        var registry = GameManager.Instance.dataManager.GET_GROWTH_REGISTRY();
        if (registry == null) return false;

        MinionLineageSO targetLineage = registry.minionLineages.Find(lin => lin.jobType == job);
        if (targetLineage == null) return false;

        int emptyIdx = Slots.FindIndex(s => s.IsEmpty);
        if (emptyIdx != -1) { EquipLineage(emptyIdx, targetLineage); Slots[emptyIdx].Quantity = amount; return true; }
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
        if (ActiveAbilities.Exists(a => a.GetType() == ability.GetType())) return false;
        Slots[slotIndex].EquippedLineage = null;
        Slots[slotIndex].Quantity = 0;
        Slots[slotIndex].EquippedThrowAbility = ability;
        UpdateActiveAbilities();
        return true;
    }

    public void ApplyMetamorphosis(MinionLineageSO lineage, int index)
    {
        var slot = Slots.Find(s => s.EquippedLineage == lineage);
        if (slot != null) slot.EvolutionIndex = index;
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
            if (kvp.Key.effectType == type) totalBonus += kvp.Key.valuePerStack * kvp.Value;
        }
        return totalBonus;
    }
}

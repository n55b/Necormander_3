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
    private GemAggregatedStats _globalGemStats = new GemAggregatedStats(); // [추가] 전역 합산 스탯
    public List<GemInstance> AvailableGemInstances { get; private set; } = new List<GemInstance>(); 
    [SerializeField] private GemSO _defaultRootGemSO; 

    public class GemAggregatedStats
    {
        public float AttackBonus = 0f;
        public float HealthBonus = 0f;
        public float AttackSpeedBonus = 0f;
        public float RespawnTimeBonus = 0f;
        
        // [신규] 속성 및 특수 효과 합산
        public Dictionary<DebuffStackType, float> WeaponAttributes = new Dictionary<DebuffStackType, float>();
        public Dictionary<DebuffStackType, float> HandAttributes = new Dictionary<DebuffStackType, float>();
        public HashSet<GemUniqueType> UniqueEffects = new HashSet<GemUniqueType>();
        
        // [신규] 시너지 그룹별 최대 인접 개수
        public Dictionary<GemSynergyGroup, int> SynergyCounts = new Dictionary<GemSynergyGroup, int>();

        public void Clear()
        {
            AttackBonus = 0f;
            HealthBonus = 0f;
            AttackSpeedBonus = 0f;
            RespawnTimeBonus = 0f;
            WeaponAttributes.Clear();
            HandAttributes.Clear();
            UniqueEffects.Clear();
            SynergyCounts.Clear();
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
        _globalGemStats.Clear(); 

        if (_defaultRootGemSO == null)
        {
            Debug.LogError("InventoryManager: _defaultRootGemSO is not assigned.");
            return;
        }

        GemInstance rootInstance = new GemInstance(_defaultRootGemSO, CommandData.SkeletonWarrior); 
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
        _globalGemStats.Clear();

        if (GemTreeRoot == null) return;

        // 1. 모든 노드를 순회하며 기본 효과 합산
        List<GemTreeNode> allNodes = new List<GemTreeNode>();
        Queue<GemTreeNode> nodesToVisit = new Queue<GemTreeNode>();
        nodesToVisit.Enqueue(GemTreeRoot);

        while (nodesToVisit.Count > 0)
        {
            GemTreeNode currentNode = nodesToVisit.Dequeue();
            allNodes.Add(currentNode);

            if (currentNode.Gem != null && currentNode.Gem.BaseData != null)
            {
                CommandData job = currentNode.Gem.TargetJob;
                if (!_jobGemStats.ContainsKey(job)) _jobGemStats[job] = new GemAggregatedStats();
                
                GemAggregatedStats targetStats = _jobGemStats[job];

                foreach (var effect in currentNode.Gem.BaseData.effects)
                {
                    if (effect != null)
                    {
                        effect.Apply(targetStats);
                        effect.Apply(_globalGemStats);
                    }
                }

                foreach (var modifier in currentNode.Gem.RandomModifiers)
                {
                    ApplyStatModifier(targetStats, modifier);
                    ApplyStatModifier(_globalGemStats, modifier);
                }
            }

            foreach (var child in currentNode.Children)
            {
                if (child != null) nodesToVisit.Enqueue(child);
            }
        }

        // 2. 시너지 클러스터 계산 (인접 노드 그래프 탐색)
        CalculateSynergies(allNodes);
    }

    private void CalculateSynergies(List<GemTreeNode> allNodes)
    {
        HashSet<GemTreeNode> visited = new HashSet<GemTreeNode>();
        
        foreach (var node in allNodes)
        {
            if (visited.Contains(node) || node.Gem == null || node.Gem.BaseData == null) continue;
            if (node.Gem.BaseData.synergyGroup == GemSynergyGroup.Base) continue;

            GemSynergyGroup group = node.Gem.BaseData.synergyGroup;
            int clusterSize = FindClusterSize(node, group, visited);

            // 해당 그룹의 최대 클러스터 크기 저장
            if (!_globalGemStats.SynergyCounts.ContainsKey(group) || _globalGemStats.SynergyCounts[group] < clusterSize)
            {
                _globalGemStats.SynergyCounts[group] = clusterSize;
            }
        }
    }

    private int FindClusterSize(GemTreeNode startNode, GemSynergyGroup targetGroup, HashSet<GemTreeNode> globalVisited)
    {
        int size = 0;
        Queue<GemTreeNode> queue = new Queue<GemTreeNode>();
        HashSet<GemTreeNode> clusterVisited = new HashSet<GemTreeNode>();

        queue.Enqueue(startNode);
        clusterVisited.Add(startNode);
        globalVisited.Add(startNode);

        while (queue.Count > 0)
        {
            GemTreeNode current = queue.Dequeue();
            size++;

            // 상하좌우 인접 노드 체크
            // 1. 상 (부모)
            CheckAndEnqueue(current.Parent, targetGroup, queue, clusterVisited, globalVisited);

            // 2. 하 (자식들)
            foreach (var child in current.Children)
            {
                CheckAndEnqueue(child, targetGroup, queue, clusterVisited, globalVisited);
            }

            // 3. 좌우 인접 체크 (층별 시각적 인덱스 기반)
            CheckAndEnqueue(GetVisualNeighbor(current, -1), targetGroup, queue, clusterVisited, globalVisited);
            CheckAndEnqueue(GetVisualNeighbor(current, 1), targetGroup, queue, clusterVisited, globalVisited);
        }

        return size;
    }

    /// <summary>
    /// [신규] 해당 노드와 동일한 층(Depth)에서 시각적으로 바로 옆에 있는 슬롯의 노드를 반환합니다.
    /// 인덱스 기반으로 사촌 및 Wrap-around를 통합 처리합니다.
    /// </summary>
    private GemTreeNode GetVisualNeighbor(GemTreeNode node, int direction)
    {
        if (node == null || node.Parent == null) return null;

        // 1. 현재 노드가 속한 층의 모든 슬롯 목록 확보
        int depth = GetNodeDepth(node);
        List<GemTreeNode> depthSlots = GetAllSlotsAtDepth(depth);

        int myIdx = depthSlots.IndexOf(node);
        if (myIdx == -1) return null;

        // 2. 인접 인덱스 계산 (Wrap-around 포함)
        int targetIdx = myIdx + direction;
        if (targetIdx < 0) targetIdx = depthSlots.Count - 1;
        else if (targetIdx >= depthSlots.Count) targetIdx = 0;

        // 3. 해당 슬롯의 노드 반환 (빈 슬롯이면 null이 반환됨)
        return depthSlots[targetIdx];
    }

    private int GetNodeDepth(GemTreeNode node)
    {
        int depth = 0;
        GemTreeNode curr = node;
        while (curr.Parent != null)
        {
            curr = curr.Parent;
            depth++;
        }
        return depth;
    }

    private List<GemTreeNode> GetAllSlotsAtDepth(int targetDepth)
    {
        List<GemTreeNode> currentLevelNodes = new List<GemTreeNode> { GemTreeRoot };

        for (int d = 0; d < targetDepth; d++)
        {
            List<GemTreeNode> nextLevelSlots = new List<GemTreeNode>();
            foreach (var node in currentLevelNodes)
            {
                if (node != null)
                {
                    nextLevelSlots.AddRange(node.Children);
                }
            }
            currentLevelNodes = nextLevelSlots;
        }

        return currentLevelNodes;
    }

    private void CheckAndEnqueue(GemTreeNode node, GemSynergyGroup targetGroup, Queue<GemTreeNode> queue, HashSet<GemTreeNode> clusterVisited, HashSet<GemTreeNode> globalVisited)
    {
        if (node == null || clusterVisited.Contains(node)) return;
        if (node.Gem != null && node.Gem.BaseData != null && node.Gem.BaseData.synergyGroup == targetGroup)
        {
            queue.Enqueue(node);
            clusterVisited.Add(node);
            globalVisited.Add(node);
        }
    }

    public int GetSynergyCount(GemSynergyGroup group)
    {
        return _globalGemStats.SynergyCounts.TryGetValue(group, out int count) ? count : 0;
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

    // --- 신규 젬 효과 쿼리 메서드 ---

    public float GetWeaponAttribute(DebuffStackType type)
    {
        return _globalGemStats.WeaponAttributes.TryGetValue(type, out float val) ? val : 0f;
    }

    public float GetHandAttribute(DebuffStackType type)
    {
        return _globalGemStats.HandAttributes.TryGetValue(type, out float val) ? val : 0f;
    }

    public bool HasUniqueEffect(GemUniqueType type)
    {
        return _globalGemStats.UniqueEffects.Contains(type);
    }

    public float GetAggregatedGemBonus(CommandData job, StatType type)
    {
        // [수정] job 파라미터를 무시하고 전역 합산 스탯을 반환하여 모든 미니언에게 동일 적용
        switch (type)
        {
            case StatType.Attack: return _globalGemStats.AttackBonus;
            case StatType.Health: return _globalGemStats.HealthBonus;
            case StatType.AttackSpeed: return _globalGemStats.AttackSpeedBonus;
            case StatType.RespawnTime: return _globalGemStats.RespawnTimeBonus;
            default: return 0f;
        }
    }

    public GemAggregatedStats GetJobGemStats(CommandData job)
    {
        // UI 등에서 여전히 직업별로 구분된 데이터를 보고 싶을 수 있으므로 유지
        if (_jobGemStats.TryGetValue(job, out var stats)) return stats;
        return null;
    }

    // 전역 스탯 게터 추가
    public GemAggregatedStats GlobalGemStats => _globalGemStats;

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
        
        // [수정] 직업 적합성 체크 우회 (모든 젬은 모든 직업 노드에 장착 가능하거나, 무시됨)
        /*
        if (!gemData.IsEligible(targetJob))
        {
            Debug.LogError($"<color=red>[InventoryManager]</color> Generation Failed: {gemData.itemName} not eligible for {targetJob}.");
            return;
        }
        */

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

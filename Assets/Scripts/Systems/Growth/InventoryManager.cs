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
        public int Quantity;                // 미니언 마리수
        
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

    [Space(10)]
    [Tooltip("시작 시 장착할 투척 능력 리스트")]
    [SerializeField] private List<ThrowAbilitySO> debugStartingAbilities = new List<ThrowAbilitySO>();
    // ======================================================

    public System.Action OnGemTreeUpdated;
    public System.Action OnMinionUpdated;

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

        // [추가] 상태형(Bool) 속성 합산
        public Dictionary<DebuffBoolType, float> WeaponBoolAttributes = new Dictionary<DebuffBoolType, float>();
        public Dictionary<DebuffBoolType, float> HandBoolAttributes = new Dictionary<DebuffBoolType, float>();

        public Dictionary<GemUniqueType, int> UniqueEffectCounts = new Dictionary<GemUniqueType, int>();
        
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
            WeaponBoolAttributes.Clear(); // [추가]
            HandBoolAttributes.Clear();   // [추가]
            UniqueEffectCounts.Clear();
            SynergyCounts.Clear();
        }
    }

    private List<ThrowAbilitySO> _activeAbilities = new List<ThrowAbilitySO>();
    public List<ThrowAbilitySO> ActiveAbilities => _activeAbilities;

    public void Initialize(bool hasSave)
    {
        Instance = this;
        while (Slots.Count < 10)
        {
            Slots.Add(new CoreSlot());
        }
        UpdateActiveAbilities();
        InitializeGemTree(); 
        
        // [유니크] 중독 전역 유니크(PoisonHost 등) 매니저 부착
        if (GetComponent<PoisonUniqueManager>() == null)
            gameObject.AddComponent<PoisonUniqueManager>();
            
        // [유니크] 한기 광역 유니크 (AbsoluteZero, BitingWind 등) 매니저 부착
        if (GetComponent<ChillUniqueManager>() == null)
            gameObject.AddComponent<ChillUniqueManager>();

        // [유니크] 기력/노화 광역 매니저 (Goryeojang) 매니저 부착
        if (GetComponent<AgingUniqueManager>() == null)
            gameObject.AddComponent<AgingUniqueManager>();
            
        // [유니크] 방패병 고유 매니저 (ShieldbearerUniqueManager) 부착
        if (GetComponent<ShieldbearerUniqueManager>() == null)
            gameObject.AddComponent<ShieldbearerUniqueManager>();
            
        Debug.Log("<color=cyan>[InventoryManager]</color> Initialized.");

        if (useDebugStartingInventory && !hasSave)
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

        // 1. 모든 노드 목록 확보
        List<GemTreeNode> allNodes = new List<GemTreeNode>();
        Queue<GemTreeNode> nodesToVisit = new Queue<GemTreeNode>();
        nodesToVisit.Enqueue(GemTreeRoot);

        while (nodesToVisit.Count > 0)
        {
            GemTreeNode currentNode = nodesToVisit.Dequeue();
            if (currentNode != null)
            {
                allNodes.Add(currentNode);
                foreach (var child in currentNode.Children)
                {
                    if (child != null) nodesToVisit.Enqueue(child);
                }
            }
        }

        // 2. 시너지 클러스터 먼저 계산 (활성화 여부 판별을 위해)
        CalculateSynergies(allNodes);

        // 3. 노드를 다시 순회하며 활성화된 효과만 합산
        foreach (var node in allNodes)
        {
            if (node.Gem != null && node.Gem.BaseData != null)
            {
                GemSynergyGroup group = node.Gem.BaseData.synergyGroup;
                int synergyCount = GetSynergyCount(group);

                // [핵심 로직 수정] 
                // 시너지 세트 효과는 각 스크립트에서 GetLevel로 체크하므로, 
                // 보석 자체의 기본 스탯과 유니크 효과는 장착 즉시 발동하도록 제한 해제.
                bool isEffectActive = true; 

                if (isEffectActive)
                {
                    CommandData job = node.Gem.TargetJob;
                    if (!_jobGemStats.ContainsKey(job)) _jobGemStats[job] = new GemAggregatedStats();
                    
                    GemAggregatedStats targetStats = _jobGemStats[job];

                    foreach (var effect in node.Gem.BaseData.effects)
                    {
                        if (effect != null)
                        {
                            effect.Apply(targetStats);
                            effect.Apply(_globalGemStats);
                        }
                    }

                    foreach (var modifier in node.Gem.RandomModifiers)
                    {
                        ApplyStatModifier(targetStats, modifier);
                        ApplyStatModifier(_globalGemStats, modifier);
                    }
                }
            }
        }
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

    // [추가] 상태형(Bool) 보석 효과 조회 게터
    public float GetWeaponBoolAttribute(DebuffBoolType type)
    {
        return _globalGemStats.WeaponBoolAttributes.TryGetValue(type, out float val) ? val : 0f;
    }

    public float GetHandBoolAttribute(DebuffBoolType type)
    {
        return _globalGemStats.HandBoolAttributes.TryGetValue(type, out float val) ? val : 0f;
    }

    public int GetUniqueEffectCount(GemUniqueType type)
    {
        if (_globalGemStats.UniqueEffectCounts.TryGetValue(type, out int count))
        {
            return count;
        }
        return 0;
    }

    public bool HasUniqueEffect(GemUniqueType type)
    {
        return GetUniqueEffectCount(type) > 0;
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

        // [수정] 보석이 전역 효과로 변경되어 RewardProcessor에서 targetJob을 강제로 SkeletonWarrior로 넘기므로, 보유 여부 체크를 우회합니다.
        /*
        if (!HasJobInSlots(targetJob))
        {
            Debug.LogError($"<color=red>[InventoryManager]</color> Generation Failed: Player does not own {targetJob}.");
            return;
        }
        */

        GemInstance newGemInstance = new GemInstance(gemData, targetJob);
        AvailableGemInstances.Add(newGemInstance);
        Debug.Log($"<color=green>[InventoryManager]</color> Generated {gemData.itemName} for {targetJob}.");
    }

    private void Debug_InitializeInventory()
    {
        Debug.Log($"<color=cyan>[InventoryManager]</color> Debug_InitializeInventory Started. Starting Minions Count: {debugStartingMinions.Count}, Starting Gems Count: {debugStartingGems_Data.Count}");

        // 1. 미니언 먼저 생성 (미소유 직업만 추가)
        for (int i = 0; i < debugStartingMinions.Count; i++)
        {
            if (debugStartingMinions[i] == null) continue;
            if (HasMinion(debugStartingMinions[i].jobType)) continue;

            int qty = (i < debugStartingMinionQuantities.Count) ? debugStartingMinionQuantities[i] : 1;
            AddMinionOrIncreaseQuantity(debugStartingMinions[i].jobType, Mathf.Max(1, qty));
        }

        // 2. 보석 생성 (인벤토리나 장착창의 개수와 디버그 보석 리스트의 개수를 매칭하여 부족분만 추가)
        Dictionary<GemSO, int> debugGemTargets = new Dictionary<GemSO, int>();
        for (int i = 0; i < debugStartingGems_Data.Count; i++)
        {
            GemSO gem = debugStartingGems_Data[i];
            if (gem == null) continue;
            
            if (debugGemTargets.ContainsKey(gem))
            {
                debugGemTargets[gem]++;
            }
            else
            {
                debugGemTargets[gem] = 1;
            }
        }

        foreach (var kvp in debugGemTargets)
        {
            GemSO gem = kvp.Key;
            int targetCount = kvp.Value;
            int ownedCount = CountGemAlreadyOwned(gem);
            int needToSpawn = targetCount - ownedCount;

            Debug.Log($"<color=cyan>[InventoryManager]</color> Debug Gem Merge Check: {gem.itemName} -> Target: {targetCount}, Owned: {ownedCount}, Need to spawn: {needToSpawn}");

            if (needToSpawn > 0)
            {
                int spawnedThisTurn = 0;
                for (int i = 0; i < debugStartingGems_Data.Count; i++)
                {
                    if (debugStartingGems_Data[i] == gem)
                    {
                        if (spawnedThisTurn >= needToSpawn) break;

                        CommandData job;
                        if (i < debugStartingGems_TargetJobs.Count)
                        {
                            job = debugStartingGems_TargetJobs[i];
                        }
                        else
                        {
                            job = FindFirstEligibleOwnedJob(gem);
                        }

                        AddGemToAvailable(gem, job);
                        spawnedThisTurn++;
                    }
                }
            }
        }

        // 3. 투척 능력 자동 장착 (장착되지 않은 능력만 추가)
        foreach (var ability in debugStartingAbilities)
        {
            if (ability == null) continue;
            if (IsAbilityAlreadyEquipped(ability)) continue;

            int emptyIdx = Slots.FindIndex(s => s.IsEmpty);
            if (emptyIdx != -1)
            {
                EquipThrowAbility(emptyIdx, ability);
            }
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
        if (existingSlot != null) { existingSlot.Quantity += amount; OnMinionUpdated?.Invoke(); return true; }

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
        
        OnMinionUpdated?.Invoke();
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

    #region Save / Load Serialization
    public void SaveToData(SaveData data)
    {
        data.gold = gold;

        // Slots 저장
        data.slots.Clear();
        foreach (var slot in Slots)
        {
            var slotData = new CoreSlotSaveData();
            slotData.isShattered = slot.IsShattered;
            slotData.equippedLineageJob = slot.EquippedLineage != null ? slot.EquippedLineage.jobType.ToString() : "";
            slotData.equippedThrowAbilityName = slot.EquippedThrowAbility != null ? slot.EquippedThrowAbility.name : "";
            slotData.evolutionIndex = slot.EvolutionIndex;
            slotData.quantity = slot.Quantity;
            data.slots.Add(slotData);
        }

        // Treasures 저장
        data.treasures.Clear();
        foreach (var kvp in TreasureStacks)
        {
            if (kvp.Key == null) continue;
            var treasureData = new TreasureSaveData();
            treasureData.treasureSOAddress = kvp.Key.name;
            treasureData.stackCount = kvp.Value;
            data.treasures.Add(treasureData);
        }

        // AvailableGems 저장
        data.availableGems.Clear();
        foreach (var gem in AvailableGemInstances)
        {
            if (gem == null || gem.BaseData == null) continue;
            data.availableGems.Add(SaveGemInstance(gem));
        }

        // GemTreeRoot 저장
        data.flatGemTree.Clear();
        if (GemTreeRoot != null)
        {
            SaveGemTreeFlat(GemTreeRoot, data.flatGemTree);
        }
    }

    private GemInstanceSaveData SaveGemInstance(GemInstance gem)
    {
        var gemData = new GemInstanceSaveData();
        gemData.baseGemSOAddress = gem.BaseData.name;
        gemData.instanceId = gem.InstanceId;
        gemData.subSlots = gem.SubSlots;
        gemData.randomModifiers = new List<StatModifier>(gem.RandomModifiers);
        gemData.targetJob = gem.TargetJob;
        return gemData;
    }

    private void SaveGemTreeFlat(GemTreeNode rootNode, List<FlatGemTreeNodeSaveData> flatList)
    {
        if (rootNode == null) return;
        Queue<(GemTreeNode node, string parentId, int slotIndex)> queue = new Queue<(GemTreeNode, string, int)>();
        queue.Enqueue((rootNode, null, -1));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var node = current.node;

            if (node == null || node.Gem == null) continue;

            var flatData = new FlatGemTreeNodeSaveData();
            flatData.gem = SaveGemInstance(node.Gem);
            flatData.parentInstanceId = current.parentId;
            flatData.slotIndexInParent = current.slotIndex;
            flatList.Add(flatData);

            for (int i = 0; i < node.Children.Count; i++)
            {
                var childNode = node.Children[i];
                if (childNode != null)
                {
                    queue.Enqueue((childNode, node.Gem.InstanceId, i));
                }
            }
        }
    }

    public void LoadFromData(SaveData data)
    {
        gold = data.gold;

        var registry = GameManager.Instance.dataManager.GET_GROWTH_REGISTRY();
        if (registry == null)
        {
            Debug.LogError("[InventoryManager] GrowthRegistrySO is missing during LoadFromData!");
            return;
        }

        // Slots 로드
        Slots.Clear();
        for (int i = 0; i < data.slots.Count; i++)
        {
            var slotData = data.slots[i];
            var coreSlot = new CoreSlot();
            coreSlot.IsShattered = slotData.isShattered;

            if (!string.IsNullOrEmpty(slotData.equippedLineageJob))
            {
                if (System.Enum.TryParse<CommandData>(slotData.equippedLineageJob, out var job))
                {
                    coreSlot.EquippedLineage = registry.minionLineages.Find(lin => lin.jobType == job);
                }
            }

            if (!string.IsNullOrEmpty(slotData.equippedThrowAbilityName))
            {
                var ability = registry.specialAbilities.Find(a => a.name == slotData.equippedThrowAbilityName || a.itemName == slotData.equippedThrowAbilityName);
                coreSlot.EquippedThrowAbility = ability as ThrowAbilitySO;
            }

            coreSlot.EvolutionIndex = slotData.evolutionIndex;
            coreSlot.Quantity = slotData.quantity;
            Slots.Add(coreSlot);
        }
        // 10개 맞추기
        while (Slots.Count < 10)
        {
            Slots.Add(new CoreSlot());
        }
        UpdateActiveAbilities();

        // Treasures 로드
        TreasureStacks.Clear();
        foreach (var treasureData in data.treasures)
        {
            var treasure = registry.treasures.Find(t => t.name == treasureData.treasureSOAddress || t.itemName == treasureData.treasureSOAddress);
            if (treasure != null)
            {
                TreasureStacks[treasure] = treasureData.stackCount;
            }
        }

        // AvailableGems 로드
        AvailableGemInstances.Clear();
        foreach (var gemData in data.availableGems)
        {
            var gemInstance = LoadGemInstance(gemData, registry);
            if (gemInstance != null)
            {
                AvailableGemInstances.Add(gemInstance);
            }
        }

        // GemTreeRoot 및 Node Index 복원
        _gemNodeIndex = new Dictionary<string, GemTreeNode>();
        
        if (data.flatGemTree != null && data.flatGemTree.Count > 0)
        {
            var loadedNodes = new Dictionary<string, GemTreeNode>();

            // 1차: 모든 노드 인스턴스화
            foreach (var flatData in data.flatGemTree)
            {
                var gemInstance = LoadGemInstance(flatData.gem, registry);
                if (gemInstance != null)
                {
                    var node = new GemTreeNode(gemInstance);
                    loadedNodes[gemInstance.InstanceId] = node;
                    _gemNodeIndex[gemInstance.InstanceId] = node;

                    // 부모 ID가 없으면 루트
                    if (string.IsNullOrEmpty(flatData.parentInstanceId))
                    {
                        GemTreeRoot = node;
                    }
                }
            }

            // 2차: 부모-자식 연결
            foreach (var flatData in data.flatGemTree)
            {
                if (string.IsNullOrEmpty(flatData.parentInstanceId)) continue;

                if (loadedNodes.TryGetValue(flatData.gem.instanceId, out GemTreeNode childNode) &&
                    loadedNodes.TryGetValue(flatData.parentInstanceId, out GemTreeNode parentNode))
                {
                    if (flatData.slotIndexInParent >= 0 && flatData.slotIndexInParent < parentNode.Gem.SubSlots)
                    {
                        // 기존 SocketChild를 사용하면 내부 배열을 대체하므로
                        // 단순 배열 할당으로 복원 (이미 GemTreeNode 생성 시 할당됨)
                        parentNode.Children[flatData.slotIndexInParent] = childNode;
                    }
                    else
                    {
                        Debug.LogError($"[InventoryManager] Invalid slot index {flatData.slotIndexInParent} for parent {parentNode.Gem.BaseData.itemName}");
                    }
                }
            }
        }
        else
        {
            InitializeGemTree();
        }

        RecalculateGemTreeStats();

        // 세이브 데이터를 복원한 후 디버그 아이템들을 추가 주입 (중복 항목은 제외)
        if (useDebugStartingInventory)
        {
            Debug_InitializeInventory();
        }

        OnGemTreeUpdated?.Invoke();
        OnMinionUpdated?.Invoke();
    }

    private GemInstance LoadGemInstance(GemInstanceSaveData gemData, GrowthRegistrySO registry)
    {
        if (gemData == null)
        {
            Debug.LogError("[InventoryManager] gemData is null in LoadGemInstance.");
            return null;
        }
        if (registry == null)
        {
            Debug.LogError("[InventoryManager] GrowthRegistrySO is null in LoadGemInstance.");
            return null;
        }
        if (registry.gems == null)
        {
            Debug.LogError("[InventoryManager] registry.gems is null in LoadGemInstance.");
            return null;
        }

        GemSO gemSO = null;

        // 시스템 루트 보석에 대한 특별 예외 처리 (Default_Root_Gem)
        if (gemData.baseGemSOAddress == "Default_Root_Gem" || (_defaultRootGemSO != null && (gemData.baseGemSOAddress == _defaultRootGemSO.name || gemData.baseGemSOAddress == _defaultRootGemSO.itemName)))
        {
            gemSO = _defaultRootGemSO;
            Debug.Log($"<color=cyan>[InventoryManager]</color> Root GemSO '{gemData.baseGemSOAddress}' recovered using _defaultRootGemSO.");
        }
        else
        {
            gemSO = registry.gems.Find(g => g != null && (g.name == gemData.baseGemSOAddress || g.itemName == gemData.baseGemSOAddress));
            
            // 디버그 보석 리스트에서 백업 탐색
            if (gemSO == null && debugStartingGems_Data != null)
            {
                gemSO = debugStartingGems_Data.Find(g => g != null && (g.name == gemData.baseGemSOAddress || g.itemName == gemData.baseGemSOAddress));
                if (gemSO != null)
                {
                    Debug.Log($"<color=cyan>[InventoryManager]</color> GemSO '{gemData.baseGemSOAddress}' recovered from debugStartingGems_Data backup.");
                }
            }
        }

        if (gemSO == null)
        {
            Debug.LogError($"[InventoryManager] GemSO '{gemData.baseGemSOAddress}' not found in registry, debugStartingGems_Data, or _defaultRootGemSO. (InstanceId: {gemData.instanceId})");
            return null;
        }

        return new GemInstance(gemSO, gemData.instanceId, gemData.subSlots, gemData.randomModifiers, gemData.targetJob);
    }



    private int CountGemAlreadyOwned(GemSO gem)
    {
        if (gem == null) return 0;
        int count = 0;

        // 1. 인벤토리에서 개수 카운트
        int inventoryCount = 0;
        foreach (var g in AvailableGemInstances)
        {
            if (g != null && g.BaseData == gem) inventoryCount++;
        }
        count += inventoryCount;

        // 2. 젬 트리에서 개수 카운트
        int treeCount = 0;
        if (GemTreeRoot != null)
        {
            treeCount = CountGemInTree(GemTreeRoot, gem);
        }
        count += treeCount;

        Debug.Log($"<color=cyan>[InventoryManager]</color> CountGemAlreadyOwned({gem.itemName}): Total = {count} (Inventory: {inventoryCount}, Tree: {treeCount})");
        return count;
    }

    private int CountGemInTree(GemTreeNode node, GemSO gem)
    {
        if (node == null) return 0;
        int count = 0;
        if (node.Gem != null && node.Gem.BaseData == gem) count++;
        foreach (var child in node.Children)
        {
            if (child != null)
            {
                count += CountGemInTree(child, gem);
            }
        }
        return count;
    }

    private bool IsAbilityAlreadyEquipped(ThrowAbilitySO ability)
    {
        if (ability == null) return false;
        return Slots.Exists(s => s.EquippedThrowAbility == ability);
    }

    private bool HasMinion(CommandData jobType)
    {
        return Slots.Exists(s => s.EquippedLineage != null && s.EquippedLineage.jobType == jobType);
    }
    #endregion
}

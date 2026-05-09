using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 보석 트리 시스템의 기능을 유니티 에디터의 인스펙터에서 직접 테스트하기 위한 디버거 스크립트입니다.
/// </summary>
public class GemTreeDebugger : MonoBehaviour
{
    [Header("Debug Settings (Input)")]
    [SerializeField] private GemSO _gemSOToAddToAvailable; 
    [SerializeField] private CommandData _targetJob; // [추가] 보석을 생성할 대상 직업
    [SerializeField] private string _gemInstanceIdToUnsocket; 

    [Header("Select Slot From List")]
    [SerializeField] private int _targetAvailableSlotIndex; 
    [SerializeField] private int _totalAvailableSlotCount; // [추가] 총 빈 슬롯 개수 표시

    [Header("Debugging Values (Runtime View)")]
    [SerializeField] private List<GemInstanceInfo> _currentAvailableGems = new List<GemInstanceInfo>();
    
    [System.Serializable]
    public struct GemInstanceInfo // 인스펙터 가독성을 위한 구조체
    {
        public string name;
        public CommandData job;
        public int slots;
        public string id;
    }

    [System.Serializable]
    public struct AvailableSlotInfo
    {
        public int globalIndex; // [추가] 0, 1, 2... 순서
        public string parentName;
        public string parentId;
        public int slotIndex;
        public string displayPath; 
    }

    [SerializeField] private List<AvailableSlotInfo> _availableSlots = new List<AvailableSlotInfo>();
    
    private GemTreeNode _gemTreeRoot;
    
    [Header("Aggregated Stats (Per Job)")]
    [SerializeField] private CommandData _viewStatsForJob = CommandData.SkeletonWarrior;
    [SerializeField] private float _atkBonus;
    [SerializeField] private float _hpBonus;
    [SerializeField] private float _spdBonus;

    private void Update()
    {
        if (InventoryManager.Instance != null)
        {
            // 가용 보석 정보 갱신
            _currentAvailableGems.Clear();
            foreach(var gem in InventoryManager.Instance.AvailableGemInstances)
            {
                _currentAvailableGems.Add(new GemInstanceInfo {
                    name = gem.BaseData.itemName,
                    job = gem.TargetJob,
                    slots = gem.SubSlots,
                    id = gem.InstanceId
                });
            }

            _gemTreeRoot = InventoryManager.Instance.GemTreeRoot;
            RefreshAvailableSlots();

            var stats = InventoryManager.Instance.GetJobGemStats(_viewStatsForJob);
            if (stats != null)
            {
                _atkBonus = stats.AttackBonus;
                _hpBonus = stats.HealthBonus;
                _spdBonus = stats.AttackSpeedBonus;
            }
            else
            {
                _atkBonus = _hpBonus = _spdBonus = 0f;
            }
        }
    }

    private void RefreshAvailableSlots()
    {
        _availableSlots.Clear();
        if (_gemTreeRoot == null) return;
        FindAvailableSlotsRecursive(_gemTreeRoot, "Root");
        _totalAvailableSlotCount = _availableSlots.Count;
    }

    private void FindAvailableSlotsRecursive(GemTreeNode node, string path)
    {
        if (node == null || node.Gem == null) return;

        for (int i = 0; i < node.Gem.SubSlots; i++)
        {
            if (node.Children[i] == null)
            {
                _availableSlots.Add(new AvailableSlotInfo {
                    globalIndex = _availableSlots.Count,
                    parentName = node.Gem.BaseData.itemName,
                    parentId = node.Gem.InstanceId,
                    slotIndex = i,
                    displayPath = $"[{_availableSlots.Count}] {path} -> Slot {i}"
                });
            }
            else
            {
                FindAvailableSlotsRecursive(node.Children[i], $"{path} > {node.Children[i].Gem.BaseData.itemName}");
            }
        }
    }

    [ContextMenu("0. Print Tree Structure")]
    public void Debug_PrintTree()
    {
        if (_gemTreeRoot == null) return;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("<color=yellow>====== Gem Tree (Per Job) ======</color>");
        PrintNodeRecursive(_gemTreeRoot, sb, 0, -1);
        Debug.Log(sb.ToString());
    }

    private void PrintNodeRecursive(GemTreeNode node, System.Text.StringBuilder sb, int indent, int slotIdx)
    {
        string space = new string(' ', indent * 4);
        string prefix = (slotIdx == -1) ? "[Root]" : $"[Slot {slotIdx}]";
        
        string detail = (node.Gem.BaseData is GemDebuffSO debuff) ? $"{debuff.targetDebuffType}" : "Stat";
        sb.AppendLine($"{space}{prefix} <color=cyan>{node.Gem.BaseData.itemName}</color> (<color=orange>{node.Gem.TargetJob}</color>) [{detail}] - ID: {node.Gem.InstanceId.Substring(0, 5)}");

        for (int i = 0; i < node.Gem.SubSlots; i++)
        {
            if (node.Children[i] != null) PrintNodeRecursive(node.Children[i], sb, indent + 1, i);
            else sb.AppendLine($"{space}    [Slot {i}] <color=gray>(Empty)</color>");
        }
    }

    [ContextMenu("1. Add Gem To Available")]
    public void Debug_AddGemToAvailable()
    {
        InventoryManager.Instance.AddGemToAvailable(_gemSOToAddToAvailable, _targetJob);
    }

    [ContextMenu("2. Socket Gem to Selected Slot")]
    public void Debug_SocketGemToSelectedSlot()
    {
        if (_availableSlots.Count == 0 || _targetAvailableSlotIndex >= _availableSlots.Count) return;
        if (InventoryManager.Instance.AvailableGemInstances.Count == 0) return;

        AvailableSlotInfo target = _availableSlots[_targetAvailableSlotIndex];
        GemInstance gemToSocket = InventoryManager.Instance.AvailableGemInstances[0];

        InventoryManager.Instance.SocketGem(target.parentId, target.slotIndex, gemToSocket);
    }

    [ContextMenu("3. Unsocket Gem")]
    public void Debug_UnsocketGem()
    {
        InventoryManager.Instance.UnsocketGem(_gemInstanceIdToUnsocket);
    }
}

using System.Collections.Generic;
using UnityEngine; // For Debug.LogError

/// <summary>
/// 보석 트리의 각 노드를 나타냅니다.
/// 이 노드는 GemInstance와 부모/자식 노드에 대한 참조를 가집니다.
/// </summary>
public class GemTreeNode
{
    public GemInstance Gem { get; private set; }
    public GemTreeNode Parent { get; private set; }
    public List<GemTreeNode> Children { get; private set; }

    // Root node constructor
    public GemTreeNode(GemInstance gemInstance)
    {
        if (gemInstance == null)
        {
            Debug.LogError("GemTreeNode: GemInstance cannot be null.");
            return;
        }

        Gem = gemInstance;
        Parent = null; // Root node has no parent
        InitializeChildren();
    }

    // Child node constructor
    public GemTreeNode(GemInstance gemInstance, GemTreeNode parent)
    {
        if (gemInstance == null)
        {
            Debug.LogError("GemTreeNode: GemInstance cannot be null.");
            return;
        }
        if (parent == null)
        {
            Debug.LogError("GemTreeNode: Child node must have a parent.");
            return;
        }

        Gem = gemInstance;
        Parent = parent;
        InitializeChildren();
    }

    private void InitializeChildren()
    {
        Children = new List<GemTreeNode>(Gem.SubSlots);
        for (int i = 0; i < Gem.SubSlots; i++)
        {
            Children.Add(null); // Initialize with nulls for empty slots
        }
    }

    /// <summary>
    /// 지정된 슬롯에 자식 노드를 장착합니다.
    /// </summary>
    /// <param name="slotIndex">장착할 슬롯 인덱스 (0부터 시작).</param>
    /// <param name="childInstance">장착할 GemInstance.</param>
    /// <returns>장착 성공 시 새로운 GemTreeNode, 실패 시 null.</returns>
    public GemTreeNode SocketChild(int slotIndex, GemInstance childInstance)
    {
        if (slotIndex < 0 || slotIndex >= Gem.SubSlots)
        {
            Debug.LogError($"GemTreeNode: Invalid slot index {slotIndex}. Available slots: {Gem.SubSlots}");
            return null;
        }
        if (Children[slotIndex] != null)
        {
            Debug.LogError($"GemTreeNode: Slot {slotIndex} is already occupied.");
            return null;
        }
        if (childInstance == null)
        {
            Debug.LogError("GemTreeNode: Cannot socket a null GemInstance.");
            return null;
        }

        GemTreeNode newChildNode = new GemTreeNode(childInstance, this);
        Children[slotIndex] = newChildNode;
        return newChildNode;
    }

    /// <summary>
    /// 지정된 슬롯의 자식 노드(와 그 하위 트리)를 제거합니다.
    /// </summary>
    /// <param name="slotIndex">해제할 슬롯 인덱스.</param>
    /// <returns>해제된 모든 GemInstance 목록.</returns>
    public List<GemInstance> UnsocketChild(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= Gem.SubSlots)
        {
            Debug.LogError($"GemTreeNode: Invalid slot index {slotIndex}. Available slots: {Gem.SubSlots}");
            return new List<GemInstance>();
        }
        if (Children[slotIndex] == null)
        {
            Debug.LogWarning($"GemTreeNode: Slot {slotIndex} is already empty.");
            return new List<GemInstance>();
        }

        GemTreeNode nodeToUnsocket = Children[slotIndex];
        Children[slotIndex] = null; // Clear the slot

        List<GemInstance> collectedGems = new List<GemInstance>();
        CollectGemInstancesRecursive(nodeToUnsocket, collectedGems);

        return collectedGems;
    }

    private void CollectGemInstancesRecursive(GemTreeNode node, List<GemInstance> collectedGems)
    {
        if (node == null) return;

        collectedGems.Add(node.Gem);

        foreach (var child in node.Children)
        {
            if (child != null)
            {
                CollectGemInstancesRecursive(child, collectedGems);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GemTreeUI : Singleton<GemTreeUI>
{

    [Header("UI Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private Transform inventoryContainer;
    [SerializeField] private RectTransform treeContent;
    [SerializeField] private GemSynergyDisplayUI synergyDisplay; 

    [Header("Prefabs")]
    [SerializeField] private GameObject inventorySlotPrefab;
    [SerializeField] private GameObject nodePrefab;
    [SerializeField] private GameObject connectorPrefab;
    [SerializeField] private GameObject floorLinePrefab;
    [SerializeField] private GameObject tooltipPrefab; // [추가] 툴팁 프리팹

    [Header("Tree Layout Settings")]
    [SerializeField] private float rowHeight = 200f;
    [SerializeField] private float nodeSpacing = 120f;

    [Header("Zoom Settings")]
    [SerializeField] private float minZoom = 0.5f;
    [SerializeField] private float maxZoom = 2.0f;
    [SerializeField] private float zoomSpeed = 0.1f;

    private bool _isOpen = false;
    private List<GameObject> _spawnedUIElements = new List<GameObject>();
    private Dictionary<int, List<GemNodeUI>> _nodesByDepth = new Dictionary<int, List<GemNodeUI>>();
    private Dictionary<GemTreeNode, float> _subTreeWidths = new Dictionary<GemTreeNode, float>();

    public bool IsOpen => _isOpen;


    protected override void OnAwake()
    {
        if (mainPanel != null) mainPanel.SetActive(false);

        // [추가] 툴팁 프리팹 소환
        if (tooltipPrefab != null)
        {
            Instantiate(tooltipPrefab, transform.parent); // Canvas 하위에 생성
        }
    }

    private void Update()
    {
        if (_isOpen) HandleZoom();
    }

    public void Toggle()
    {
        _isOpen = !_isOpen;

        if (_isOpen) 
        {
            if(UIPopUpManager.Instance.IsPopUpActive || UIPopUpManager.Instance.IsOnBattle) 
            {
                _isOpen = !_isOpen; // 팝업이 이미 활성화되어 있으면 토글 상태 원복
                return; // 이미 팝업이 활성화되어 있으면 새로 띄우지 않음
            }

            UIPopUpManager.Instance.PopUpUI(mainPanel);
            UIEventBus.NotifyOpen("GemTree");
            // 팝업 매니저에 상태 전달

            RefreshUI();
        }
        else
        {
            // [추가] UI를 닫을 때 남아있을 수 있는 툴팁 강제 제거
            if (GemTooltipUI.Instance != null) GemTooltipUI.Instance.Hide();

            UIPopUpManager.Instance?.ClosePopUpUI();
            UIEventBus.NotifyClose("GemTree"); // 팝업 매니저에 상태 전달

        }
    }

    public void RefreshUI()
    {
        var ghost = GameObject.Find("GemDragGhost");
        if (ghost != null) Destroy(ghost);

        ClearUI();
        if (InventoryManager.Instance == null) return;

        foreach (var gemInstance in InventoryManager.Instance.AvailableGemInstances)
            CreateInventorySlot(gemInstance);

        if (InventoryManager.Instance.GemTreeRoot != null)
        {
            var nodePositions = CalculateAllNodePositions();
            RenderTree(InventoryManager.Instance.GemTreeRoot, nodePositions);
            DrawSynergyLines(nodePositions); // [추가] 시너지 연결선 그리기 호출
            DrawFloorLines(nodePositions);
        }

        // 시너지 리스트 갱신
        if (synergyDisplay != null)
        {
            synergyDisplay.RefreshSynergyList();
        }
    }
    
    #region Layout Calculation (2-Pass)

    private Dictionary<GemTreeNode, Vector2> CalculateAllNodePositions()
    {
        var positions = new Dictionary<GemTreeNode, Vector2>();
        _subTreeWidths.Clear();
        
        CalculateWidthsRecursive(InventoryManager.Instance.GemTreeRoot);
        CalculatePositionsRecursive(InventoryManager.Instance.GemTreeRoot, null, 0, positions);
        
        var finalPositions = new Dictionary<GemTreeNode, Vector2>();
        if(positions.Count > 0 && positions.ContainsKey(InventoryManager.Instance.GemTreeRoot))
        {
            Vector2 centerOffset = new Vector2(-positions[InventoryManager.Instance.GemTreeRoot].x, -treeContent.rect.height * 0.45f);
            foreach(var kvp in positions)
            {
                finalPositions[kvp.Key] = kvp.Value + centerOffset;
            }
        }
        return finalPositions;
    }

    private float CalculateWidthsRecursive(GemTreeNode node)
    {
        if (node == null) return nodeSpacing; 

        var activeChildren = node.Children.FindAll(c => c != null);
        if (activeChildren.Count == 0)
        {
            float width = nodeSpacing * Mathf.Max(1, node.Gem.SubSlots);
            _subTreeWidths[node] = width;
            return width;
        }

        float totalWidth = 0;
        for(int i = 0; i < node.Gem.SubSlots; i++)
        {
            var child = i < node.Children.Count ? node.Children[i] : null;
            totalWidth += CalculateWidthsRecursive(child);
        }
        
        _subTreeWidths[node] = totalWidth;
        return totalWidth;
    }

    private void CalculatePositionsRecursive(GemTreeNode node, GemTreeNode parent, int depth, Dictionary<GemTreeNode, Vector2> positions)
    {
        if (node == null) return;

        float xPos;
        float yPos = depth * rowHeight;

        if (parent == null)
        {
            xPos = 0;
        }
        else
        {
            float parentX = positions[parent].x;
            float parentWidth = _subTreeWidths[parent];
            float startX = parentX - parentWidth / 2f;
            
            float offsetWithinParent = 0;
            for(int i = 0; i < parent.Gem.SubSlots; i++)
            {
                var sibling = i < parent.Children.Count ? parent.Children[i] : null;
                if (sibling == node) break;
                
                if (sibling != null) offsetWithinParent += _subTreeWidths[sibling];
                else offsetWithinParent += nodeSpacing;
            }
            xPos = startX + offsetWithinParent + _subTreeWidths[node] / 2f;
        }
        
        positions[node] = new Vector2(xPos, yPos);

        foreach (var child in node.Children)
        {
            CalculatePositionsRecursive(child, node, depth + 1, positions);
        }
    }
    
    #endregion

    #region UI Creation & Rendering

    private void RenderTree(GemTreeNode node, Dictionary<GemTreeNode, Vector2> positions)
    {
        if (node == null || !positions.ContainsKey(node)) return;
        
        int depth = Mathf.RoundToInt(positions[node].y / rowHeight);
        CreateNodeUI(node, positions[node], depth);

        float parentWidth = _subTreeWidths[node];
        float startX = positions[node].x - parentWidth / 2f;

        float accumulatedWidth = 0;
        for (int i = 0; i < node.Gem.SubSlots; i++)
        {
            GemTreeNode child = (i < node.Children.Count) ? node.Children[i] : null;
            
            if (child != null)
            {
                RenderTree(child, positions);

                // [수정] 부모-자식 연결선 색상 세분화
                Color lineColor;
                if (node.Gem.BaseData.synergyGroup != GemSynergyGroup.Base &&
                    node.Gem.BaseData.synergyGroup == child.Gem.BaseData.synergyGroup)
                {
                    // 1. 시너지 있음: 그룹 색상
                    lineColor = GemSO.GetSynergyColor(node.Gem.BaseData.synergyGroup);
                }
                else
                {
                    // 2. 시너지 없음 (장착됨): 흰색
                    lineColor = Color.white;
                }
                CreateConnector(positions[node], positions[child], lineColor);

                accumulatedWidth += _subTreeWidths[child];
            }
            else
            {
                float emptySlotWidth = nodeSpacing;
                Vector2 childPos = new Vector2(startX + accumulatedWidth + emptySlotWidth / 2, positions[node].y + rowHeight);
                CreateEmptySlotNode(childPos, node, i, depth + 1);
                
                // [추가] 빈 슬롯과의 연결선도 기본 구조로 표시
                CreateConnector(positions[node], childPos, new Color(0.5f, 0.5f, 0.5f, 0.15f));
                
                accumulatedWidth += emptySlotWidth;
            }
        }
    }

    /// <summary>
    /// [신규] 모든 노드를 순회하며 시너지 그룹이 일치하는 인접 노드 간에 연결선을 그립니다.
    /// 사용자의 제안에 따라 층별 슬롯 인덱스 기반으로 인접성을 판단합니다.
    /// </summary>
    private void DrawSynergyLines(Dictionary<GemTreeNode, Vector2> positions)
    {
        // 층별로 순회
        for (int d = 1; d < 10; d++) // Root(0층)는 제외
        {
            List<GemTreeNode> slots = GetAllSlotsAtDepth(d);
            if (slots.Count <= 1) continue;

            for (int i = 0; i < slots.Count; i++)
            {
                GemTreeNode curr = slots[i];
                if (curr == null || curr.Gem == null || curr.Gem.BaseData == null) continue;
                if (curr.Gem.BaseData.synergyGroup == GemSynergyGroup.Base) continue;

                // 왼쪽 인접 슬롯 확인 (Wrap-around 포함)
                int leftIdx = (i == 0) ? slots.Count - 1 : i - 1;
                GemTreeNode leftNeighbor = slots[leftIdx];

                if (leftNeighbor != null && positions.ContainsKey(leftNeighbor) &&
                    leftNeighbor.Gem != null && leftNeighbor.Gem.BaseData != null &&
                    leftNeighbor.Gem.BaseData.synergyGroup == curr.Gem.BaseData.synergyGroup)
                {
                    // 시너지 그룹이 같다면 연결선 생성
                    CreateConnector(positions[curr], positions[leftNeighbor], GemSO.GetSynergyColor(curr.Gem.BaseData.synergyGroup));
                }
            }
        }
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
        if (InventoryManager.Instance == null || InventoryManager.Instance.GemTreeRoot == null)
            return new List<GemTreeNode>();

        List<GemTreeNode> currentLevelNodes = new List<GemTreeNode> { InventoryManager.Instance.GemTreeRoot };

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
    
    private void DrawFloorLines(Dictionary<GemTreeNode, Vector2> positions)
    {
        var depths = new HashSet<int>();
        foreach (var node in positions.Keys)
        {
            int depth = Mathf.RoundToInt(positions[node].y / rowHeight);
            if (!depths.Contains(depth)) depths.Add(depth);
        }
        
        if (!positions.ContainsKey(InventoryManager.Instance.GemTreeRoot)) return;
        float rootY = positions[InventoryManager.Instance.GemTreeRoot].y;

        for(int i = 0; i < 10; i++)
        {
            CreateFloorLine(rootY + i * rowHeight);
        }
    }

    private void CreateInventorySlot(GemInstance gem)
    {
        GameObject slotObj = Instantiate(inventorySlotPrefab, inventoryContainer);
        var slotUI = slotObj.GetComponent<GemInventorySlotUI>();
        if(slotUI != null) slotUI.Setup(gem);
        _spawnedUIElements.Add(slotObj);
    }
    
    private void CreateNodeUI(GemTreeNode node, Vector2 pos, int depth)
    {
        GameObject nodeObj = Instantiate(nodePrefab, treeContent);
        nodeObj.GetComponent<RectTransform>().anchoredPosition = pos;
        var ui = nodeObj.GetComponent<RectTransform>().GetComponent<GemNodeUI>();
        if(ui != null)
        {
            ui.Setup(node, depth);
            RegisterNodeByDepth(ui, depth);
        }
        _spawnedUIElements.Add(nodeObj);
    }

    private void CreateEmptySlotNode(Vector2 pos, GemTreeNode parent, int slotIdx, int depth)
    {
        GameObject nodeObj = Instantiate(nodePrefab, treeContent);
        nodeObj.GetComponent<RectTransform>().anchoredPosition = pos;
        var ui = nodeObj.GetComponent<RectTransform>().GetComponent<GemNodeUI>();
        if(ui != null)
        {
            ui.SetupEmpty(parent, slotIdx, depth);
            RegisterNodeByDepth(ui, depth);
        }
        _spawnedUIElements.Add(nodeObj);
    }

    private void CreateFloorLine(float yPos)
    {
        if (floorLinePrefab == null) return;
        GameObject lineObj = Instantiate(floorLinePrefab, treeContent);
        lineObj.transform.SetAsFirstSibling();
        var rect = lineObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(0, yPos);
        rect.sizeDelta = new Vector2(treeContent.rect.width * 2, rect.sizeDelta.y);
        _spawnedUIElements.Add(lineObj);
    }

    private void CreateConnector(Vector2 start, Vector2 end, Color color)
    {
        GameObject connObj = Instantiate(connectorPrefab, treeContent);
        connObj.transform.SetAsFirstSibling();
        var rect = connObj.GetComponent<RectTransform>();
        
        // [추가] 색상 적용
        var img = connObj.GetComponent<Image>();
        if (img != null) img.color = color;

        Vector2 mid = (start + end) / 2f;
        rect.anchoredPosition = mid;
        Vector2 dir = end - start;
        rect.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        rect.sizeDelta = new Vector2(dir.magnitude, rect.sizeDelta.y);
        _spawnedUIElements.Add(connObj);
    }

    private void ClearUI()
    {
        foreach (var obj in _spawnedUIElements) if(obj != null) Destroy(obj);
        _spawnedUIElements.Clear();
        _nodesByDepth.Clear();
    }
    
    private void RegisterNodeByDepth(GemNodeUI nodeUI, int depth)
    {
        if (!_nodesByDepth.ContainsKey(depth)) _nodesByDepth[depth] = new List<GemNodeUI>();
        _nodesByDepth[depth].Add(nodeUI);
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            treeContent.localScale = Vector3.one * Mathf.Clamp(treeContent.localScale.x + scroll * zoomSpeed, minZoom, maxZoom);
        }
    }

    #endregion
}

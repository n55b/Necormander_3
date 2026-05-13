using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GemTreeUI : MonoBehaviour
{
    public static GemTreeUI Instance;

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


    private void Awake()
    {
        Instance = this;
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
        mainPanel.SetActive(_isOpen);
        if (_isOpen) RefreshUI();
        else
        {
            // [추가] UI를 닫을 때 남아있을 수 있는 툴팁 강제 제거
            if (GemTooltipUI.Instance != null) GemTooltipUI.Instance.Hide();
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

                // [유지] 형제 노드(좌우) 시너지 체크: 시너지가 있을 때만 생성
                if (i > 0)
                {
                    GemTreeNode leftSibling = node.Children[i - 1];
                    if (leftSibling != null && 
                        leftSibling.Gem.BaseData.synergyGroup != GemSynergyGroup.Base &&
                        leftSibling.Gem.BaseData.synergyGroup == child.Gem.BaseData.synergyGroup)
                    {
                        CreateConnector(positions[leftSibling], positions[child], GemSO.GetSynergyColor(child.Gem.BaseData.synergyGroup));
                    }
                }
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

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 보석 트리의 각 노드(장착된 보석 또는 빈 슬롯)를 표현하며 드롭 타겟 역할을 합니다.
/// </summary>
public class GemNodeUI : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private GameObject emptyVisual;
    [SerializeField] private GameObject filledVisual;
    [SerializeField] private TextMeshProUGUI jobText;
    [SerializeField] private Image highlightOverlay;

    private GemTreeNode _nodeData;
    private GemTreeNode _parentData; // 빈 슬롯일 경우 참조
    private int _slotIndex;         // 빈 슬롯일 경우 참조
    private bool _isEmptySlot = false;

    private void Awake()
    {
        if (highlightOverlay != null) highlightOverlay.gameObject.SetActive(false);
    }

    public void Setup(GemTreeNode node, int depth)
    {
        _nodeData = node;
        _isEmptySlot = false;

        if (emptyVisual != null) emptyVisual.SetActive(false);
        if (filledVisual != null) filledVisual.SetActive(true);
        if (iconImage != null) iconImage.sprite = node.Gem.BaseData.icon;
        
        // [수정] 젬 트리 노드에서 직업 텍스트를 더 이상 표시하지 않음
        if (jobText != null) jobText.gameObject.SetActive(false);
    }

    public void SetupEmpty(GemTreeNode parent, int slotIdx, int depth)
    {
        _parentData = parent;
        _slotIndex = slotIdx;
        _isEmptySlot = true;

        if (emptyVisual != null) emptyVisual.SetActive(true);
        if (filledVisual != null) filledVisual.SetActive(false);
        if (jobText != null) jobText.text = "";
    }

    // --- 마우스 피드백 (Highlight) ---

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 드래그 중일 때만 빈 슬롯을 하이라이트
        if (_isEmptySlot && eventData.dragging && highlightOverlay != null)
        {
            highlightOverlay.gameObject.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (highlightOverlay != null)
        {
            highlightOverlay.gameObject.SetActive(false);
        }
    }

    // --- 드롭 처리 ---

    public void OnDrop(PointerEventData eventData)
    {
        if (highlightOverlay != null) highlightOverlay.gameObject.SetActive(false);

        if (!_isEmptySlot) return;

        // 드래그 중인 슬롯 가져오기
        GameObject draggedObj = eventData.pointerDrag;
        if (draggedObj == null) return;

        var invenSlot = draggedObj.GetComponent<GemInventorySlotUI>();
        if (invenSlot != null && invenSlot.Gem != null)
        {
            // 인벤토리 매니저를 통해 실제 장착 실행
            bool success = InventoryManager.Instance.SocketGem(_parentData.Gem.InstanceId, _slotIndex, invenSlot.Gem);
            
            if (success)
            {
                // 성공 시 UI 전체 리프레시
                GemTreeUI.Instance.RefreshUI();
            }
        }
    }
}

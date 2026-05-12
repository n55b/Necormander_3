using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 인벤토리 목록의 개별 보석 아이템을 관리하고 드래그 기능을 제공합니다.
/// </summary>
public class GemInventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    // [추가] 모든 인스턴스가 공유하는 현재 드래그 중인 고스트 참조
    private static GameObject _currentDragGhost;

    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI jobText;

    private GemInstance _gem;
    private Canvas _canvas;

    public GemInstance Gem => _gem;

    public void Setup(GemInstance gem)
    {
        _gem = gem;
        if (iconImage != null) iconImage.sprite = gem.BaseData.icon;
        if (nameText != null) nameText.text = $"{gem.BaseData.itemName} (Slots: {gem.SubSlots})";
        
        // [수정] 젬 인벤토리 슬롯에서 직업 텍스트를 더 이상 표시하지 않음
        if (jobText != null) jobText.gameObject.SetActive(false);
        
        _canvas = GetComponentInParent<Canvas>();
    }

    // --- 툴팁 핸들러 ---

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (GemTooltipUI.Instance != null && _gem != null)
        {
            GemTooltipUI.Instance.Show(_gem);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (GemTooltipUI.Instance != null)
        {
            GemTooltipUI.Instance.Hide();
        }
    }

    // --- 드래그 핸들러 ---

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_gem == null) return;

        // [수정] 새로운 드래그가 시작되면, 혹시 남아있을지 모를 이전 고스트를 파괴
        if (_currentDragGhost != null)
        {
            Destroy(_currentDragGhost);
        }

        _currentDragGhost = new GameObject("GemDragGhost");
        _currentDragGhost.transform.SetParent(_canvas.transform, false);
        _currentDragGhost.transform.SetAsLastSibling();
        
        Image ghostImage = _currentDragGhost.AddComponent<Image>();
        ghostImage.sprite = _gem.BaseData.icon;
        ghostImage.raycastTarget = false;
        ghostImage.rectTransform.sizeDelta = new Vector2(50, 50);

        UpdateGhostPosition(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_currentDragGhost != null)
        {
            UpdateGhostPosition(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_currentDragGhost != null)
        {
            Destroy(_currentDragGhost);
            _currentDragGhost = null;
        }
    }

    private void UpdateGhostPosition(PointerEventData eventData)
    {
        Vector2 pos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.transform as RectTransform, 
            eventData.position, 
            _canvas.worldCamera, 
            out pos
        );
        _currentDragGhost.transform.localPosition = pos;
    }
}

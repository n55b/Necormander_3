using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 10개 핸드 슬롯 중 하나의 상태를 표시하고 장착 버튼을 제공하는 개별 슬롯 UI 요소입니다.
/// </summary>
public class HandSlotSelectionItem : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI infoText; // 아이템 이름 또는 슬롯 번호 표시
    [SerializeField] private Button equipButton;

    private int _slotIndex;
    private HandSlotSelectionUI _parentUI;

    public void Setup(int index, InventoryManager.CoreSlot slot, HandSlotSelectionUI parent)
    {
        _slotIndex = index;
        _parentUI = parent;

        var itemData = slot.GetCurrentItemData();

        // [추가] 텍스트 칸 로직: 아이템이 있으면 이름, 없으면 슬롯 번호
        if (infoText != null)
        {
            if (itemData != null && !string.IsNullOrEmpty(itemData.itemName))
                infoText.text = itemData.itemName;
            else
                infoText.text = $"Slot {index + 1}";
        }

        // 슬롯의 현재 내용물 아이콘 표시
        if (iconImage != null)
        {
            if (itemData != null && itemData.icon != null)
            {
                iconImage.sprite = itemData.icon;
                iconImage.gameObject.SetActive(true);
            }
            else
            {
                iconImage.gameObject.SetActive(false);
            }

            if (slot.IsShattered)
            {
                iconImage.color = Color.black; // 잠긴 슬롯 표시
            }
            else
            {
                iconImage.color = Color.white;
            }
        }

        if (equipButton != null)
        {
            equipButton.interactable = !slot.IsShattered;
            equipButton.onClick.RemoveAllListeners();
            equipButton.onClick.AddListener(() => _parentUI.OnSlotSelected(_slotIndex));
        }
    }
}

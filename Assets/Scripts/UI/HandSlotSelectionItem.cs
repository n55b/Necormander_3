using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// 10개 핸드 슬롯 중 하나의 상태를 표시하고 장착 버튼을 제공하는 개별 슬롯 UI 요소입니다.
/// </summary>
public class HandSlotSelectionItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI infoText; // 아이템 이름 또는 슬롯 번호 표시
    [SerializeField] private Button equipButton;

    private int _slotIndex;
    private HandSlotSelectionUI _parentUI;
    private InventoryManager.CoreSlot _currentSlot;

    public void Setup(int index, InventoryManager.CoreSlot slot, HandSlotSelectionUI parent, bool isReadOnly)
    {
        _slotIndex = index;
        _parentUI = parent;
        _currentSlot = slot; // [추가] 툴팁용 슬롯 데이터 저장

        var itemData = slot.GetCurrentItemData();

        // [추가] 텍스트 칸 로직: 아이템이 있으면 이름, 없으면 슬롯 번호
        if (infoText != null)
        {
            if (itemData != null && !string.IsNullOrEmpty(itemData.itemName))
            {
                if (itemData.localizedItemName != null && !itemData.localizedItemName.IsEmpty)
                {
                    var op = itemData.localizedItemName.GetLocalizedStringAsync();
                    if (op.IsDone) infoText.text = op.Result;
                    else 
                    {
                        var handle = op;
                        handle.WaitForCompletion();
                        infoText.text = handle.Result;
                    }
                    
                    if (string.IsNullOrEmpty(infoText.text) || infoText.text.StartsWith("No translation"))
                    {
                        infoText.text = itemData.itemName;
                    }
                }
                else
                {
                    infoText.text = itemData.itemName;
                }
            }
            else
            {
                infoText.text = GetUIString("UI_Slot_Empty", index + 1);
            }
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
            // [수정] 조회 모드에서는 상호작용 불가
            equipButton.interactable = !slot.IsShattered && !isReadOnly;
            
            equipButton.onClick.RemoveAllListeners();
            if (!isReadOnly)
            {
                equipButton.onClick.AddListener(() => _parentUI.OnSlotSelected(_slotIndex));
            }
        }
    }

    #region Tooltip Logic

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_currentSlot == null || _currentSlot.IsEmpty || CommonTooltipUI.Instance == null) return;

        var itemData = _currentSlot.GetCurrentItemData();
        if (itemData == null) return;

        TooltipData data = new TooltipData(itemData.itemName, itemData.description);
        data.localizedTitle = itemData.localizedItemName;
        data.localizedDescription = itemData.localizedDescription;
        
        if (_currentSlot.EquippedLineage != null)
        {
            // 미니언 정보 구성
            var minion = _currentSlot.GetCurrentMinionData();
            
            string minionLocalizedName = itemData.itemName;
            var nameOp = itemData.localizedItemName.GetLocalizedStringAsync();
            if (nameOp.IsDone) minionLocalizedName = nameOp.Result;
            else { var handle = nameOp; handle.WaitForCompletion(); minionLocalizedName = handle.Result; }

            data.type = $"<color=#FFD700>{GetUIString("UI_Minion_Prefix", minionLocalizedName)}</color>";
            data.titleColor = new Color(0.8f, 1f, 0.8f);
            
            var stats = CharacterStat.GetPreviewStats(minion);

            data.effects = new List<string> {
                $"{GetUIString("UI_Stat_HP")}: {stats.hp:F1}",
                $"{GetUIString("UI_Stat_ATK")}: {stats.atk:F1}",
                $"{GetUIString("UI_Stat_SPD")}: {stats.spd:F1}",
                $"<color=#AAAAAA>{GetUIString("UI_Count", _currentSlot.Quantity)}</color>"
            };
        }
        else if (_currentSlot.EquippedThrowAbility != null)
        {
            // 능력 정보 구성
            var ability = _currentSlot.EquippedThrowAbility;
            data.type = $"<color=#00BFFF>{GetUIString("UI_ThrowAbility_Prefix", ability.rarity)}</color>";
            data.titleColor = new Color(0.8f, 0.9f, 1f);
            
            // 능력은 설명에 상세 수치가 포함되어 있는 경우가 많으므로 기본 정보만 표시
            data.effects = new List<string> {
                $"<color=#FF7F50>{GetUIString("UI_Equipped_Capability")}</color>"
            };
        }
        else return;

        CommonTooltipUI.Instance.Show(data);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (CommonTooltipUI.Instance != null)
            CommonTooltipUI.Instance.Hide();
    }

    private void OnDisable()
    {
        if (CommonTooltipUI.Instance != null)
            CommonTooltipUI.Instance.Hide();
    }

    private string GetUIString(string key, params object[] args)
    {
        var op = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.GetLocalizedStringAsync("UI Text Table", key, arguments: args);
        if (op.IsDone)
            return op.Result;
        
        var handle = op;
        handle.WaitForCompletion();
        return handle.Result;
    }
    #endregion
}

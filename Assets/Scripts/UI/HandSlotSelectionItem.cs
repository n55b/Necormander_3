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

    /// <param name="accepts">지금 고른 보상이 이 칸에 들어갈 수 있는지. false 면 장착 버튼을 잠근다.</param>
    public void Setup(int index, InventoryManager.CoreSlot slot, HandSlotSelectionUI parent, bool isReadOnly, bool accepts = true)
    {
        _slotIndex = index;
        _parentUI = parent;
        _currentSlot = slot; // [추가] 툴팁용 슬롯 데이터 저장

        var itemData = slot.GetCurrentItemData();

        // [수정] 빈 슬롯 판정은 itemData 존재 여부로만 하고, 표시 텍스트는 내부에서 localizedItemName -> itemName 순으로 폴백합니다.
        // (itemName 필드가 비어있고 localizedItemName만 채워진 아이템의 경우 '비어있음'으로 잘못 표시되던 버그 수정)
        if (infoText != null)
        {
            if (itemData != null)
            {
                string resolvedName = null;

                if (itemData.localizedItemName != null && !itemData.localizedItemName.IsEmpty)
                {
                    var op = itemData.localizedItemName.GetLocalizedStringAsync();
                    if (op.IsDone) resolvedName = op.Result;
                    else 
                    {
                        var handle = op;
                        handle.WaitForCompletion();
                        resolvedName = handle.Result;
                    }

                    if (string.IsNullOrEmpty(resolvedName) || resolvedName.StartsWith("No translation"))
                    {
                        resolvedName = itemData.itemName;
                    }
                }
                else
                {
                    resolvedName = itemData.itemName;
                }

                infoText.text = !string.IsNullOrEmpty(resolvedName) ? resolvedName : GetUIString("UI_Slot_Empty", index + 1);
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
            // accepts: 메인 소환수는 메인 칸에만, 서브는 서브 칸에만 들어간다.
            equipButton.interactable = accepts && !slot.IsShattered && !isReadOnly;
            
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
        
        if (_currentSlot.EquippedMinion != null)
        {
            // 미니언 정보 구성
            var minion = _currentSlot.GetCurrentMinionData();
            
            string minionLocalizedName = itemData.itemName;
            if (itemData.localizedItemName != null && !itemData.localizedItemName.IsEmpty)
            {
                var nameOp = itemData.localizedItemName.GetLocalizedStringAsync();
                if (nameOp.IsDone) minionLocalizedName = nameOp.Result;
                else { var handle = nameOp; handle.WaitForCompletion(); minionLocalizedName = handle.Result; }
            }

            data.type = $"<color=#FFD700>{GetUIString("UI_Minion_Prefix", minionLocalizedName)}</color>";
            data.titleColor = new Color(0.8f, 1f, 0.8f);

            // 메인은 액티브 스킬 설명, 서브는 패시브 수치에서 생성된 설명이 나온다.
            string desc = minion.ResolveDescription();
            if (!string.IsNullOrEmpty(desc))
            {
                data.description = desc;
                data.localizedDescription = null; // 소환수 설명은 로컬라이즈 대상이 아니므로 참조 해제
            }

            // [수정] 미니언 스탯 및 보유 수량은 더 이상 표시하지 않습니다 (스킬 설명만 표시).
            data.effects = null;
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

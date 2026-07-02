using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 개별 미니언 직업의 보석 장착 상태를 표시하고 장착 버튼을 제공하는 UI 항목입니다.
/// </summary>
public class GemSlotSelectionItem : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image minionIcon;
    [SerializeField] private TextMeshProUGUI jobNameText;
    
    [Header("Gem Slot Buttons (Connect only 2 Buttons)")]
    [SerializeField] private Button[] gemButtons; // 2개 고정, 텍스트는 자동으로 찾음

    private CommandData _jobType;
    private GemSlotSelectionUI _parentUI;

    public void Setup(CommandData job, MinionDataSO minion, GemSlotSelectionUI parent)
    {
        _jobType = job;
        _parentUI = parent;

        if (jobNameText != null) jobNameText.text = job.ToString().Replace("Skeleton", "");
        
        if (minionIcon != null && minion != null)
        {
            minionIcon.sprite = minion.rewardItemData != null && minion.rewardItemData.icon != null ? minion.rewardItemData.icon : minion.minionIcon;
        }

        // 새로운 보석 트리 시스템에서는 미니언 직업에 직접 보석이 장착되지 않습니다.
        // 이 UI는 향후 보석 트리 구조를 반영하여 장착된 보석을 표시하도록 리팩토링되어야 합니다.

        for (int i = 0; i < gemButtons.Length; i++)
        {
            if (gemButtons[i] == null) continue;

            int slotIndex = i;
            gemButtons[i].onClick.RemoveAllListeners();
            gemButtons[i].onClick.AddListener(() => _parentUI.OnGemSlotSelected(_jobType, slotIndex));

            var nameText = gemButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            if (nameText != null)
            {
                nameText.text = $"Slot {slotIndex + 1} (Empty)"; // 임시 텍스트
            }
        }

    }
}

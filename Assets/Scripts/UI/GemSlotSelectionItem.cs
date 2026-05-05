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

    public void Setup(CommandData job, MinionLineageSO lineage, GemSlotSelectionUI parent)
    {
        _jobType = job;
        _parentUI = parent;

        if (jobNameText != null) jobNameText.text = job.ToString().Replace("Skeleton", "");
        
        if (minionIcon != null && lineage != null)
        {
            minionIcon.sprite = lineage.baseItemData.icon;
        }

        // 현재 장착된 보석 정보 로드
        var equippedGems = InventoryManager.Instance.GetEquippedGems(job);

        for (int i = 0; i < gemButtons.Length; i++)
        {
            if (gemButtons[i] == null) continue;

            int slotIndex = i;
            gemButtons[i].onClick.RemoveAllListeners();
            gemButtons[i].onClick.AddListener(() => _parentUI.OnGemSlotSelected(_jobType, slotIndex));

            // [자동 찾기] 버튼 내부의 TextMeshProUGUI 컴포넌트를 긁어옵니다.
            var nameText = gemButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            if (nameText != null)
            {
                if (equippedGems != null && slotIndex < equippedGems.Count && equippedGems[slotIndex] != null)
                {
                    nameText.text = equippedGems[slotIndex].itemName;
                }
                else
                {
                    nameText.text = "Empty";
                }
            }
        }
    }
}

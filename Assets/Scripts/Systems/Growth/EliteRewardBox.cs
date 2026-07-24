using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어가 상호작용하여 엘리트 등급의 보상을 얻을 수 있는 상자입니다.
/// </summary>
public class EliteRewardBox : MonoBehaviour, IInteractable
{
    [Header("보상 설정")]
    [Tooltip("체크 시 던지기 능력/환골탈태/보석이 모두 나옵니다.")]
    [SerializeField] private bool isSuperEliteBox = false;

    public string InteractionPrompt => "Open Elite Reward";

    public bool Interact(GameObject interactor)
    {
        var inven = InventoryManager.Instance;
        var data = GameManager.Instance.dataManager;

        // [보상 개편 26/07/24] 보상방 = 메인 소환수 획득. 1장 택1 + 스킵(RewardSelectionUI 의 skip 버튼).
        // 메인 슬롯 1개라 이미 있으면 HandSlot 픽에서 교체된다. 슈퍼 상자는 2장 중 택1(카드 수만 다름).
        int cardCount = isSuperEliteBox ? 2 : 1;
        List<RewardCandidate> rewards = RewardProcessor.GenerateSummonRewards(
            inven, data, typeof(MainMinionDataSO), cardCount);

        if (rewards.Count > 0)
        {
            // RewardSelectionUI를 통해 보상 선택 창 표시
            RewardManager.Instance.ShowRewardSelection(rewards);
            Debug.Log($"<color=magenta>[EliteRewardBox]</color> Opened, presenting {rewards.Count} rewards.");
            
            // 상호작용 후 자기 자신을 파괴
            Destroy(gameObject);
            return true;
        }

        Debug.LogWarning("[EliteRewardBox] No valid rewards to offer.");
        return false;
    }

    public void OnFocused(GameObject interactor){}
    public void OnLostFocus(GameObject interactor){}
}

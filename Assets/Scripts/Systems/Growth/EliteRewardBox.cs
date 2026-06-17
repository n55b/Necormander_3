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
        List<RewardCandidate> rewards;

        if (isSuperEliteBox)
        {
            // 모든 좋은 보상(능력, 환골탈태, 보석) 풀에서 3개 추첨
            rewards = RewardProcessor.GenerateMixedCandidates(inven, data,
                new List<RewardCategory> { RewardCategory.Ability, RewardCategory.Metamorphosis, RewardCategory.Gem });
        }
        else
        {
            // [수정] 보스 보상과 동일하게 능력과 환골탈태를 섞어서 3개 추첨 (보석 제외)
            rewards = RewardProcessor.GenerateMixedCandidates(inven, data,
                new List<RewardCategory> { RewardCategory.Ability, RewardCategory.Metamorphosis });
        }

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

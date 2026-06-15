using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보상 후보 3개를 보여주고 선택을 받는 범용 UI 클래스입니다.
/// </summary>
public class RewardSelectionUI : MonoBehaviour
{
    [Header("UI Containers")]
    [SerializeField] private GameObject panel;
    [SerializeField] private RewardCard[] cards; // 3개 고정
    [SerializeField] private Button skipButton;

    private List<RewardCandidate> _currentCandidates;

    private void Awake()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipClicked);
            
        // 초기 상태는 비활성화
        Hide();
    }

    public void Show(List<RewardCandidate> candidates)
    {
        UIPopUpManager.Instance.ForcePopUpUI(panel); // 팝업 매니저에 상태 전달

        _currentCandidates = candidates;
        if (panel != null) panel.SetActive(true);
        UIEventBus.NotifyOpen("Reward");
        UIEventBus.NotifyOpen("Reward");


        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) continue; // [방어 코드] 카드가 할당되지 않은 경우 스킵

            if (i < candidates.Count)
            {
                cards[i].gameObject.SetActive(true);
                cards[i].Setup(candidates[i], i);
            }
            else
            {
                cards[i].gameObject.SetActive(false);
            }
        }
    }

    public void Hide()
    {
        Debug.Log("<color=white>[RewardUI]</color> Hiding Selection UI.");
        if (panel != null) panel.SetActive(false);
        UIPopUpManager.Instance?.ClosePopUpUI();
        UIEventBus.NotifyClose("Reward"); // 팝업 상태 해제태 해제
        UIEventBus.NotifyClose("Reward");

    }

    public void OnCardClicked(int index)
    {
        if (index >= 0 && index < _currentCandidates.Count)
        {
            RewardManager.Instance.ApplyReward(_currentCandidates[index]);
        }
    }

    private void OnSkipClicked()
    {
        RewardManager.Instance.SkipReward();
    }
}

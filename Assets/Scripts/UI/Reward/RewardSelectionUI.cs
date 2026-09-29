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
        _currentCandidates = candidates;
        // Modal: 맵·주머니·장착 정보는 매니저가 닫고, 보상창은 항상 뜬다. (SetActive 도 매니저가 한다)
        if (UIPopUpManager.Instance != null) UIPopUpManager.Instance.Open(panel, UIPopUpManager.Layer.Modal);
        else if (panel != null) panel.SetActive(true);
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
        // 내 창만 닫는다 (Awake 에서 불려도 다른 창에 영향 없음)
        if (UIPopUpManager.Instance != null && UIPopUpManager.Instance.IsOpen(panel)) UIPopUpManager.Instance.Close(panel);
        else if (panel != null) panel.SetActive(false);
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

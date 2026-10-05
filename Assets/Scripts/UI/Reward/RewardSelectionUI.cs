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

    // 콜백 모드: RewardManager 를 거치지 않고 호출자가 직접 결과를 받는다(마을 소환수 선택 NPC 등).
    // null 이면 기존처럼 RewardManager 로 보낸다.
    private System.Action<RewardCandidate> _onPicked;
    private System.Action _onSkipped;

    /// <summary>
    /// 보상 파이프라인 밖에서 쓰는 선택창. 카드를 고르면 onPicked, 스킵하면 onSkipped 가 불린다.
    /// 창을 닫는 것은 호출자 몫이다(Hide).
    /// </summary>
    public void ShowWithCallbacks(List<RewardCandidate> candidates, System.Action<RewardCandidate> onPicked, System.Action onSkipped)
    {
        Show(candidates);
        _onPicked = onPicked;
        _onSkipped = onSkipped;
    }

    private void Awake()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipClicked);
            
        // 초기 상태는 비활성화
        Hide();
    }

    public void Show(List<RewardCandidate> candidates)
    {
        _onPicked = null; // 일반 Show 는 항상 RewardManager 경로. 콜백 모드는 ShowWithCallbacks 가 다시 채운다.
        _onSkipped = null;
        _currentCandidates = candidates;
        // Modal: 맵·주머니·장착 정보는 매니저가 닫고, 보상창은 항상 뜬다. (SetActive 도 매니저가 한다)
        if (UIPopUpManager.Instance != null) UIPopUpManager.Instance.Open(panel, UIPopUpManager.Layer.Modal);
        else if (panel != null) panel.SetActive(true);
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
        UIEventBus.NotifyClose("Reward"); // 팝업 상태 해제
    }

    public void OnCardClicked(int index)
    {
        if (_currentCandidates == null || index < 0 || index >= _currentCandidates.Count) return;

        if (_onPicked != null)
        {
            var cb = _onPicked;
            _onPicked = null; _onSkipped = null; // 연타로 두 번 지급되지 않게 먼저 끊는다
            cb(_currentCandidates[index]);
            return;
        }
        RewardManager.Instance.ApplyReward(_currentCandidates[index]);
    }

    private void OnSkipClicked()
    {
        if (_onSkipped != null || _onPicked != null)
        {
            var cb = _onSkipped;
            _onPicked = null; _onSkipped = null;
            cb?.Invoke();
            return;
        }
        RewardManager.Instance.SkipReward();
    }
}

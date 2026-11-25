using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;

/// <summary>
/// 보상 후보 3개를 보여주고 선택을 받는 범용 UI 클래스입니다.
/// </summary>
public class RewardSelectionUI : MonoBehaviour
{
    [Header("UI 컨테이너")]
    [SerializeField] private GameObject panel;
    [SerializeField] private RewardCard[] cards; // 3개 고정
    [SerializeField] private Button skipButton;
    [SerializeField] private TextMeshProUGUI heading;
    private readonly LocalizedString titleString = new();
    private readonly LocalizedString skipString = new("UI Text Table", "Choice_Skip");
    private TextMeshProUGUI skipLabel;

    private List<RewardCandidate> _currentCandidates;

    // 콜백 모드: RewardManager 를 거치지 않고 호출자가 직접 결과를 받는다(마을 소환수 선택 NPC 등).
    // null 이면 기존처럼 RewardManager 로 보낸다.
    private System.Action<RewardCandidate> _onPicked;
    private System.Action _onSkipped;

    /// <summary>
    /// 보상 파이프라인 밖에서 쓰는 선택창. 카드를 고르면 onPicked, 스킵하면 onSkipped 가 불린다.
    /// 창을 닫는 것은 호출자 몫이다(Hide).
    /// </summary>
    public void ShowWithCallbacks(List<RewardCandidate> candidates, System.Action<RewardCandidate> onPicked, System.Action onSkipped,
        string titleKey = "Choice_Reward")
    {
        ShowChoices(candidates, titleKey);
        _onPicked = onPicked;
        _onSkipped = onSkipped;
    }

    private void Awake()
    {
        if (skipButton != null)
        {
            skipButton.onClick.AddListener(OnSkipClicked);
            skipLabel = skipButton.GetComponentInChildren<TextMeshProUGUI>(true);
        }
            
        // 초기 상태는 비활성화
        Hide();
    }

    public void Show(List<RewardCandidate> candidates) => ShowChoices(candidates, "Choice_Reward");

    private void ShowChoices(List<RewardCandidate> candidates, string titleKey)
    {
        _onPicked = null; // 일반 Show 는 항상 RewardManager 경로. 콜백 모드는 ShowWithCallbacks 가 다시 채운다.
        _onSkipped = null;
        _currentCandidates = candidates;
        PixelViewport.FitLogicalFrame((RectTransform)transform);
        UnbindLabels();
        titleString.SetReference("UI Text Table", titleKey);
        titleString.StringChanged += SetHeading;
        skipString.StringChanged += SetSkip;
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
        SetNavigation();
    }

    private void SetNavigation()
    {
        Button first = null, previous = null;
        foreach (var card in cards)
        {
            if (card == null || !card.gameObject.activeSelf) continue;
            var button = card.GetComponent<Button>();
            if (first == null) first = button;
            button.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = previous, selectOnDown = skipButton };
            if (previous != null)
            {
                var back = previous.navigation;
                back.selectOnRight = button;
                previous.navigation = back;
            }
            previous = button;
        }
        if (skipButton != null)
            skipButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = previous };
        if (first != null) first.Select();
        else if (skipButton != null) skipButton.Select();
    }

    private void SetHeading(string value) { if (heading != null) heading.text = $"<type=1.5>{value}</type>"; }
    private void SetSkip(string value) { if (skipLabel != null) skipLabel.text = value; }
    private void UnbindLabels()
    {
        titleString.StringChanged -= SetHeading;
        skipString.StringChanged -= SetSkip;
    }
    private void OnDestroy() => UnbindLabels();

    public void Hide()
    {
        _currentCandidates = null;
        UnbindLabels();
        // 내 창만 닫는다 (Awake 에서 불려도 다른 창에 영향 없음)
        if (UIPopUpManager.Instance != null && UIPopUpManager.Instance.IsOpen(panel)) UIPopUpManager.Instance.Close(panel);
        else if (panel != null) panel.SetActive(false);
        UIEventBus.NotifyClose("Reward"); // 팝업 상태 해제
    }

    public void OnCardClicked(int index)
    {
        if (_currentCandidates == null || index < 0 || index >= _currentCandidates.Count) return;
        var choice = _currentCandidates[index];
        _currentCandidates = null;

        if (_onPicked != null)
        {
            var cb = _onPicked;
            _onPicked = null; _onSkipped = null; // 연타로 두 번 지급되지 않게 먼저 끊는다
            cb(choice);
            return;
        }
        RewardManager.Instance.ApplyReward(choice);
    }

    private void OnSkipClicked()
    {
        if (_currentCandidates == null) return;
        _currentCandidates = null;
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

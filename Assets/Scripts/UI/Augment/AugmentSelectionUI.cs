using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 증강 방에서 전투 시작 직전에 뜨는 증강 선택 창.
///
/// 상단 카드 N장(전부 페널티 있음) + 하단 P0 버튼 1개. P0 은 언제나 고를 수 있고,
/// 아무 디버프 없이 소소한 보상만 받는다.
///
/// 보상 창(RewardSelectionUI)과 따로 만든 이유: 레이아웃이 3장+스킵이 아니라 4장+전용 버튼이고,
/// 여길 건드리려고 보상 창을 고치면 상점/보상방 흐름까지 같이 흔들린다.
/// </summary>
public class AugmentSelectionUI : MonoBehaviour
{
    public static AugmentSelectionUI Instance;

    [Header("UI")]
    [Tooltip("창 전체. 평소엔 꺼져 있다.")]
    [SerializeField] private GameObject panelRoot;
    [Tooltip("상단 카드들. 테이블의 cardCount 보다 많으면 남는 칸은 자동으로 꺼진다.")]
    [SerializeField] private AugmentCard[] cards;
    [Tooltip("하단 P0(무위험) 버튼. 카드와 같은 스크립트를 쓴다.")]
    [SerializeField] private AugmentCard noRiskCard;
    [Tooltip("창 제목. 비워도 된다.")]
    [SerializeField] private TextMeshProUGUI titleText;

    private Action<AugmentOffer> _onPicked;

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        Instance = this;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    /// <summary>
    /// 선택지를 띄운다. 고르기 전까지는 시간이 멈춰 있고(UIPopUpManager 가 처리),
    /// 고르는 순간 onPicked 가 딱 한 번 불린다.
    /// </summary>
    public void Show(List<AugmentOffer> offers, AugmentOffer noRisk, Action<AugmentOffer> onPicked)
    {
        _onPicked = onPicked;

        if (panelRoot == null)
        {
            // 창이 없으면 게임이 멈춰 버리니, 아무것도 못 고른 셈 치고 바로 전투로 넘긴다.
            Debug.LogError("[AugmentSelectionUI] panelRoot 가 비어 있다. 선택 없이 전투를 시작한다.");
            Pick(null);
            return;
        }

        panelRoot.SetActive(true);
        UIPopUpManager.Instance?.ForcePopUpUI(panelRoot); // 시간 정지 포함
        UIEventBus.NotifyOpen("Augment");

        if (titleText != null) titleText.text = "무엇을 짊어질 것인가";

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) continue;
            cards[i].Setup(offers != null && i < offers.Count ? offers[i] : null, this);
        }

        if (noRiskCard != null) noRiskCard.Setup(noRisk, this);
    }

    /// <summary>카드(또는 P0 버튼)를 눌렀다.</summary>
    public void Pick(AugmentOffer offer)
    {
        var cb = _onPicked;
        _onPicked = null; // 콜백 안에서 전투가 시작되므로, 먼저 비워 재진입을 막는다

        Hide();
        cb?.Invoke(offer);
    }

    public void Hide()
    {
        if (panelRoot != null && !panelRoot.activeSelf) return;
        if (panelRoot != null) panelRoot.SetActive(false);
        UIPopUpManager.Instance?.ClosePopUpUI(); // 시간 재개
        UIEventBus.NotifyClose("Augment");
    }
}

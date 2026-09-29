using UnityEngine;

/// <summary>미니언 습득/교체. 슬롯 고르기 없이 현재 미니언과 바닥 후보를 비교한다.</summary>
public class HandSlotSelectionUI : Singleton<HandSlotSelectionUI>
{
    [SerializeField] private GameObject panel;
    [SerializeField] private SkillExplainSlotUI currentCard;
    [SerializeField] private SkillExplainSlotUI candidateCard;
    [SerializeField] private SkillExplainSlotUI currentDetails;
    [SerializeField] private SkillExplainSlotUI candidateDetails;
    [SerializeField] private GameObject comparisonTarget;
    [SerializeField] private GameObject replaceButton;
    [SerializeField] private RectTransform singleCardAnchor;
    [SerializeField] private RectTransform candidateAnchor;
    [SerializeField, Min(0.1f)] private float confirmHoldSeconds = 3f;
    private GroundItem _source;
    private System.Action _onComplete;
    private bool _open;
    public bool IsOpen => _open;
    public float ConfirmHoldSeconds => confirmHoldSeconds;

    protected override void OnAwake() { panel.SetActive(false); }
    public void ToggleReadOnly() => SkillExplainUI.Instance?.Toggle();

    // NPC도 실제 바닥 픽업을 먼저 만든다. 스킵해도 없어지지 않는다.
    public void Show(RewardCandidate candidate)
    {
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (_open || player == null || !(candidate.rawData is MinionDataSO minion)) return;
        var drop = GroundItem.Drop(minion, player.transform.position);
        if (drop != null) Show(drop);
    }

    public void Show(GroundItem source, System.Action onComplete = null)
    {
        if (_open || source == null || !source.IsAvailable || source.Minion == null) return;
        PouchUI.Instance?.SetOpen(false);
        SkillExplainUI.Instance?.SetOpen(false);
        _source = source;
        _onComplete = onComplete;
        _open = true;
        gameObject.SetActive(true);
        // Modal 로 panel 을 등록한다(예전엔 싱글톤 루트를 등록해서 닫힐 때 루트째 꺼졌다). 주머니·장착 정보는 매니저가 닫는다.
        if (UIPopUpManager.Instance != null) UIPopUpManager.Instance.Open(panel, UIPopUpManager.Layer.Modal, OnPanelClosed);
        else panel.SetActive(true);
        UIEventBus.NotifyOpen("HandSlot");
        RefreshSlots();
    }

    public void RefreshSlots()
    {
        var current = InventoryManager.Instance != null ? InventoryManager.Instance.MainSummon : null;
        var candidate = _source != null ? _source.Minion : null;
        currentCard.gameObject.SetActive(current != null);
        if (current != null) currentCard.SetData(current.minionIcon, current.minionName, "현재 장착");
        candidateCard.SetData(candidate != null ? candidate.minionIcon : null,
            candidate != null ? candidate.minionName : "", current != null ? "교체 후보" : "클릭하여 습득");
        var anchor = current != null ? candidateAnchor : singleCardAnchor;
        if (anchor != null) candidateCard.transform.position = anchor.position;
        comparisonTarget.SetActive(current != null);
        replaceButton.SetActive(current != null);
        Hover(HandSlotSelectionItem.ActionKind.Compare, false);
    }

    public static string Describe(MinionDataSO data)
    {
        if (!(data is MainMinionDataSO main)) return data != null ? data.ResolveDescription() : "";
        return $"<b>Space · 미니언 스킬</b>\n{main.minionSkill?.description}\n\n" +
               $"<b>기본 공격 마무리</b>\n{main.finisher.Describe()}\n\n" +
               $"<b>대쉬</b>\n{main.dashModifier.Describe()}";
    }

    public void Hover(HandSlotSelectionItem.ActionKind kind, bool show)
    {
        var current = InventoryManager.Instance != null ? InventoryManager.Instance.MainSummon : null;
        var candidate = _source != null ? _source.Minion : null;
        bool left = show && current != null && (kind == HandSlotSelectionItem.ActionKind.Current || kind == HandSlotSelectionItem.ActionKind.Compare);
        bool right = show && candidate != null && (kind == HandSlotSelectionItem.ActionKind.Candidate || kind == HandSlotSelectionItem.ActionKind.Compare);
        currentDetails.gameObject.SetActive(left);
        candidateDetails.gameObject.SetActive(right);
        if (left) currentDetails.SetData(current.minionIcon, current.minionName, Describe(current));
        if (right) candidateDetails.SetData(candidate.minionIcon, candidate.minionName, Describe(candidate));
    }

    public void Select(HandSlotSelectionItem.ActionKind kind)
    {
        if (!_open || _source == null || !_source.IsAvailable) { Hide(); return; }
        if (kind == HandSlotSelectionItem.ActionKind.Skip) { Complete(); return; }
        var inventory = InventoryManager.Instance;
        if (inventory == null) return;
        var old = inventory.MainSummon;
        if (kind == HandSlotSelectionItem.ActionKind.Candidate && old != null) return;
        if (kind != HandSlotSelectionItem.ActionKind.Candidate && kind != HandSlotSelectionItem.ActionKind.Replace) return;
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (player == null) return;
        var oldDrop = old != null ? GroundItem.Drop(old, player.transform.position) : null;
        if (old != null && oldDrop == null) return;
        if (!inventory.EquipMinion(_source.Minion))
        {
            if (oldDrop != null) oldDrop.Consume();
            return;
        }
        _source.Consume();
        Complete();
    }

    public void OnSlotSelected(int index) => Select(HandSlotSelectionItem.ActionKind.Candidate);
    private void OnDisable()
    {
        if (_open)
        {
            UIPopUpManager.Instance?.Forget(panel);
            UIEventBus.NotifyClose("HandSlot");
        }
        _open = false;
        _source = null;
        _onComplete = null;
    }
    private void Complete() { var done = _onComplete; Hide(); done?.Invoke(); }
    public void Hide()
    {
        if (!_open) return;
        // 내 창만 닫는다. 정리는 OnPanelClosed 에서.
        var mgr = UIPopUpManager.Instance;
        if (mgr != null && mgr.IsOpen(panel)) mgr.Close(panel);
        else { panel.SetActive(false); OnPanelClosed(); }
    }

    private void OnPanelClosed()
    {
        if (!_open) return;
        _open = false;
        _source = null;
        _onComplete = null;
        CommonTooltipUI.Instance?.Hide();
        UIEventBus.NotifyClose("HandSlot");
    }
}

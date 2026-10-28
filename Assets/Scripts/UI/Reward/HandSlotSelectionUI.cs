using UnityEngine;

/// <summary>미니언 습득/교체. 슬롯 고르기 없이 현재 미니언과 후보를 비교한다.</summary>
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
    [SerializeField, Min(0.1f)] private float confirmHoldSeconds = 1f;
    [SerializeField, Min(0.1f)] private float skipHoldSeconds = 1f;

    // 후보는 둘 중 하나로 들고 있다.
    //  · _source  : 이미 바닥에 있는 픽업(바닥 소환수에 F, 마을 NPC). 스킵하면 그대로 바닥에 남는다.
    //  · _pending : 보상 상자 후보. 아직 바닥에 없다 — 스킵(또는 그냥 닫기)하는 순간 바닥에 떨어진다.
    private GroundItem _source;
    private MinionDataSO _pending;
    private bool _pendingResolved;
    private MinionDataSO Candidate => _source != null ? _source.Minion : _pending;

    private System.Action _onComplete;
    private bool _open;
    public bool IsOpen => _open;
    public float ConfirmHoldSeconds => confirmHoldSeconds;
    public float HoldSeconds(HandSlotSelectionItem.ActionKind action)
        => action == HandSlotSelectionItem.ActionKind.Skip ? skipHoldSeconds : confirmHoldSeconds;

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
        _source = source;
        _pending = null;
        _pendingResolved = true;
        Open(onComplete);
    }

    /// <summary>
    /// 보상 상자용. 창을 열 때는 아무것도 바닥에 떨어뜨리지 않는다.
    ///  · 스킵(또는 결정 없이 창이 닫힘) → 그때 후보 소환수가 플레이어 발밑에 떨어진다.
    ///  · 교체 → 기존 소환수가 떨어지고 후보를 장착한다.
    ///  · 빈 슬롯에 습득 → 아무것도 떨어지지 않는다.
    /// 창을 열지 못하면 false — 호출 쪽에서 예전처럼 바닥에 떨어뜨려 보상이 사라지지 않게 한다.
    /// </summary>
    public bool ShowPending(MinionDataSO minion, System.Action onComplete = null)
    {
        if (_open || minion == null) return false;
        _source = null;
        _pending = minion;
        _pendingResolved = false;
        Open(onComplete);
        return true;
    }

    private void Open(System.Action onComplete)
    {
        PouchUI.Instance?.SetOpen(false);
        SkillExplainUI.Instance?.SetOpen(false);
        _onComplete = onComplete;
        _open = true;
        gameObject.SetActive(true);
        // Modal 로 panel 을 등록한다(예전엔 싱글톤 루트를 등록해서 닫힐 때 루트째 꺼졌다). 주머니·장착 정보는 매니저가 닫는다.
        if (UIPopUpManager.Instance != null) UIPopUpManager.Instance.Open(panel, UIPopUpManager.Layer.Modal, OnPanelClosed);
        else panel.SetActive(true);
        UIEventBus.NotifyOpen("HandSlot");
        RefreshSlots();
    }

    /// <summary>보상 상자 모드에서 아직 결정되지 않은 후보를 플레이어 발밑에 떨어뜨린다(스킵/닫기).</summary>
    private void DropPendingIfUnresolved()
    {
        if (_pending == null || _pendingResolved) return;
        _pendingResolved = true;
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (player == null)
        {
            Debug.LogWarning($"[HandSlotSelectionUI] 플레이어가 없어 '{_pending.name}' 을(를) 떨어뜨리지 못했습니다.");
            return;
        }
        GroundItem.Drop(_pending, player.transform.position);
    }

    public void RefreshSlots()
    {
        var current = InventoryManager.Instance != null ? InventoryManager.Instance.MainSummon : null;
        var candidate = Candidate;
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
        var sb = new System.Text.StringBuilder();

        if (main.minionSkill != null)
        {
            string skillName = string.IsNullOrEmpty(main.minionSkill.skillName) ? main.minionSkill.name : main.minionSkill.skillName;
            sb.Append("[Space] ").Append(skillName).Append("\n").Append(main.minionSkill.description);
        }
        if (main.finisher != null && main.finisher.IsValid)
            sb.Append("\n\n[기본 공격]\n").Append(main.finisher.Describe());
        if (main.dashModifier != null)
            sb.Append("\n\n[Shift] 대시\n").Append(main.dashModifier.Describe());

        return sb.ToString();
    }

    public void Hover(HandSlotSelectionItem.ActionKind kind, bool show)
    {
        var current = InventoryManager.Instance != null ? InventoryManager.Instance.MainSummon : null;
        var candidate = Candidate;
        bool left = show && current != null && (kind == HandSlotSelectionItem.ActionKind.Current || kind == HandSlotSelectionItem.ActionKind.Compare);
        bool right = show && candidate != null && (kind == HandSlotSelectionItem.ActionKind.Candidate || kind == HandSlotSelectionItem.ActionKind.Compare);
        currentDetails.gameObject.SetActive(left);
        candidateDetails.gameObject.SetActive(right);
        if (left) currentDetails.SetData(current.minionIcon, current.minionName, Describe(current));
        if (right) candidateDetails.SetData(candidate.minionIcon, candidate.minionName, Describe(candidate));
    }

    public void Select(HandSlotSelectionItem.ActionKind kind)
    {
        var candidate = Candidate;
        if (!_open || candidate == null || (_source != null && !_source.IsAvailable)) { Hide(); return; }
        if (kind == HandSlotSelectionItem.ActionKind.Skip)
        {
            DropPendingIfUnresolved(); // 보상 상자 모드: 스킵하는 순간 후보가 바닥에 떨어진다
            Complete();
            return;
        }
        var inventory = InventoryManager.Instance;
        if (inventory == null) return;
        var old = inventory.MainSummon;
        if (kind == HandSlotSelectionItem.ActionKind.Candidate && old != null) return;
        if (kind != HandSlotSelectionItem.ActionKind.Candidate && kind != HandSlotSelectionItem.ActionKind.Replace) return;
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (player == null) return;
        var oldDrop = old != null ? GroundItem.Drop(old, player.transform.position) : null; // 교체: 기존 소환수가 바닥에 떨어진다
        if (old != null && oldDrop == null) return;
        if (!inventory.EquipMinion(candidate))
        {
            if (oldDrop != null) oldDrop.Consume();
            return;
        }
        if (_source != null)
        {
            _source.PlayPickupFeedback();
            _source.Consume();
        }
        else _pendingResolved = true; // 보상 상자 모드: 장착했으니 후보는 떨어뜨리지 않는다
        Complete();
    }

    public void OnSlotSelected(int index) => Select(HandSlotSelectionItem.ActionKind.Candidate);
    private void OnDisable()
    {
        if (_open)
        {
            UIPopUpManager.Instance?.Forget(panel);
            UIEventBus.NotifyClose("HandSlot");
            // 여기선 떨어뜨리지 않는다 — OnDisable 은 주로 씬 언로드 때 불려서, 사라지는 씬에 픽업을 만들게 된다.
        }
        _open = false;
        _source = null;
        _pending = null;
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
        // 보상 상자 모드에서 결정 없이 닫혔으면(ESC 등) 스킵과 같게 후보를 바닥에 떨어뜨려 보상이 사라지지 않게 한다.
        DropPendingIfUnresolved();
        _open = false;
        _source = null;
        _pending = null;
        _onComplete = null;
        CommonTooltipUI.Instance?.Hide();
        UIEventBus.NotifyClose("HandSlot");
    }
}

using UnityEngine;

/// <summary>소환수 습득/교체. 슬롯 2칸이 찼으면 현재 카드를 눌러 교체할 슬롯을 고른 뒤 후보와 비교한다.</summary>
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, "", "Assembly-CSharp", "HandSlotSelectionUI")]
public class MainMinionSelectionUI : Singleton<MainMinionSelectionUI>
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
    private int _targetSlot;
    public bool IsOpen => _open;
    public int TargetSlot => _targetSlot;

    private static bool SlotsFull => InventoryManager.Instance != null && InventoryManager.Instance.FindEmptySummonSlot() < 0;
    private bool CandidateBlocked => InventoryManager.Instance != null && Candidate != null && InventoryManager.Instance.HasSameFamilyEquipped(Candidate);
    private MainMinionDataSO TargetSummon => InventoryManager.Instance != null ? InventoryManager.Instance.GetSummonAt(_targetSlot) : null;
    public float ConfirmHoldSeconds => confirmHoldSeconds;
    public float HoldSeconds(MainMinionSelectionAction.ActionKind action)
        => action == MainMinionSelectionAction.ActionKind.Skip ? skipHoldSeconds : confirmHoldSeconds;

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
        var inventory = InventoryManager.Instance;
        int empty = inventory != null ? inventory.FindEmptySummonSlot() : -1;
        _targetSlot = empty >= 0 ? empty : (inventory != null ? inventory.ActiveSummonSlot : 0);
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
            Debug.LogWarning($"[MainMinionSelectionUI] 플레이어가 없어 '{_pending.name}' 을(를) 떨어뜨리지 못했습니다.");
            return;
        }
        GroundItem.Drop(_pending, player.transform.position);
    }

    public void RefreshSlots()
    {
        // 빈 칸이 있으면 _targetSlot 이 빈 칸이라 current 가 null → 예전처럼 단일 카드 습득 화면이다.
        var current = TargetSummon;
        var candidate = Candidate;
        bool blocked = CandidateBlocked;
        currentCard.gameObject.SetActive(current != null);
        if (current != null) currentCard.SetData(current.minionIcon, current.minionName,
            $"슬롯 {_targetSlot + 1} 교체 대상 (클릭하여 슬롯 변경)");
        candidateCard.SetData(candidate != null ? candidate.minionIcon : null,
            candidate != null ? candidate.minionName : "",
            blocked ? "같은 소환수를 이미 장착 중" : current != null ? "교체 후보" : "클릭하여 습득");
        var anchor = current != null ? candidateAnchor : singleCardAnchor;
        if (anchor != null) candidateCard.transform.position = anchor.position;
        comparisonTarget.SetActive(current != null);
        replaceButton.SetActive(current != null && !blocked);
        Hover(MainMinionSelectionAction.ActionKind.Compare, false);
    }

    public static string Describe(MinionDataSO data)
    {
        if (!(data is MainMinionDataSO main)) return data != null ? data.ResolveDescription() : "";
        var sb = new System.Text.StringBuilder();

        if (main.minionSkill != null)
        {
            string skillName = string.IsNullOrEmpty(main.minionSkill.skillName) ? main.minionSkill.name : main.minionSkill.skillName;
            sb.Append("[우클릭] ").Append(skillName).Append("\n").Append(main.minionSkill.description);
        }
        if (main.finisher != null && main.finisher.IsValid)
            sb.Append("\n\n[자동 공격]\n").Append(main.finisher.Describe());

        sb.Append("\n\n[Space 스왑] ");
        if (main.dashModifier != null && main.dashModifier.DealsDamage) sb.Append("대쉬 공격\n").Append(main.dashModifier.Describe());
        else sb.Append("스킬로 대체");
        return sb.ToString();
    }

    public void Hover(MainMinionSelectionAction.ActionKind kind, bool show)
    {
        var current = TargetSummon;
        var candidate = Candidate;
        bool left = show && current != null && (kind == MainMinionSelectionAction.ActionKind.Current || kind == MainMinionSelectionAction.ActionKind.Compare);
        bool right = show && candidate != null && (kind == MainMinionSelectionAction.ActionKind.Candidate || kind == MainMinionSelectionAction.ActionKind.Compare);
        currentDetails.gameObject.SetActive(left);
        candidateDetails.gameObject.SetActive(right);
        if (left) currentDetails.SetData(current.minionIcon, current.minionName, Describe(current));
        if (right) candidateDetails.SetData(candidate.minionIcon, candidate.minionName, Describe(candidate));
    }

    /// <summary>현재 카드 클릭: 슬롯 2칸이 찼을 때 교체할 슬롯을 바꾼다.</summary>
    public void ToggleTargetSlot()
    {
        if (!_open || !SlotsFull) return;
        _targetSlot = 1 - _targetSlot;
        RefreshSlots();
    }

    public void Select(MainMinionSelectionAction.ActionKind kind)
    {
        var candidate = Candidate;
        if (!_open || candidate == null || (_source != null && !_source.IsAvailable)) { Hide(); return; }
        if (kind == MainMinionSelectionAction.ActionKind.Skip)
        {
            DropPendingIfUnresolved(); // 보상 상자 모드: 스킵하는 순간 후보가 바닥에 떨어진다
            Complete();
            return;
        }
        var inventory = InventoryManager.Instance;
        if (inventory == null) return;
        if (CandidateBlocked) return; // 같은 계열은 교체로도 장착하지 않는다. 건너뛰기만 가능하다.
        var old = TargetSummon;
        if (kind == MainMinionSelectionAction.ActionKind.Candidate && old != null) return;
        if (kind != MainMinionSelectionAction.ActionKind.Candidate && kind != MainMinionSelectionAction.ActionKind.Replace) return;
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (player == null) return;
        var oldDrop = old != null ? GroundItem.Drop(old, player.transform.position) : null; // 교체: 기존 소환수가 바닥에 떨어진다
        if (old != null && oldDrop == null) return;
        if (!inventory.EquipMinion(_targetSlot, candidate))
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

    public void OnSlotSelected(int index) => Select(MainMinionSelectionAction.ActionKind.Candidate);
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

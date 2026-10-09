using UnityEngine;
using AstroNuts.Localization;

/// <summary>C는 왼쪽 스탯, 화살표는 장착 상세 정보. V와 동시에 열 수 있으며 시간을 멈추지 않는다.</summary>
public class SkillExplainUI : Singleton<SkillExplainUI>
{
    [Header("UI Panels")]
    [Tooltip("팝업으로 켜고 끌 대상. 비워두면 이 오브젝트 자신을 사용합니다.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private RectTransform statsPanel;
    [SerializeField] private RectTransform detailsPanel;
    [SerializeField] private TMPro.TextMeshProUGUI expandLabel;
    [SerializeField] private TMPro.TextMeshProUGUI summaryText;
    [SerializeField] private UnityEngine.UI.Image[] summaryIcons;

    [Header("왼쪽 패널: 장비")]
    [UnityEngine.Serialization.FormerlySerializedAs("playerSkillSlots")]
    [SerializeField] private SkillExplainSlotUI[] equipmentSlots = new SkillExplainSlotUI[1];
    [SerializeField] private SkillExplainSlotUI guardSlot;
    [SerializeField] private SkillExplainSlotUI minionHeader;
    [SerializeField] private TMPro.TextMeshProUGUI playerStatsText;

    [Header("오른쪽 패널: 메인 소환수 공격 (0=기본, 1=대쉬, 2=스킬)")]
    [SerializeField] private SkillExplainSlotUI[] linkedSkillSlots = new SkillExplainSlotUI[3];

    [Header("Keyword Tooltip")]
    [Tooltip("설명 속 '취약', '기절' 같은 키워드를 감지해 하이라이트 + 호버 툴팁을 붙입니다. 보상 카드와 동일한 사전을 연결하세요.")]
    [SerializeField] private KeywordDictionary keywordDictionary;

    private bool _isOpen = false;
    private float _nextStatsRefresh;

    public bool IsOpen => _isOpen;
    public bool IsExpanded => _isOpen && detailsPanel != null && detailsPanel.gameObject.activeSelf;

    protected override void OnAwake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        SetExpanded(false);
        panelRoot.SetActive(false);
    }

    public void Toggle()
    {
        SetOpen(!_isOpen);
    }

    // 전투 중에도 열 수 있는 정보창이다. 팝업 매니저의 Overlay 로 등록한다(시간 정지·입력 차단 없음).
    //   · 주머니와 동시에 열리며 상세 정보가 주머니보다 위에 표시된다.
    //   · 맵/옵션/대화가 떠 있으면 안 열린다. 보상 등 Modal 위에는 얹힌다.
    //   · ESC 로 닫히고, 보상창 등장 시 매니저가 닫는다 — 어느 경로든 OnPanelClosed 로 온다.
    public void SetOpen(bool open)
    {
        if (open == _isOpen) return;
        var mgr = UIPopUpManager.Instance;

        if (open)
        {
            RefreshUI();
            if (mgr != null) { if (!mgr.Open(panelRoot, UIPopUpManager.Layer.Overlay, OnPanelClosed,
                overlayPriority: 1, visualRoot: transform)) return; }
            else panelRoot.SetActive(true);
            _isOpen = true;
            SetExpanded(false);
            PouchUI.Instance?.EndDrag(null);
            CommonTooltipUI.Instance?.Hide();
            return;
        }

        if (mgr != null && mgr.IsOpen(panelRoot)) mgr.Close(panelRoot); // → OnPanelClosed
        else { panelRoot.SetActive(false); OnPanelClosed(); }
    }

    private void OnPanelClosed()
    {
        _isOpen = false;
        SetExpanded(false);
        CommonTooltipUI.Instance?.Hide();
    }

    private void OnDisable()
    {
        if (_isOpen) UIPopUpManager.Instance?.Forget(panelRoot);
        _isOpen = false;
        SetExpanded(false);
    }

    public void ToggleDetails()
    {
        if (!_isOpen) return;
        PouchUI.Instance?.EndDrag(null);
        CommonTooltipUI.Instance?.Hide();
        SetExpanded(!IsExpanded);
    }

    private void SetExpanded(bool expanded)
    {
        if (detailsPanel != null) detailsPanel.gameObject.SetActive(expanded);
        if (expandLabel != null) expandLabel.text = expanded ? "<" : ">";
    }

    public bool ContainsScreenPoint(Vector2 point, Camera camera)
        => _isOpen && ((statsPanel != null && statsPanel.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(statsPanel, point, camera))
            || (IsExpanded && detailsPanel.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(detailsPanel, point, camera)));

    private void Update()
    {
        if (!_isOpen) return;
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (player == null || player.Stat.Health.IsDead) SetOpen(false);
        else if (Time.unscaledTime >= _nextStatsRefresh) RefreshPlayerStats();
    }

    /// <summary>
    /// 현재 장비 / 메인 소환수 정보로 슬롯들을 갱신합니다.
    /// </summary>
    public void RefreshUI()
    {
        PlayerSkillController skillController = null;
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            skillController = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>();
        }

        RefreshEquipmentSlots();
        RefreshPlayerStats();
        RefreshLinkedSkillSlots(skillController);
        var guard = InventoryManager.Instance != null ? InventoryManager.Instance.EquippedRightClick : null;
        guardSlot?.SetData(guard != null ? guard.icon : null, guard != null ? guard.ResolveTitle() : "가드",
            guard != null ? guard.ResolveDescription() : "장착된 가드가 없습니다.");
        var main = InventoryManager.Instance != null ? InventoryManager.Instance.MainSummon : null;
        minionHeader?.SetData(main != null ? main.minionIcon : null,
            main != null ? main.minionName : "미니언 없음", "");
        var equipment = PlayerSkillInventoryManager.Instance != null ? PlayerSkillInventoryManager.Instance.EquippedEquipment?.baseData : null;
        SetSummaryIcon(0, equipment != null ? equipment.icon : null);
        SetSummaryIcon(1, guard != null ? guard.icon : null);
        SetSummaryIcon(2, main != null ? main.minionIcon : null);
        SetSummaryIcon(3, main != null && main.minionSkill != null ? main.minionSkill.icon : null);
        SetSummaryIcon(4, main != null ? main.dashModifier.uiIcon : null);
        if (summaryText != null) summaryText.text = $"플레이어 정보  [C]     소환수 {(main != null ? 1 : 0)}";
    }

    private void SetSummaryIcon(int index, Sprite sprite)
    {
        if (summaryIcons == null || index >= summaryIcons.Length || summaryIcons[index] == null) return;
        summaryIcons[index].sprite = sprite;
        summaryIcons[index].enabled = sprite != null;
    }

    private void RefreshEquipmentSlots()
    {
        foreach (var slot in equipmentSlots)
            if (slot != null) FillEquipmentSlot(slot);
    }

    private void RefreshPlayerStats()
    {
        _nextStatsRefresh = Time.unscaledTime + 0.2f;
        if (playerStatsText == null) return;
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        var stat = player != null ? player.Stat : null;
        if (stat == null) { playerStatsText.text = "플레이어 정보 준비 중"; return; }
        var guard = player.GetComponent<PlayerParryController>();
        var dodge = player.GetComponent<MeleeDodgeController>();
        playerStatsText.text = $"<line-height=19>체력<pos=112>{stat.CURHP:0}/{stat.MAXHP:0}\n" +
            $"가드<pos=112>{(guard != null ? guard.GuardAmount : 0):0}/{(guard != null ? guard.GuardCapacity : 0):0}\n" +
            $"회피 횟수<pos=112>{(dodge != null ? dodge.CurrentCharges : 0)}/{(dodge != null ? dodge.MaxCharges : 0)}\n\n" +
            $"물리 공격력<pos=112>{stat.ATK:0.##}\n마법 공격력<pos=112>{stat.MAGIC:0.##}\n" +
            $"방어력<pos=112>{stat.DEF:0.##}%\n회피율<pos=112>{stat.EVASION * 100f:0.##}%\n" +
            $"공격 속도<pos=112>{stat.ATKSPD:0.##}\n치명타 확률<pos=112>{stat.CRIT_CHANCE:0.##}%\n" +
            $"치명타 피해량<pos=112>{stat.CRIT_DAMAGE:0.##}%\n적중률<pos=112>{stat.ACCURACY * 100f:0.##}%\n" +
            $"기본 공격 배율<pos=112>{stat.BASIC_ATK_MULT:0.##}\n이동 속도<pos=112>{stat.MOVESPEED:0.##}\n" +
            $"스킬 쿨타임 감소<pos=112>{stat.SKILL_CDR * 100f:0.##}%\n대시 쿨타임 감소<pos=112>{stat.DASH_CDR * 100f:0.##}%\n" +
            $"물리 피해 증폭<pos=112>{stat.PHYS_AMP * 100f:0.##}%\n마법 피해 증폭<pos=112>{stat.MAGIC_AMP * 100f:0.##}%";
    }

    /// <summary>맨 하단 슬롯: 착용 중인 장비의 이름/효과/강화등급.</summary>
    private void FillEquipmentSlot(SkillExplainSlotUI slot)
    {
        var eq = PlayerSkillInventoryManager.Instance != null
            ? PlayerSkillInventoryManager.Instance.EquippedEquipment : null;

        if (eq == null || eq.baseData == null)
        {
            slot.SetData(null, "장비 없음", "장착된 장비가 없습니다.");
            return;
        }

        var so = eq.baseData;
        string title = string.IsNullOrEmpty(so.equipmentName) ? so.name : so.equipmentName;

        // 효과 = 장비 설명(디자이너가 적은 효과 문구) + 강화 등급.
        string enhance = $"강화 +{eq.enhanceLevel}/{so.maxEnhanceLevel}";
        string desc = string.IsNullOrEmpty(so.description) ? enhance : $"{so.description}\n\n{enhance}";

        slot.SetData(so.icon, title, ApplyKeywordHighlighting(desc));
    }

    private void RefreshLinkedSkillSlots(PlayerSkillController skillController)
    {
        // 오른쪽 패널 = 장착 중인 메인 소환수의 3가지 공격 설명.
        //   [0] 기본 공격(finisher)  [1] 대쉬 공격(dashModifier)  [2] Space 스킬(minionSkill)
        MainMinionDataSO main = skillController != null ? skillController.MainSummon : null;

        if (main == null)
        {
            foreach (var s in linkedSkillSlots) s?.SetEmpty();
            return;
        }

        FillLinkedSlot(0, main.finisher.uiIcon,     Fallback(main.finisher.uiTitle, "기본 공격"),     main.finisher.Describe());
        FillLinkedSlot(1, main.dashModifier.uiIcon, Fallback(main.dashModifier.uiTitle, "대쉬 공격"), main.dashModifier.Describe());

        var sk = main.minionSkill;
        FillLinkedSlot(2, sk != null ? sk.icon : null,
                          sk != null ? Fallback(sk.skillName, "스킬") : "스킬",
                          sk != null ? sk.description : null);
    }

    private void FillLinkedSlot(int i, Sprite icon, string title, string desc)
    {
        if (i < 0 || i >= linkedSkillSlots.Length || linkedSkillSlots[i] == null) return;
        linkedSkillSlots[i].SetData(icon, title, ApplyKeywordHighlighting(desc ?? ""));
    }

    private static string Fallback(string s, string def) => string.IsNullOrEmpty(s) ? def : s;

    /// <summary>
    /// 설명 문장 안에서 키워드 사전에 등록된 단어(예: "취약")를 찾아 색상 + 호버용 <link> 태그로 감싸니다.
    /// (보상 카드와 동일한 방식. 스프라이트 태그는 붙이지 않아 미설정 스프라이트로 인한 "?" 표시를 피합니다.)
    /// </summary>
    private string ApplyKeywordHighlighting(string text)
    {
        if (keywordDictionary == null || string.IsNullOrEmpty(text)) return text;

        foreach (var entry in keywordDictionary.entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id) || entry.displayName == null) continue;

            string keyword = entry.displayName.IsEmpty ? null : entry.displayName.GetLocalizedString();
            if (string.IsNullOrEmpty(keyword) || !text.Contains(keyword)) continue;

            string colorHex = ColorUtility.ToHtmlStringRGB(entry.badgeColor);

            // [추가] spriteName이 지정되어 있고, 해당 이름을 가진 스프라이트가 텍스트에 연결된 Sprite Asset 안에 있으면
            // 단어 앞에 아이콘이 함꿀 표시됩니다. 없거나 미설정이면 "?"가 나오니 주의.
            string spriteTag = string.IsNullOrEmpty(entry.spriteName)
                ? string.Empty
                : $"<sprite name=\"{entry.spriteName}\">";

            string wrapped = $"<u><color=#{colorHex}><link=\"{entry.id}\">{spriteTag}{keyword}</link></color></u>";
            text = text.Replace(keyword, wrapped);
        }

        return text;
    }

}

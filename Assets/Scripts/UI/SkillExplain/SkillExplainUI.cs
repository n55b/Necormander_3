using UnityEngine;
using AstroNuts.Localization;

/// <summary>
/// 내가 현재 장착 중인 플레이어 스킬(Q/E/R)과, 그에 연동되어 발동되는 연계(미니언) 스킬의
/// 설명을 볼 수 있는 UI. V키로 여닫으며(과거 GemTreeUI가 사용하던 키), UIPopUpManager를 통해
/// 다른 팝업들과 동일한 방식으로 관리됩니다.
/// </summary>
public class SkillExplainUI : Singleton<SkillExplainUI>
{
    [Header("UI Panels")]
    [Tooltip("팝업으로 켜고 끌 대상. 비워두면 이 오브젝트 자신을 사용합니다.")]
    [SerializeField] private GameObject panelRoot;

    [Header("Player Skill Slots (Q, E, R 순서)")]
    [SerializeField] private SkillExplainSlotUI[] playerSkillSlots = new SkillExplainSlotUI[3];

    [Header("Linked Skill Slots (Q, E, R에 연동된 미니언 스킬)")]
    [SerializeField] private SkillExplainSlotUI[] linkedSkillSlots = new SkillExplainSlotUI[3];

    [Header("Keyword Tooltip")]
    [Tooltip("설명 속 '취약', '기절' 같은 키워드를 감지해 하이라이트 + 호버 툴팁을 붙입니다. 보상 카드와 동일한 사전을 연결하세요.")]
    [SerializeField] private KeywordDictionary keywordDictionary;

    private bool _isOpen = false;
    private bool _openedAsOverlay = false; // 다른 팝업 위에 오버레이로 띄운 상태인지 여부

    public bool IsOpen => _isOpen;

    protected override void OnAwake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        panelRoot.SetActive(false);
    }

public void Toggle()
    {
        _isOpen = !_isOpen;

        if (_isOpen)
        {
            if (UIPopUpManager.Instance.IsOnBattle)
            {
                _isOpen = !_isOpen; // 전투 중에는 열지 않음
                return;
            }

            RefreshUI();

            if (UIPopUpManager.Instance.IsPopUpActive)
            {
                // 보상 선택 등 다른 팝업이 이미 떠 있는 상황.
                // 기존 팝업을 건드리지 않고(UIPopUpManager의 단일-팝업 독점을 거치지 않고)
                // 정보용 오버레이로만 위에 띄우고, 닫을 때도 같은 방식으로 직접 끈다.
                _openedAsOverlay = true;
                panelRoot.SetActive(true);
            }
            else
            {
                _openedAsOverlay = false;
                UIPopUpManager.Instance.PopUpUI(panelRoot);
            }
        }
        else
        {
            if (_openedAsOverlay)
            {
                // 다른 팝업(보상 선택 등)의 상택를 건드리지 않고 이 패널만 끄다.
                panelRoot.SetActive(false);
                _openedAsOverlay = false;
            }
            else
            {
                UIPopUpManager.Instance?.ClosePopUpUI();
            }
        }
    }

    /// <summary>
    /// 현재 장착된 플레이어 스킬 / 연계 스킬 정보로 슬롯들을 갱신합니다.
    /// </summary>
    public void RefreshUI()
    {
        PlayerSkillController skillController = null;
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            skillController = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>();
        }

        RefreshPlayerSkillSlots(skillController);
        RefreshLinkedSkillSlots(skillController);
    }

private void RefreshPlayerSkillSlots(PlayerSkillController skillController)
    {
        for (int i = 0; i < playerSkillSlots.Length; i++)
        {
            var slot = playerSkillSlots[i];
            if (slot == null) continue;

            PlayerSkillSO skill = skillController != null ? skillController.GetEquippedPlayerSkill(i) : null;

            if (skill != null)
            {
                // [스킬 설명] 이름 + 설명만 채워도 충분함 (설명 속 키워드는 자동으로 하이라이트/툴팁 처리)
                string description = ApplyKeywordHighlighting(skill.description);
                slot.SetData(skill.icon, skill.skillName, description);
            }
            else
            {
                slot.SetEmpty();
            }
        }
    }

private void RefreshLinkedSkillSlots(PlayerSkillController skillController)
    {
        for (int i = 0; i < linkedSkillSlots.Length; i++)
        {
            var slot = linkedSkillSlots[i];
            if (slot == null) continue;

            MinionDataSO minion = skillController != null ? skillController.GetEquippedMinion(i) : null;
            MinionSkillSO linkedSkill = minion != null ? minion.minionSkill : null;

            if (linkedSkill != null)
            {
                Sprite icon = linkedSkill.icon != null ? linkedSkill.icon : minion.minionIcon;
                string condition = GetKeywordDisplayName(linkedSkill.reactKeyword);
                string rawDescription = $"[발동 조건: {condition}]\n{linkedSkill.description}";
                string description = ApplyKeywordHighlighting(rawDescription);

                slot.SetData(icon, linkedSkill.skillName, description);
            }
            else
            {
                slot.SetEmpty();
            }
        }
    }

    /// <summary>
    /// 연계 스킬의 발동 조건(reactKeyword)을 사람이 읽을 수 있는 문구로 변환합니다.
    /// </summary>
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

private string GetKeywordDisplayName(SkillKeyword keyword)
    {
        switch (keyword)
        {
            case SkillKeyword.Vulnerability: return "취약 상태의 적 존재 시";
            case SkillKeyword.Debuff: return "디버프가 걸린 적 존재 시";
            case SkillKeyword.Stun: return "기절한 적 존재 시";
            case SkillKeyword.Strike: return "격파(취약) 판정 시";
            case SkillKeyword.Smash: return "강타(취약) 판정 시";
            default: return "조건 없음";
        }
    }
}

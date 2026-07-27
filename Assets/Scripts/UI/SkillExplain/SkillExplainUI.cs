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

    [Header("왼쪽 패널: 0=Q스킬, 1=E스킬, 맨아래=장착 장비")]
    [SerializeField] private SkillExplainSlotUI[] playerSkillSlots = new SkillExplainSlotUI[3];

    [Header("오른쪽 패널: 메인 소환수 공격 (0=기본, 1=대쉬, 2=스킬)")]
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
        // 슬롯 0..n-2 = Q/E 플레이어 스킬. 맨 마지막(하단) 슬롯 = 착용 장비.
        // (R 은 이제 소환수 전용이라 옛 R-스킬 칸이 비어 → 장비 칸으로 재활용)
        int equipmentSlot = playerSkillSlots.Length - 1;

        for (int i = 0; i < playerSkillSlots.Length; i++)
        {
            var slot = playerSkillSlots[i];
            if (slot == null) continue;

            if (i == equipmentSlot) { FillEquipmentSlot(slot); continue; }

            PlayerSkillSO skill = skillController != null ? skillController.GetEquippedPlayerSkill(i) : null;

            if (skill != null)
            {
                // [스킬 설명] 이름 + 설명만 채워도 충분함 (설명 속 키워드는 자동으로 하이라이트/툴팁 처리)
                slot.SetData(skill.icon, skill.skillName, ApplyKeywordHighlighting(skill.description));
            }
            else
            {
                slot.SetEmpty();
            }
        }
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
        //   [0] 기본 공격(평타 마무리 finisher)  [1] 대쉬 공격(dashModifier)  [2] 스킬(R minionSkill)
        MainMinionDataSO main = skillController != null ? skillController.MainSummon : null;

        if (main == null)
        {
            foreach (var s in linkedSkillSlots) s?.SetEmpty();
            return;
        }

        FillLinkedSlot(0, main.finisher.uiIcon,     Fallback(main.finisher.uiTitle, "기본 공격"),     main.finisher.uiDescription);
        FillLinkedSlot(1, main.dashModifier.uiIcon, Fallback(main.dashModifier.uiTitle, "대쉬 공격"), main.dashModifier.uiDescription);

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

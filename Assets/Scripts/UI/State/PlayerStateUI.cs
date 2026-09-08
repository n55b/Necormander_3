using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// PlayerState UI - 플레이어 체력, 골드, 부활 타이머 표시.
/// Space 미니언 / 우클릭 가드 아이콘 + 쿨타임 Fill 오버레이 표시.
/// </summary>
public class PlayerStateUI : MonoBehaviour
{
    /// <summary>
    /// 스킬 슬롯 1개의 UI 요소.
    /// - SkillIcon    : 스킬 아이콘 Image (Sprite 교체)
    /// - CooldownFill : 쿨타임 마스크 Image (별도 오버레이 오브젝트 권장)
    ///                  null 이면 SkillIcon 자체에 Filled 타입 적용
    /// - CooldownText : 남은 초 텍스트 (선택)
    /// - ArrowImage   : 스킬 교체 UI
    /// - SkillChangeButton : 스킬 교체 버튼
    /// </summary>
    [System.Serializable]
    public class SkillSlotUI
    {
        [Tooltip("슬롯 루트 오브젝트")]
        public GameObject SlotRoot;
        [Tooltip("스킬 아이콘 Image")]
        public Image SkillIcon;
        [Tooltip("쿨타임 Fill Image (null이면 SkillIcon에 Filled 타입 적용)")]
        public Image CooldownFill;
        [Tooltip("쿨타임 남은 초 텍스트 (선택)")]
        public TextMeshProUGUI CooldownText;
        [Tooltip("스킬 교체 UI")]
        public GameObject ArrowImage;
        public Button SkillChangeButton;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Inspector 필드
    // ─────────────────────────────────────────────────────────────────────
    [Header("Panel Parent")]
    [SerializeField] private GameObject panelParent;

    [Header("HP Settings")]
    [SerializeField] private Image hpSprite;
    [Tooltip("보호막 게이지. 체력바와 같은 자리에 '뒤쪽'으로 깔린다 — 앞의 체력바가 왼쪽을 덮으므로 " +
             "체력이 끝나는 지점부터 보호막 색이 이어져 보인다.\n" +
             "보호막이 최대 체력을 넘기면 바의 눈금 자체가 늘어나 빨간 부분이 비율상 줄어든다(롤 방식).\n" +
             "무채색 스프라이트(UI_HpbarWhite)라 Image 의 Color 로 원하는 색을 낼 수 있다.")]
    [SerializeField] private Image shieldSprite;
    [SerializeField] private TextMeshProUGUI hpText;

    [Header("Gold Settings")]
    [SerializeField] private TextMeshProUGUI goldText;

    [Header("Stamina Settings")]
    [SerializeField] private GameObject staminaUIPrefab;

    [Header("대쉬 / Space 미니언 스킬")]
    [SerializeField] private DashCooldownUI dashCooldownUI;
    [UnityEngine.Serialization.FormerlySerializedAs("skillSlotQ")]
    [SerializeField] private SkillSlotUI guardSlot;
    [UnityEngine.Serialization.FormerlySerializedAs("skillSlotR")]
    [SerializeField] private SkillSlotUI minionSkillSlot;

    // ─────────────────────────────────────────────────────────────────────
    // 런타임
    // ─────────────────────────────────────────────────────────────────────
    private CharacterHealth       _playerHealth;
    private CharacterStatus       _playerStatus;   // 보호막 잔량 소스
    private PlayerSkillController _skillCtrl;
    private PlayerParryController _guardCtrl;
    private List<Image>           _hpFillImages  = new List<Image>();
    private SkillSlotUI[]         _skillSlots;
    private int _lastGold = int.MinValue; // dirty 비교용



    // ─────────────────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────────────────
    public void Initialize(CharacterHealth playerHealth)
    {
        _playerHealth = playerHealth;
        _skillSlots   = new SkillSlotUI[] { guardSlot, minionSkillSlot };

        // 보호막은 CharacterHealth 가 아니라 같은 오브젝트의 CharacterStatus 가 들고 있다.
        _playerStatus = _playerHealth != null ? _playerHealth.GetComponent<CharacterStatus>() : null;

        if (_playerHealth != null) { _playerHealth.UpdateHPBar += RefreshHP; RefreshHP(); }

        RefreshGold();

        // PlayerSkillController 연결
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
            _skillCtrl = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>();

        // 인벤토리 변경될 때마다 스킬 아이콘 자동 갱신
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated += RefreshSkillIcons;


        // Fill 초기 비활성화
        foreach (var s in _skillSlots)
        {
            if (s == null) continue;
            if (s.CooldownFill != null) { s.CooldownFill.fillAmount = 0f; s.CooldownFill.gameObject.SetActive(false); }
            if (s.CooldownText != null) s.CooldownText.text = "";
        }

        // 스타미나 UI
        // StaminaUI staminaUI = GetComponentInChildren<StaminaUI>();
        // if (staminaUI == null)
        // {
        //     if (staminaUIPrefab != null)
        //     {
        //         GameObject obj = Instantiate(staminaUIPrefab, panelParent != null ? panelParent.transform : transform);
        //         staminaUI = obj.GetComponent<StaminaUI>() ?? obj.AddComponent<StaminaUI>();
        //     }
        //     else staminaUI = gameObject.AddComponent<StaminaUI>();
        // }
        // if (staminaUI != null && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        //     staminaUI.Initialize(GameManager.Instance.PLAYERCONTROLLER.STAMINA);

        // Dash 쿨타임 UI
        if (dashCooldownUI != null)
        {
            if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
                dashCooldownUI.Initialize(GameManager.Instance.PLAYERCONTROLLER);
        }

        RefreshSkillIcons();
        Debug.Log("<color=green>[PlayerStateUI]</color> HUD Initialized.");
    }

    #region UI State Management
    public void PopUpStateUI() { }
    public void CloseStateUI() { }
    #endregion

    private void OnDestroy()
    {
        if (_playerHealth != null) _playerHealth.UpdateHPBar -= RefreshHP;
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated -= RefreshSkillIcons;

    }

    private void Update()
    {
        RefreshGold();
        RefreshBars();   // 보호막은 피해/회복 이벤트 없이도 변한다 → 폴링
        UpdateSkillCooldowns();
    }

    // ─────────────────────────────────────────────────────────────────────
    // HP
    // ─────────────────────────────────────────────────────────────────────
    #region HP
    /// <summary>피해/회복 이벤트용. 바 두 개는 공용 함수가 그리고, 여기선 숫자 텍스트만 추가로 갱신한다.</summary>
    public void RefreshHP()
    {
        if (_playerHealth == null) return;
        RefreshBars();
        if (hpText != null) hpText.text = $"{(int)_playerHealth.CurHP} / {(int)_playerHealth.MaxHP}";
    }

    /// <summary>
    /// 체력바 + 보호막바를 함께 그린다. 둘을 한 함수에 묶은 이유는 <b>서로의 눈금을 공유</b>하기 때문이다.
    ///
    /// [롤 방식 — 바 길이는 고정, 눈금이 늘어난다]
    /// 바 전체가 나타내는 양은 최대 체력이 아니라 max(최대 체력, 현재 체력 + 보호막) 이다.
    /// 보호막이 최대 체력을 밀어내면 눈금이 그만큼 늘어나므로, 빨간 부분이 비율상 줄어들고 그 자리를
    /// 흰색이 차지한다. 보호막이 사라지면 눈금이 원래대로 줄어 빨간 바가 다시 꽉 찬다.
    /// (바가 배경 프레임 밖으로 삐져나가는 게 아니다 — 전체 길이는 그대로다.)
    ///
    /// 매 프레임 도는 이유: 보호막은 '피해/회복' 이벤트 없이도 변한다(가드 성공으로 생기고 5초 뒤 만료).
    /// UpdateHPBar 에만 묶으면 그 순간들을 통째로 놓친다. 텍스트만 이벤트 쪽에 남겨 매 프레임
    /// 문자열을 새로 만들지 않게 했다.
    /// </summary>
    private void RefreshBars()
    {
        if (hpSprite == null || _playerHealth == null || _playerHealth.MaxHP <= 0f) return;

        float shield = _playerStatus != null ? _playerStatus.TotalShield : 0f;
        float denom = Mathf.Max(_playerHealth.MaxHP, _playerHealth.CurHP + shield);

        SetFill(hpSprite, _playerHealth.CurHP / denom);

        if (shieldSprite == null) return;
        // 보호막이 없으면 두 바의 폭이 같아져 가장자리가 1픽셀 삐져나올 수 있다. 아예 끈다.
        bool show = shield > 0.01f;
        if (shieldSprite.enabled != show) shieldSprite.enabled = show;
        // 보호막바는 '보호막만큼'이 아니라 '체력+보호막'까지 채운다. 체력바 뒤에 깔려 있어서
        // 앞의 빨간 바가 왼쪽을 가리고 그 오른쪽만 흰색으로 보인다 = 오프셋 계산이 필요 없다.
        if (show) SetFill(shieldSprite, (_playerHealth.CurHP + shield) / denom);
    }

    private static void SetFill(Image img, float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Abs(img.fillAmount - value) > 0.0005f) img.fillAmount = value;
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────
    // Gold
    // ─────────────────────────────────────────────────────────────────────
    #region Gold
    public void RefreshGold()
    {
        if (InventoryManager.Instance == null || goldText == null) return;
        int gold = InventoryManager.Instance.GOLD;
        if (gold == _lastGold) return;
        _lastGold = gold;
        goldText.SetText("{0}", gold);
    }
    #endregion


    // ─────────────────────────────────────────────────────────────────────
    // Space 미니언 아이콘 + 쿨타임
    // ─────────────────────────────────────────────────────────────────────
    #region Skill Cooldowns

    /// <summary>
    /// 스킬 아이콘을 갱신합니다.
    /// 아이콘 우선순위: playerSkill.icon → minionIcon
    /// InventoryManager.OnMinionUpdated에 등록되어 인벤토리 변경 시 자동 호출됩니다.
    /// </summary>
    public void RefreshSkillIcons()
    {
        if (_skillCtrl == null && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
            _skillCtrl = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>();
        if (minionSkillSlot != null) RefreshMinionSlot(minionSkillSlot);
        if (_skillCtrl != null) _guardCtrl = _skillCtrl.GetComponent<PlayerParryController>();
        var rc = InventoryManager.Instance != null ? InventoryManager.Instance.EquippedRightClick : null;
        if (guardSlot != null && guardSlot.SkillIcon != null && rc != null)
        {
            guardSlot.SkillIcon.sprite = rc.ResolveIcon();
            guardSlot.SkillIcon.color = Color.white;
            var tip = guardSlot.SkillIcon.GetComponent<SkillTooltipTrigger>();
            if (tip == null) tip = guardSlot.SkillIcon.gameObject.AddComponent<SkillTooltipTrigger>();
            tip.SetData(rc.ResolveTitle(), rc.ResolveDescription());
        }
    }

    private void UpdateSkillCooldowns()
    {
        if (_skillCtrl != null && minionSkillSlot != null) UpdateMinionCooldown(minionSkillSlot);
        if (_guardCtrl != null && guardSlot != null)
        {
            float remaining = _guardCtrl.CooldownRemaining;
            var rc = InventoryManager.Instance != null ? InventoryManager.Instance.EquippedRightClick : null;
            float max = rc != null ? rc.config.cooldownDuration : 3f;
            if (guardSlot.CooldownFill != null)
            {
                guardSlot.CooldownFill.gameObject.SetActive(remaining > 0f);
                guardSlot.CooldownFill.fillAmount = max > 0f ? Mathf.Clamp01(remaining / max) : 0f;
            }
            if (guardSlot.CooldownText != null)
                guardSlot.CooldownText.text = remaining > 0f ? remaining.ToString("0.0") : "";
        }
    }

    private MainMinionDataSO EquippedMainSummon =>
        InventoryManager.Instance != null ? InventoryManager.Instance.MainSummon : null;

    private void RefreshMinionSlot(SkillSlotUI slot)
    {
        MainMinionDataSO main = EquippedMainSummon;
        bool has = main != null;

        if (slot.SkillIcon != null)
        {
            if (has)
            {
                slot.SkillIcon.sprite = main.ResolveIcon(); // MainMinionDataSO 는 소환수 스킬 아이콘을 반환
                slot.SkillIcon.color  = Color.white;
                slot.SkillIcon.type   = Image.Type.Simple;

                var tooltip = slot.SkillIcon.GetComponent<SkillTooltipTrigger>();
                if (tooltip == null) tooltip = slot.SkillIcon.gameObject.AddComponent<SkillTooltipTrigger>();
                string desc = main.ResolveDescription();
                if (!string.IsNullOrEmpty(desc)) tooltip.SetData(main.ResolveTitle(), desc);
                else tooltip.Clear();
            }
            else
            {
                slot.SkillIcon.sprite = null;
                var tooltipEmpty = slot.SkillIcon.GetComponent<SkillTooltipTrigger>();
                if (tooltipEmpty != null) tooltipEmpty.Clear();
                slot.SkillIcon.color = new Color(1f, 1f, 1f, 0.2f);
            }
        }

        if (slot.CooldownFill != null) { slot.CooldownFill.fillAmount = 0f; slot.CooldownFill.gameObject.SetActive(false); }
        if (slot.CooldownText != null) slot.CooldownText.text = "";
    }

    private void UpdateMinionCooldown(SkillSlotUI slot)
    {
        var main = EquippedMainSummon;
        if (main == null || main.minionSkill == null) return;

        float maxCd     = main.minionSkill.cooldownTime;
        float remaining = _skillCtrl.GetMainSummonCooldownRemaining();
        bool  onCd      = remaining > 0.05f;
        float fill      = (maxCd > 0f && onCd) ? Mathf.Clamp01(remaining / maxCd) : 0f;

        if (slot.CooldownFill != null)
        {
            bool isActive = slot.CooldownFill.gameObject.activeSelf;
            if (isActive != onCd) slot.CooldownFill.gameObject.SetActive(onCd);
            if (onCd) slot.CooldownFill.fillAmount = fill;
        }
        else if (slot.SkillIcon != null)
        {
            slot.SkillIcon.type          = Image.Type.Filled;
            slot.SkillIcon.fillMethod    = Image.FillMethod.Radial360;
            slot.SkillIcon.fillClockwise = false;
            slot.SkillIcon.fillAmount    = onCd ? fill : 1f;
        }

        if (slot.CooldownText != null)
        {
            if (onCd)
                slot.CooldownText.SetText("{0:1}", remaining);
            else if (slot.CooldownText.text.Length > 0)
                slot.CooldownText.text = "";
        }
    }
    #endregion

}

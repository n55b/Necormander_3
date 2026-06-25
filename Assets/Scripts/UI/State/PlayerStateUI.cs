using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// PlayerState UI - 플레이어 체력, 골드, 부활 타이머 표시.
/// Q/E/R 플레이어 스킬 아이콘 + 쿨타임 Fill 오버레이 표시.
/// </summary>
public class PlayerStateUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────
    // 내부 클래스: 부활 타이머 아이콘
    // ─────────────────────────────────────────────────────────────────────
    private class ReviveIcon
    {
        public AllyManager.MinionInfo TargetInfo;
        public GameObject IconObject;
        public TextMeshProUGUI TimerText;
        public Image ArmyImage;

        public ReviveIcon(AllyManager.MinionInfo info, GameObject obj)
        {
            TargetInfo = info;
            IconObject = obj;
            TimerText  = obj.GetComponentInChildren<TextMeshProUGUI>();
            ArmyImage  = obj.GetComponentInChildren<Image>();
            ArmyImage.sprite = info.Data.minionIcon;
        }
    }

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
    [SerializeField] private TextMeshProUGUI hpText;

    [Header("Gold Settings")]
    [SerializeField] private TextMeshProUGUI goldText;

    [Header("Stamina Settings")]
    [SerializeField] private GameObject staminaUIPrefab;

    [Header("Dash / Q / E / R 플레이어 스킬 슬롯")]
    [SerializeField] private DashCooldownUI dashCooldownUI;
    [SerializeField] private SkillSlotUI skillSlotQ;
    [SerializeField] private SkillSlotUI skillSlotE;
    [SerializeField] private SkillSlotUI skillSlotR;

    // ─────────────────────────────────────────────────────────────────────
    // 런타임
    // ─────────────────────────────────────────────────────────────────────
    private CharacterHealth       _playerHealth;
    private AllyManager           _allyManager;
    private PlayerSkillController _skillCtrl;
    private List<Image>           _hpFillImages  = new List<Image>();
    private List<ReviveIcon>      _revivingIcons = new List<ReviveIcon>();
    private SkillSlotUI[]         _skillSlots;
    private int _lastGold = int.MinValue; // dirty 비교용


    // ─────────────────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────────────────
    public void Initialize(CharacterHealth playerHealth, AllyManager allyManager)
    {
        _playerHealth = playerHealth;
        _allyManager  = allyManager;
        _skillSlots   = new SkillSlotUI[] { skillSlotQ, skillSlotE, skillSlotR };

        if (_playerHealth != null) { _playerHealth.UpdateHPBar += RefreshHP; RefreshHP(); }
        if (_allyManager  != null) { _allyManager.OnAllyRespawnStart += AddReviveIcon; _allyManager.OnAllyRespawned += RemoveReviveIcon; }

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

        Debug.Log("<color=green>[PlayerStateUI]</color> HUD Initialized.");
    }

    #region UI State Management
    public void PopUpStateUI() { ClearReviveIcons(); }
    public void CloseStateUI() { }
    #endregion

    private void OnDestroy()
    {
        if (_playerHealth != null) _playerHealth.UpdateHPBar -= RefreshHP;
        if (_allyManager  != null)
        {
            _allyManager.OnAllyRespawnStart -= AddReviveIcon;
            _allyManager.OnAllyRespawned    -= RemoveReviveIcon;
        }
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated -= RefreshSkillIcons;
    }

    private void Update()
    {
        RefreshGold();
        UpdateReviveTimers();
        UpdateSkillCooldowns();
    }

    // ─────────────────────────────────────────────────────────────────────
    // HP
    // ─────────────────────────────────────────────────────────────────────
    #region HP
    public void RefreshHP()
    {
        if (hpSprite == null || _playerHealth == null) return;
        float ratio = _playerHealth.MaxHP > 0f ? _playerHealth.CurHP / _playerHealth.MaxHP : 0f;
        hpSprite.fillAmount = ratio;
        if (hpText != null) hpText.text = $"{(int)_playerHealth.CurHP} / {(int)_playerHealth.MaxHP}";
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
    // Revive
    // ─────────────────────────────────────────────────────────────────────
    #region Revive
    private void AddReviveIcon(AllyManager.MinionInfo info) { }

    private void RemoveReviveIcon(AllyManager.MinionInfo info)
    {
        var icon = _revivingIcons.Find(r => r.TargetInfo == info);
        if (icon != null) { Destroy(icon.IconObject); _revivingIcons.Remove(icon); }
    }

    private void ClearReviveIcons()
    {
        foreach (var icon in _revivingIcons) Destroy(icon.IconObject);
        _revivingIcons.Clear();
    }

    private void UpdateReviveTimers()
    {
        foreach (var icon in _revivingIcons)
            if (icon.TargetInfo != null && icon.TimerText != null)
                icon.TimerText.text = icon.TargetInfo.RespawnTimer.ToString("F1");
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────
    // Q / E / R 스킬 아이콘 + 쿨타임
    // ─────────────────────────────────────────────────────────────────────
    #region Skill Cooldowns

    /// <summary>
    /// 스킬 아이콘을 갱신합니다.
    /// 아이콘 우선순위: playerSkill.icon → minionIcon
    /// InventoryManager.OnMinionUpdated에 등록되어 인벤토리 변경 시 자동 호출됩니다.
    /// </summary>
    public void RefreshSkillIcons()
    {
        // _skillCtrl이 아직 null이면 재취득 시도
        if (_skillCtrl == null && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
            _skillCtrl = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>();

        if (_skillCtrl == null || _skillSlots == null) return;

        for (int i = 0; i < _skillSlots.Length; i++)
        {
            var slot = _skillSlots[i];
            if (slot == null) continue;

            MinionDataSO data = _skillCtrl.GetEquippedMinion(i);
            var pSkill = _skillCtrl.GetEquippedPlayerSkill(i);
            bool has = pSkill != null;

            if (slot.SkillIcon != null)
            {
                if (has)
                {
                    // playerSkill.icon이 있으면 우선 사용, 없으면 미니언 아이콘으로 대체
                    slot.SkillIcon.sprite = (pSkill.icon != null)
                        ? pSkill.icon
                        : (data != null ? data.minionIcon : null);
                    slot.SkillIcon.color = Color.white;
                    slot.SkillIcon.type  = Image.Type.Simple;

                    var tooltip = slot.SkillIcon.GetComponent<SkillTooltipTrigger>();
                    if (tooltip == null) tooltip = slot.SkillIcon.gameObject.AddComponent<SkillTooltipTrigger>();
                    tooltip.SetData(pSkill.skillName, pSkill.description);
                }
                else
                {
                    slot.SkillIcon.sprite = null;

                    var tooltipEmpty = slot.SkillIcon.GetComponent<SkillTooltipTrigger>();
                    if (tooltipEmpty != null) tooltipEmpty.Clear();
                    slot.SkillIcon.color  = new Color(1f, 1f, 1f, 0.2f);
                }
            }

            // 아이콘 바뀌면 Fill도 리셋
            if (slot.CooldownFill != null) { slot.CooldownFill.fillAmount = 0f; slot.CooldownFill.gameObject.SetActive(false); }
            if (slot.CooldownText != null) slot.CooldownText.text = "";
        }
    }

    private void UpdateSkillCooldowns()
    {
        if (_skillCtrl == null || _skillSlots == null) return;

        for (int i = 0; i < _skillSlots.Length; i++)
        {
            var slot = _skillSlots[i];
            if (slot == null) continue;

            var pSkill = _skillCtrl.GetEquippedPlayerSkill(i);
            if (pSkill == null) continue;

            float maxCd     = pSkill.cooldownTime;
            float remaining = _skillCtrl.GetPlayerSkillCooldownRemaining((PlayerSkillController.SkillSlot)i);
            bool  onCd      = remaining > 0.05f;
            float fill      = (maxCd > 0f && onCd) ? Mathf.Clamp01(remaining / maxCd) : 0f;

            if (slot.CooldownFill != null)
            {
                // SetActive는 값이 달라질 때만 호출
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
    }
    #endregion

    #region Skill Change

    /// <summary>
    /// 스킬 변경에 필요한 매서드
    /// </summary>
    public void OpenChangeSkillUI()
    {
        foreach(var slot in _skillSlots)
        {
            slot.ArrowImage.SetActive(true);
            slot.SkillChangeButton.enabled = true;
        }
    }
    public void CloseChangeSkillUI()
    {
        foreach(var slot in _skillSlots)
        {
            slot.ArrowImage.SetActive(false);
            slot.SkillChangeButton.enabled = false;
        }
    }
    #endregion
}

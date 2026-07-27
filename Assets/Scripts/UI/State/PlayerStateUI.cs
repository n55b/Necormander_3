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
    private PlayerSkillController _skillCtrl;
    private List<Image>           _hpFillImages  = new List<Image>();
    private SkillSlotUI[]         _skillSlots;
    private int _lastGold = int.MinValue; // dirty 비교용
    private PlayerSkillSO _pendingSkill; // 보상으로 받아서 장착 슬롯 선택을 기다리는 중인 스킬


    // ─────────────────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────────────────
    public void Initialize(CharacterHealth playerHealth)
    {
        _playerHealth = playerHealth;
        _skillSlots   = new SkillSlotUI[] { skillSlotQ, skillSlotE, skillSlotR };

        // 각 슬롯의 "스킬 바꾸기" 버튼을 실제 장착 동작에 연결.
        // R 슬롯은 소환수 액티브 전용이라 플레이어 스킬 교체 대상이 아니다 — 건너뛴다.
        for (int i = 0; i < _skillSlots.Length; i++)
        {
            if (i == (int)PlayerSkillController.SkillSlot.R) continue;
            int slotIndex = i; // 클로저 캡처용 로컬 변수
            var slot = _skillSlots[i];
            if (slot == null || slot.SkillChangeButton == null) continue;

            // 중복 등록 방지를 위해 기존 리스너를 먼저 제거
            slot.SkillChangeButton.onClick.RemoveAllListeners();
            slot.SkillChangeButton.onClick.AddListener(() => OnSkillSlotChangeClicked(slotIndex));
        }

        if (_playerHealth != null) { _playerHealth.UpdateHPBar += RefreshHP; RefreshHP(); }

        RefreshGold();

        // PlayerSkillController 연결
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
            _skillCtrl = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>();

        // 인벤토리 변경될 때마다 스킬 아이콘 자동 갱신
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated += RefreshSkillIcons;
        if (PlayerSkillInventoryManager.Instance != null)
            PlayerSkillInventoryManager.Instance.OnPlayerSkillUpdated += RefreshSkillIcons;


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
    public void PopUpStateUI() { }
    public void CloseStateUI() { }
    #endregion

    private void OnDestroy()
    {
        if (_playerHealth != null) _playerHealth.UpdateHPBar -= RefreshHP;
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated -= RefreshSkillIcons;
        if (PlayerSkillInventoryManager.Instance != null)
            PlayerSkillInventoryManager.Instance.OnPlayerSkillUpdated -= RefreshSkillIcons;

    }

    private void Update()
    {
        RefreshGold();
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

            // R 슬롯은 플레이어 스킬이 아니라 메인 소환수 액티브(구 스페이스바)를 표시한다.
            if (i == (int)PlayerSkillController.SkillSlot.R) { RefreshMinionSlot(slot); continue; }

            MinionDataSO data = _skillCtrl.GetEquippedMinion(i);
                        // 원본(PlayerSkillInventoryManager)을 직접 읽는다. _skillCtrl 캐시를 읽으면
            // 같은 OnPlayerSkillUpdated 를 듣는 두 핸들러(여기와 PlayerSkillController.SyncPlayerSkillsFromInventory)의
            // 호출 순서에 결과가 갈린다 — 우리가 먼저 돌면 아직 동기화 안 된 한 박자 전 값을 그리게 된다.
            // (장비 착용은 Equip(0)/Equip(1) 로 이벤트를 두 번 쓰므로, 마지막인 E 가 항상 빈 채로 그려졌다.)
            // 아래 R 슬롯(소환수)이 InventoryManager 원본을 읽는 것과 같은 이유다.
            var pSkill = PlayerSkillInventoryManager.Instance != null
                ? PlayerSkillInventoryManager.Instance.GetEquipped(i)
                : _skillCtrl.GetEquippedPlayerSkill(i);
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

            // R 슬롯은 메인 소환수 액티브의 쿨타임을 표시한다. 아이콘 자체는 RefreshSkillIcons(이벤트)가
            // 맞추고, 여기선 쿨타임 카운트다운만 매 프레임 갱신한다(Q/E 와 동일).
            if (i == (int)PlayerSkillController.SkillSlot.R) { UpdateMinionCooldown(slot); continue; }
            // RefreshSkillIcons 와 같은 이유로 원본을 직접 읽는다(캐시는 이벤트 순서에 한 박자 밀릴 수 있음).
            // 쿨타임 잔여시간은 타이머를 소유한 컨트롤러에서 그대로 가져온다.
            var pSkill = PlayerSkillInventoryManager.Instance != null
                ? PlayerSkillInventoryManager.Instance.GetEquipped(i)
                : _skillCtrl.GetEquippedPlayerSkill(i);
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

    // ── R 슬롯 = 메인 소환수 액티브 ─────────────────────────────────────
    // R 은 플레이어 스킬이 아니라 소환수 스킬(구 스페이스바)이다. 장착 경로도(InventoryManager),
    // 쿨타임 소스도(GetMainSummonCooldownRemaining) 플레이어 스킬과 다르므로 별도로 그린다.
    //
    // 소환수는 PlayerSkillController 의 캐시가 아니라 원본(InventoryManager)에서 직접 읽는다.
    // 캐시를 읽으면, 같은 OnMinionUpdated 이벤트를 받는 두 핸들러(여기 RefreshSkillIcons 와
    // PlayerSkillController.SyncWithInventory)의 실행 순서에 결과가 갈린다 — 우리가 먼저 돌면 아직
    // 동기화 안 된 옛 값을 그린다. InventoryManager 는 슬롯을 채운 뒤 이벤트를 쏘므로(EquipMinion),
    // 원본을 읽으면 순서와 무관하게 항상 최신이다. 그래서 매 프레임 폴링이 필요 없다.
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

    #region Skill Change

    /// <summary>
    /// 스킬 변경에 필요한 매서드
    /// </summary>
    public void OpenChangeSkillUI(PlayerSkillSO pendingSkill)
    {
        _pendingSkill = pendingSkill;

        // R 슬롯은 소환수 전용이라 플레이어 스킬 교체 대상에서 제외한다.
        for (int i = 0; i < _skillSlots.Length; i++)
        {
            if (i == (int)PlayerSkillController.SkillSlot.R) continue;
            var slot = _skillSlots[i];
            if (slot == null) continue;
            if (slot.ArrowImage != null) slot.ArrowImage.SetActive(true);
            if (slot.SkillChangeButton != null) { slot.SkillChangeButton.enabled = true; slot.SkillChangeButton.interactable = true; }
        }

        // Stop time so the player can't act while picking a slot.
        if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(true);
    }
    public void CloseChangeSkillUI()
    {
        foreach(var slot in _skillSlots)
        {
            if (slot == null) continue;
            if (slot.ArrowImage != null) slot.ArrowImage.SetActive(false);
            if (slot.SkillChangeButton != null) { slot.SkillChangeButton.enabled = false; slot.SkillChangeButton.interactable = false; }
        }

        // Resume time once a slot has been chosen (or the UI is closed).
        if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(false);
    }

    /// <summary>
    /// 슬롯의 "스킬 바꾸기" 버튼을 눌렀을 때, 대기 중이던 스킬을 그 슬롯에 장착하고 UI를 닫습니다.
    /// </summary>
    private void OnSkillSlotChangeClicked(int slotIndex)
    {
        if (_pendingSkill == null) return;

        PlayerSkillInventoryManager.Instance?.Equip(slotIndex, _pendingSkill);
        RefreshSkillIcons(); // Event 타이밍에 의존하지 않고 장착 즉시 UI를 강제로 갱신
        _pendingSkill = null;

        CloseChangeSkillUI();

        // Reward 재개
        RewardManager.Instance?.NotifyHandSlotSelectionComplete();
    }
    #endregion
}

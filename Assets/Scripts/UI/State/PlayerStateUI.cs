using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// PlayerState UI - 플레이어 체력, 골드, 부활 타이머 표시.
/// 소환수 스킬 칸 2개(활성 표시·각 쿨타임)와 스왑 쿨타임 표시.
/// </summary>
public class PlayerStateUI : MonoBehaviour
{
    [Header("기획 조절 · 동작과 타이밍")]
    [SerializeField, Range(0f, 1f), Tooltip("사용할 수 없는 스킬 아이콘의 불투명도입니다. 0이면 숨기고 1이면 원래 밝기로 표시합니다.")]
    private float unavailableSkillAlpha = 0.2f;

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
    [Header("패널 부모")]
    [SerializeField] private GameObject panelParent;

    [Header("체력 설정")]
    [SerializeField] private Image hpSprite;
    [Tooltip("보호막 게이지. 체력바와 같은 자리에 '뒤쪽'으로 깔린다 — 앞의 체력바가 왼쪽을 덮으므로 " +
             "체력이 끝나는 지점부터 보호막 색이 이어져 보인다.\n" +
             "보호막이 최대 체력을 넘기면 바의 눈금 자체가 늘어나 빨간 부분이 비율상 줄어든다(롤 방식).\n" +
             "무채색 스프라이트(UI_HpbarWhite)라 Image 의 Color 로 원하는 색을 낼 수 있다.")]
    [SerializeField] private Image shieldSprite;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField, Min(.01f), Tooltip("회복 구슬 도착 후 체력바와 숫자가 회복량만큼 차오르는 시간(초)입니다. 실제 체력은 회복 순간 즉시 적용됩니다.")]
    private float healFillDuration = .24f;
    [SerializeField, Tooltip("가로는 구슬 도착 후 시간, 세로는 체력바 회복 진행률(0~1)입니다.")]
    private AnimationCurve healFillCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("스왑 쿨타임 바 (HUD 프리팹에 배치)")]
    [UnityEngine.Serialization.FormerlySerializedAs("guardGaugeFill")]
    [SerializeField] private Image swapCooldownFill;
    [UnityEngine.Serialization.FormerlySerializedAs("guardGaugeText")]
    [SerializeField] private TextMeshProUGUI swapCooldownText;
    [UnityEngine.Serialization.FormerlySerializedAs("guardReadyColor")]
    [SerializeField] private Color swapReadyColor = new Color(0.2f, 0.6f, 1f, 1f);
    [UnityEngine.Serialization.FormerlySerializedAs("guardBrokenColor")]
    [SerializeField] private Color swapUnavailableColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("소환수 스킬 칸 활성 표시 (임시값)")]
    [SerializeField, Min(0.1f), Tooltip("활성 소환수 칸의 크기 배율.")] private float activeSlotScale = 1.15f;
    [SerializeField, Min(0.1f), Tooltip("대기 소환수 칸의 크기 배율.")] private float standbySlotScale = 0.9f;
    [SerializeField, Range(0f, 1f), Tooltip("대기 소환수 아이콘의 불투명도.")] private float standbyIconAlpha = 0.6f;
    [SerializeField, Tooltip("활성 소환수 아이콘의 외곽선 색.")] private Color activeOutlineColor = new Color(1f, 0.85f, 0.25f, 1f);

    [Header("골드 설정")]
    [SerializeField] private TextMeshProUGUI goldText;

    [Header("대쉬 / 소환수 스킬 칸 (슬롯 1 · 슬롯 2)")]
    [SerializeField] private DashCooldownUI dashCooldownUI;
    [UnityEngine.Serialization.FormerlySerializedAs("guardSlot")]
    [UnityEngine.Serialization.FormerlySerializedAs("skillSlotQ")]
    [SerializeField] private SkillSlotUI secondSummonSlot;
    [UnityEngine.Serialization.FormerlySerializedAs("skillSlotR")]
    [SerializeField] private SkillSlotUI minionSkillSlot;

    // ─────────────────────────────────────────────────────────────────────
    // 런타임
    // ─────────────────────────────────────────────────────────────────────
    private CharacterHealth       _playerHealth;
    private CharacterStatus       _playerStatus;   // 보호막 잔량 소스
    private PlayerSkillController _skillCtrl;
    private List<Image>           _hpFillImages  = new List<Image>();
    private SkillSlotUI[]         _skillSlots;
    private int _lastGold = int.MinValue; // dirty 비교용
    private sealed class HealingDisplay
    {
        public float amount, elapsed, progress;
        public bool arrived;
        public float Hidden => amount * (1f - progress);
    }
    private readonly List<HealingDisplay> _healing = new List<HealingDisplay>();
    private float _lastActualHP, _lastMaxHP, _displayedHP;
    private int _lastShownHP = -1, _lastShownMaxHP = -1, _lastShownShield = -1;

    [Tooltip("체력 숫자 뒤에 붙는 보호막 수치 (+N) 의 색")]
    [SerializeField] private Color shieldTextColor = new Color(0.75f, 0.93f, 1f, 1f);



    // ─────────────────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────────────────
    public void Initialize(CharacterHealth playerHealth)
    {
        if (_playerHealth != null)
        {
            _playerHealth.UpdateHPBar -= RefreshHP;
            _playerHealth.OnHealthRestored -= AnimateHealing;
        }
        _healing.Clear();
        _playerHealth = playerHealth;
        _skillSlots   = new SkillSlotUI[] { minionSkillSlot, secondSummonSlot };

        // 보호막은 CharacterHealth 가 아니라 같은 오브젝트의 CharacterStatus 가 들고 있다.
        _playerStatus = _playerHealth != null ? _playerHealth.GetComponent<CharacterStatus>() : null;

        if (_playerHealth != null)
        {
            _lastActualHP = _playerHealth.CurHP;
            _lastMaxHP = _playerHealth.MaxHP;
            _playerHealth.UpdateHPBar += RefreshHP;
            _playerHealth.OnHealthRestored += AnimateHealing;
            RefreshHP();
        }

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
        if (_playerHealth != null) _playerHealth.OnHealthRestored -= AnimateHealing;
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.OnMinionUpdated -= RefreshSkillIcons;

    }

    private void Update()
    {
        RefreshGold();
        AdvanceHealing(Time.unscaledDeltaTime);
        RefreshBars();   // 보호막은 피해/회복 이벤트 없이도 변한다 → 폴링
        RefreshSwapCooldown();
        UpdateSkillCooldowns();
    }

    // ─────────────────────────────────────────────────────────────────────
    // HP
    // ─────────────────────────────────────────────────────────────────────
    #region HP
    /// <summary>피해는 즉시, 회복 표시는 구슬 도착에 맞춰 갱신한다.</summary>
    public void RefreshHP()
    {
        if (_playerHealth == null) return;
        RefreshBars();
    }

    private void AnimateHealing(float amount)
    {
        var overlay = FeedbackOverlay.Instance;
        if (!isActiveAndEnabled || hpSprite == null || !hpSprite.isActiveAndEnabled || overlay == null || amount <= 0f) return;
        var heal = new HealingDisplay { amount = amount };
        _healing.Add(heal);
        // 실제 회복은 완료됐다. HUD만 날아오는 회복량을 표시에서 잠시 보류한다.
        bool started = overlay.Pulse(FeedbackCue.Heal, _playerHealth.transform.position,
            screenDestination: () => HealingDestination(heal),
            onFinished: reached =>
            {
                if (this == null) return;
                if (reached) heal.arrived = true;
                else _healing.Remove(heal);
                RefreshBars();
            });
        if (!started) _healing.Remove(heal);
    }

    private void AdvanceHealing(float dt)
    {
        if (!PolishFeedback.Enabled(FeedbackCue.Heal)) { _healing.Clear(); return; }
        for (int i = _healing.Count - 1; i >= 0; i--)
        {
            var heal = _healing[i];
            if (!heal.arrived) continue;
            heal.elapsed += dt;
            float t = Mathf.Clamp01(heal.elapsed / Mathf.Max(.01f, healFillDuration));
            // 잘못 꺾은 곡선도 체력바를 되감거나 실제 체력보다 높게 만들지 않는다.
            heal.progress = Mathf.Max(heal.progress, Mathf.Clamp01(healFillCurve.Evaluate(t)));
            if (t >= 1f) _healing.RemoveAt(i);
        }
    }

    private Vector2? HealingDestination(HealingDisplay heal)
    {
        if (this == null || !isActiveAndEnabled || hpSprite == null || !hpSprite.isActiveAndEnabled ||
            _playerHealth == null || _playerHealth.IsDead || !_healing.Contains(heal)) return null;
        float hidden = 0f, before = 0f;
        foreach (var pending in _healing) hidden += pending.Hidden;
        foreach (var pending in _healing)
        {
            if (pending == heal) break;
            before += pending.Hidden;
        }
        float shield = _playerStatus != null ? _playerStatus.TotalShield : 0f;
        float denominator = Mathf.Max(_playerHealth.MaxHP, _playerHealth.CurHP + shield);
        // 이번 회복으로 채워질 빈 구간의 중앙. 연속 회복은 각자의 구간으로 들어간다.
        float ratio = Mathf.Clamp01((_playerHealth.CurHP - hidden + before + heal.Hidden * .5f) / Mathf.Max(.001f, denominator));
        var rect = hpSprite.rectTransform;
        Vector3 world;
        if ((hpSprite.sprite == null || hpSprite.type != Image.Type.Filled) && rect.parent is RectTransform track)
        {
            // 단색 바는 anchorMax 자체가 줄어든다. 이미 줄어든 바가 아닌 전체 트랙을 기준으로 한다.
            float left = track.rect.xMin + rect.offsetMin.x;
            float right = track.rect.xMax + rect.offsetMax.x;
            world = track.TransformPoint(new Vector3(Mathf.Lerp(left, right, ratio),
                rect.localPosition.y + rect.rect.center.y, 0));
        }
        else world = rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, ratio), rect.rect.center.y, 0));
        var canvas = hpSprite.canvas;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(camera, world);
    }

    private void OnDisable()
    {
        _healing.Clear();
        RefreshBars();
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
    /// UpdateHPBar 에만 묶으면 그 순간들을 통째로 놓친다. 숫자는 표시할 정수가 바뀔 때만 갱신한다.
    /// </summary>
    private void RefreshBars()
    {
        if (hpSprite == null || _playerHealth == null || _playerHealth.MaxHP <= 0f) return;

        // 피해·사망·최대 체력 변경은 즉시 실제 값으로 합류한다. 오래된 구슬이 체력을 다시 올려 보이게 하지 않는다.
        if (_playerHealth.CurHP < _lastActualHP || !Mathf.Approximately(_lastMaxHP, _playerHealth.MaxHP) || _playerHealth.IsDead)
            _healing.Clear();
        _lastActualHP = _playerHealth.CurHP;
        _lastMaxHP = _playerHealth.MaxHP;
        float hidden = 0f;
        foreach (var heal in _healing) hidden += heal.Hidden;
        _displayedHP = Mathf.Max(0f, _playerHealth.CurHP - hidden);

        float shield = _playerStatus != null ? _playerStatus.TotalShield : 0f;
        float denom = Mathf.Max(_playerHealth.MaxHP, _playerHealth.CurHP + shield);

        SetFill(hpSprite, _displayedHP / denom);
        int shownHP = (int)_displayedHP, maxHP = (int)_playerHealth.MaxHP;
        // 보호막은 소수로 남을 수 있어 올림으로 표시한다(0.4 남아도 '+1'). 없으면 괄호 자체를 숨긴다.
        int shownShield = shield > 0.01f ? Mathf.CeilToInt(shield) : 0;
        if (hpText != null && (shownHP != _lastShownHP || maxHP != _lastShownMaxHP || shownShield != _lastShownShield))
        {
            hpText.text = shownShield > 0
                ? $"{shownHP} / {maxHP} <color=#{ColorUtility.ToHtmlStringRGBA(shieldTextColor)}>(+{shownShield})</color>"
                : $"{shownHP} / {maxHP}";
            _lastShownHP = shownHP; _lastShownMaxHP = maxHP; _lastShownShield = shownShield;
        }

        if (shieldSprite == null) return;
        // 보호막이 없으면 두 바의 폭이 같아져 가장자리가 1픽셀 삐져나올 수 있다. 아예 끈다.
        bool show = shield > 0.01f;
        if (shieldSprite.enabled != show) shieldSprite.enabled = show;
        // 보호막바는 '보호막만큼'이 아니라 '체력+보호막'까지 채운다. 체력바 뒤에 깔려 있어서
        // 앞의 빨간 바가 왼쪽을 가리고 그 오른쪽만 흰색으로 보인다 = 오프셋 계산이 필요 없다.
        if (show) SetFill(shieldSprite, (_displayedHP + shield) / denom);
    }

    private static void SetFill(Image img, float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Abs(img.fillAmount - value) > 0.0005f) img.fillAmount = value;
        // 단색 바, 또는 9분할(Sliced) 스킨 바는 fillAmount 가 먹지 않으므로 실제 너비(anchorMax)를 바꾼다.
        // (9분할이라 바가 줄어도 양 끝 모양·하이라이트가 찌그러지지 않는다)
        if (img.sprite == null || img.type != Image.Type.Filled) img.rectTransform.anchorMax = new Vector2(value, 1f);
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
    // 소환수 스킬 칸 + 쿨타임 + 스왑
    // ─────────────────────────────────────────────────────────────────────
    #region Skill Cooldowns

    /// <summary>
    /// 소환수 스킬 칸을 갱신합니다. 인덱스 0/1 이 소환수 슬롯 0/1 입니다.
    /// InventoryManager.OnMinionUpdated에 등록되어 장착/스왑 시 자동 호출됩니다.
    /// </summary>
    public void RefreshSkillIcons()
    {
        if (_skillCtrl == null && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
            _skillCtrl = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>();
        if (_skillSlots == null) return;
        for (int i = 0; i < _skillSlots.Length; i++)
            if (_skillSlots[i] != null) RefreshSummonSlot(_skillSlots[i], i);
    }

    private void UpdateSkillCooldowns()
    {
        if (_skillCtrl == null || _skillSlots == null) return;
        for (int i = 0; i < _skillSlots.Length; i++)
            if (_skillSlots[i] != null) UpdateSummonCooldown(_skillSlots[i], i);
    }

    private void RefreshSwapCooldown()
    {
        var inventory = InventoryManager.Instance;
        if (_skillCtrl == null || inventory == null) return;
        bool canSwap = inventory.EquippedSummonCount >= InventoryManager.SLOT_COUNT;
        float remaining = _skillCtrl.SwapCooldownRemaining;
        float duration = _skillCtrl.SwapCooldownDuration;
        float ready = !canSwap ? 0f : (duration > 0f ? 1f - Mathf.Clamp01(remaining / duration) : 1f);
        if (swapCooldownFill != null)
        {
            SetFill(swapCooldownFill, ready);
            swapCooldownFill.color = canSwap && remaining <= 0f ? swapReadyColor : swapUnavailableColor;
        }
        if (swapCooldownText == null) return;
        if (!canSwap) swapCooldownText.SetText("스왑 불가");
        else if (remaining > 0f) swapCooldownText.SetText("스왑 {0:1}", remaining);
        else swapCooldownText.SetText("스왑 가능 [Space]");
    }

    private void RefreshSummonSlot(SkillSlotUI slot, int index)
    {
        var inventory = InventoryManager.Instance;
        MainMinionDataSO summon = inventory != null ? inventory.GetSummonAt(index) : null;
        bool has = summon != null;
        bool active = has && inventory.ActiveSummonSlot == index;

        if (slot.SlotRoot != null)
        {
            if (!slot.SlotRoot.activeSelf) slot.SlotRoot.SetActive(true);
            slot.SlotRoot.transform.localScale = Vector3.one * (active ? activeSlotScale : standbySlotScale);
            // 칸 아래 조작 안내. 프리팹의 기존 글자를 그대로 쓴다.
            var keyLabel = slot.SlotRoot.transform.Find("Text (TMP)")?.GetComponent<TMP_Text>();
            if (keyLabel != null) keyLabel.text = active ? "우클릭" : has ? "대기" : "";
        }

        if (slot.SkillIcon != null)
        {
            slot.SkillIcon.enabled = has;
            var outline = slot.SkillIcon.GetComponent<Outline>();
            if (outline == null) outline = slot.SkillIcon.gameObject.AddComponent<Outline>();
            outline.effectColor = activeOutlineColor;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.enabled = active;

            var tooltip = slot.SkillIcon.GetComponent<SkillTooltipTrigger>();
            if (has)
            {
                slot.SkillIcon.sprite = summon.minionIcon != null ? summon.minionIcon : summon.ResolveIcon();
                slot.SkillIcon.color  = new Color(1f, 1f, 1f, active ? 1f : standbyIconAlpha);
                slot.SkillIcon.type   = Image.Type.Simple;

                if (tooltip == null) tooltip = slot.SkillIcon.gameObject.AddComponent<SkillTooltipTrigger>();
                string desc = summon.ResolveDescription();
                if (!string.IsNullOrEmpty(desc)) tooltip.SetData(summon.ResolveTitle(), desc);
                else tooltip.Clear();
            }
            else
            {
                slot.SkillIcon.sprite = null;
                if (tooltip != null) tooltip.Clear();
                slot.SkillIcon.color = new Color(1f, 1f, 1f, unavailableSkillAlpha);
            }
        }

        // 쿨타임 표시는 UpdateSummonCooldown 이 매 프레임 다시 그린다(스왑해도 이어진다).
        if (!has)
        {
            if (slot.CooldownFill != null) { slot.CooldownFill.fillAmount = 0f; slot.CooldownFill.gameObject.SetActive(false); }
            if (slot.CooldownText != null) slot.CooldownText.text = "";
        }
    }

    private void UpdateSummonCooldown(SkillSlotUI slot, int index)
    {
        var summon = InventoryManager.Instance != null ? InventoryManager.Instance.GetSummonAt(index) : null;
        if (summon == null || summon.minionSkill == null) return;

        float maxCd     = _skillCtrl.GetSummonCooldownDuration(index);
        float remaining = _skillCtrl.GetSummonCooldownRemaining(index);
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

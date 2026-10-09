using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 구르기 사용 가능 여부 UI.
/// 변경이 있을 때만 UI를 갱신하여 매 프레임 Draw Call / GC 발생을 최소화합니다.
///
/// ▸ MeleeDodgeController 있을 때 (스택 구르기)
///     - ChargeText : "2" 형태로 현재 스택 표시
///     - CooldownFill : 회복 중인 스택의 진행률 (1→0 줄어듦)
///
/// ▸ MeleeDodgeController 없을 때 (기본 1스택 구르기)
///     - ChargeText : "1" / "0" 표시
///     - CooldownFill : 쿨타임 진행률 (1→0 줄어듦)
/// </summary>
public class DashCooldownUI : MonoBehaviour
{
    [Header("스택")]
    [SerializeField] private GameObject[] dashCount; 

    [Header("쿨타임/회복 Fill Image (1→0 줄어듦)")]
    [SerializeField] private Image cooldownFill;

    [Header("대쉬 아이콘 (대쉬 종류별 교체용)")]
    [Tooltip("기본 아이콘(Dash.png)은 프리팹에 박아둔다. 나중에 대쉬 종류가 생기면 SetDashIcon 으로 교체.")]
    [SerializeField] private Image dashIcon;

    [Header("플레이어 발밑 표시 (HUD 프리팹에 배치)")]
    [SerializeField] private RectTransform worldPips;
    [SerializeField] private Image[] worldPipFills;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, -0.55f, 0f);
    private Canvas _canvas;
    private Camera _worldCamera;

    // ─── 런타임 ──────────────────────────────────────────────────────
    private PlayerController     _player;
    private MeleeDodgeController _dodge;

    // ─── dirty 비교용 캐시 ───────────────────────────────────────────
    private int   _lastCharges    = -1;
    private float _lastFill       = -1f;
private const float FILL_THRESHOLD = 0.005f; // fillAmount 변경 최소 단위

    // ─── 스택 핀(GameObject) 관리 ───
    private int   _lastPipCurrent = -1;
    private int   _lastPipMax     = -1;
    private const float PIP_USED_ALPHA = 0.35f; // recharging pip alpha

    // ─────────────────────────────────────────────────────────────────
    public void Initialize(PlayerController playerController)
    {
        _player = playerController;
        _dodge  = playerController != null
            ? playerController.GetComponent<MeleeDodgeController>()
            : null;
        _canvas = GetComponentInParent<Canvas>();
        _worldCamera = Camera.main;

        if (cooldownFill != null)
        {
            cooldownFill.type          = Image.Type.Filled;
            cooldownFill.fillMethod    = Image.FillMethod.Radial360;
            cooldownFill.fillClockwise = false;
        }

        // 캐시 초기화 (강제 1회 갱신)
        _lastCharges = -1;
        _lastFill    = -1f;
        _lastPipCurrent = _lastPipMax = -1;
        ForceRefresh();

        Debug.Log("<color=yellow>[DashCooldownUI]</color> Initialized.");
    }

    /// <summary>
    /// 대쉬 아이콘 스프라이트를 교체한다. 지금은 프리팹에 박힌 기본 아이콘(Dash.png)이 그대로 뜨고,
    /// 나중에 대쉬 종류(기본 구르기 / 2스택 구르기 / 유물 대쉬 등)가 생기면 그 종류를 정하는 쪽에서
    /// 이걸 호출해 갈아끼우면 된다. null 이면 무시(현재 아이콘 유지).
    /// </summary>
    public void SetDashIcon(Sprite icon)
    {
        if (dashIcon != null && icon != null) dashIcon.sprite = icon;
    }

    // ─────────────────────────────────────────────────────────────────
    private void Update()
    {
        if (_player == null)
        {
            if (GameManager.Instance?.PLAYERCONTROLLER != null)
                Initialize(GameManager.Instance.PLAYERCONTROLLER);
            return;
        }

        if (_dodge != null)
            UpdateMeleeMode();
        else
            UpdateBasicMode();
    }

    private void LateUpdate()
    {
        if (worldPips == null) return;
        if (_worldCamera == null) _worldCamera = Camera.main;
        bool visible = _player != null && _worldCamera != null && _canvas != null;
        Vector3 screen = visible ? _worldCamera.WorldToScreenPoint(_player.transform.position + worldOffset) : Vector3.zero;
        visible = visible && screen.z > 0f && _player.gameObject.activeInHierarchy;
        worldPips.gameObject.SetActive(visible);
        if (!visible) return;
        var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        // 변환 결과는 앵커가 아니라 부모 피벗 기준의 로컬 좌표다.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)worldPips.parent, screen, camera, out var point))
            worldPips.localPosition = point;
        if (worldPipFills == null) return;
        int current = _dodge != null ? _dodge.CurrentCharges : (_player.DashCooldownProgress >= 1f ? 1 : 0);
        int max = _dodge != null ? _dodge.MaxCharges : 1;
        float progress = _dodge != null ? _dodge.RechargeProgress : _player.DashCooldownProgress;
        for (int i = 0; i < worldPipFills.Length; i++)
        {
            var fill = worldPipFills[i];
            if (fill == null) continue;
            // Charge / Track / Fill: 레이아웃에 참여하는 Charge 전체를 숨긴다.
            fill.transform.parent.parent.gameObject.SetActive(i < max);
            fill.rectTransform.anchorMax = new Vector2(i < current ? 1f : i == current ? progress : 0f, 1f);
        }
    }

    // ─── MeleeDodgeController 모드 ───────────────────────────────────
    private void UpdateMeleeMode()
    {
        int   cur      = _dodge.CurrentCharges;
        int   max      = _dodge.MaxCharges;
        float fill     = cur < max ? 1f - _dodge.RechargeProgress : 0f;

        // 스택 숫자: 정수 비교 → GC 없음
        if (cur != _lastCharges)
        {
            _lastCharges = cur;
        }

        // Fill: 임계값 비교로 불필요한 갱신 제거
        if (Mathf.Abs(fill - _lastFill) > FILL_THRESHOLD)
        {
            _lastFill = fill;
            if (cooldownFill != null)
                cooldownFill.fillAmount = fill;
        }
        UpdatePips(cur, max);
    }

    // ─── 기본 1스택 구르기 모드 ──────────────────────────────────────
    private void UpdateBasicMode()
    {
        float progress = _player.DashCooldownProgress; // 0=쿨직후, 1=사용가능
        bool  ready    = progress >= 1f;
        int   charge   = ready ? 1 : 0;
        float fill     = ready ? 0f : 1f - progress;

        // 스택 숫자: 0 또는 1만 바뀜 → 정수 비교
        if (charge != _lastCharges)
        {
            _lastCharges = charge;
        }

        // Fill
        if (Mathf.Abs(fill - _lastFill) > FILL_THRESHOLD)
        {
            _lastFill = fill;
            if (cooldownFill != null)
                cooldownFill.fillAmount = fill;
        }
        UpdatePips(charge, 1);
    }

    // ─── 강제 전체 갱신 (Initialize 후 1회) ──────────────────────────
    private void ForceRefresh()
    {
        if (_dodge != null)
        {
            int   cur  = _dodge.CurrentCharges;
            float fill = cur < _dodge.MaxCharges ? 1f - _dodge.RechargeProgress : 0f;

            _lastCharges = cur;
            _lastFill    = fill;
            if (cooldownFill != null) cooldownFill.fillAmount = fill;
            UpdatePips(cur, _dodge.MaxCharges);
        }
        else if (_player != null)
        {
            float progress = _player.DashCooldownProgress;
            bool  ready    = progress >= 1f;
            int   charge   = ready ? 1 : 0;
            float fill     = ready ? 0f : 1f - progress;

            _lastCharges = charge;
            _lastFill    = fill;
            if (cooldownFill != null) cooldownFill.fillAmount = fill;
            UpdatePips(charge, 1);
        }
    }

    // ─── stack pip GameObjects ───
    private void UpdatePips(int current, int max)
    {
        if (dashCount == null || dashCount.Length == 0) return;
        if (current == _lastPipCurrent && max == _lastPipMax) return;

        _lastPipCurrent = current;
        _lastPipMax     = max;

        for (int i = 0; i < dashCount.Length; i++)
        {
            var pip = dashCount[i];
            if (pip == null) continue;

            bool withinMax = i < max;
            if (pip.activeSelf != withinMax)
                pip.SetActive(withinMax);

            if (!withinMax) continue;

            var img = pip.GetComponent<Image>();
            if (img == null) continue;

            bool available = i < current;
            Color c = img.color;
            float targetAlpha = available ? 1f : PIP_USED_ALPHA;
            if (!Mathf.Approximately(c.a, targetAlpha))
            {
                c.a = targetAlpha;
                img.color = c;
            }
        }
    }
}

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

        if (cooldownFill != null)
        {
            cooldownFill.type          = Image.Type.Filled;
            cooldownFill.fillMethod    = Image.FillMethod.Radial360;
            cooldownFill.fillClockwise = false;
        }

        // 캐시 초기화 (강제 1회 갱신)
        _lastCharges = -1;
        _lastFill    = -1f;
        ForceRefresh();

        Debug.Log("<color=yellow>[DashCooldownUI]</color> Initialized.");
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

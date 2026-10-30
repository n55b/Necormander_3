using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 체력바 · 가드 게이지 연출. 바의 값을 '읽기만' 하므로 기존 PlayerStateUI 로직과 부딪히지 않는다.
///  · 피해 잔상 : 깎인 구간이 잠시 남았다가 따라 줄어든다.
///  · 번쩍임   : 깎일 때 / 회복할 때 / 보호막이 생길 때 테두리 색이 번쩍인다.
///  · 툭 튀기  : 깎이거나 보호막이 생길 때 바가 살짝 커졌다 돌아온다.
///  · 위험 경고 : 바가 일정 비율 아래면 테두리가 깜빡인다.
/// 값은 'UI 스킨 설정 > ⑥ 체력 · 게이지 연출'에서 조절한다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI 스킨/게이지 연출 (체력 · 가드)")]
public class UIBarMotion : MonoBehaviour
{
    public enum Kind
    {
        [InspectorName("체력바")] Hp,
        [InspectorName("가드 게이지")] Guard,
    }

    [KoLabel("게이지 종류"), Tooltip("UI 스킨 설정의 어느 값을 쓸지 고릅니다.")]
    public Kind kind = Kind.Hp;

    [KoLabel("채워지는 바")] public Image fill;
    [KoLabel("피해 잔상 바"), Tooltip("채워지는 바 뒤에 깔린 이미지. 비우면 잔상이 없습니다.")] public Image trail;
    [KoLabel("보호막 바 (선택)")] public Image shield;
    [KoLabel("번쩍일 테두리")] public Image frame;
    [KoLabel("툭 튈 대상 (비우면 이 오브젝트)")] public RectTransform punchTarget;

    private float _last = -1f, _trail, _hold, _shrinkT = -1f, _shrinkFrom, _dangerPhase;
    private float _flashT = -1f, _flashDur;
    private Color _flashColor;
    private readonly UIPunch _punch = new UIPunch();
    private bool _lastShield;
    private float _warmup;                // 켜진 직후 초기값이 들어오는 동안은 '피격'으로 치지 않는다
    private UISkinPart _framePart;
    private Color _frameOwnColor = Color.white;

    private BarMotion Motion
    {
        get
        {
            var s = UISkinSettings.Active;
            if (s == null) return kind == Kind.Hp ? FallbackHp : FallbackGuard;
            return kind == Kind.Hp ? s.hpBar : s.guardBar;
        }
    }

    // 설정 파일이 없을 때 쓰는 기본값 (매 프레임 새로 만들지 않게 한 번만)
    private static readonly BarMotion FallbackHp = BarMotion.Hp(), FallbackGuard = BarMotion.Guard();

    private RectTransform PunchTarget => punchTarget != null ? punchTarget : transform as RectTransform;

    private void Awake()
    {
        if (frame != null)
        {
            _framePart = frame.GetComponent<UISkinPart>();
            _frameOwnColor = frame.color;
        }
    }

    private void OnEnable()
    {
        _last = -1f;
        _flashT = _shrinkT = -1f;
        _warmup = 0.5f;
        _punch.Bind(PunchTarget);
    }

    private void OnDisable()
    {
        _punch.Stop();
        if (frame != null) frame.color = FrameBase;
        if (trail != null) trail.enabled = false;
    }

    private Color FrameBase => UISkinPart.BaseColorOf(_framePart, _frameOwnColor);

    private static float Read(Image img) =>
        img.type == Image.Type.Filled ? img.fillAmount : img.rectTransform.anchorMax.x;

    private static void Write(Image img, float v)
    {
        if (img.type == Image.Type.Filled) img.fillAmount = v;
        else
        {
            var max = img.rectTransform.anchorMax;
            if (!Mathf.Approximately(max.x, v)) img.rectTransform.anchorMax = new Vector2(v, max.y);
        }
    }

    private void Flash(Color c, float time)
    {
        if (time <= 0f) return;
        _flashColor = c; _flashDur = time; _flashT = 0f;
    }

    private void Punch(float amount, float time) => _punch.Play(amount, time);

    private void LateUpdate()
    {
        if (fill == null) return;
        var m = Motion;
        bool on = UISkinSettings.MotionOn;
        float dt = UISkinSettings.DeltaTime;
        float v = Read(fill);

        if (_last < 0f || _warmup > 0f) { _warmup -= dt; _last = v; _trail = v; _lastShield = shield != null && shield.enabled; }

        // ── 값 변화 감지 ───────────────────────────────────────────
        if (v < _last - 0.0005f)
        {
            _trail = Mathf.Max(_trail, _last);   // 이미 줄어들던 잔상은 그 자리에서 다시 멈춘다
            _hold = m.trailHold;
            _shrinkT = -1f;
            if (on) { Flash(m.hitFlashColor, m.hitFlashTime); Punch(m.hitPunch, m.hitPunchTime); }
        }
        else if (v > _last + 0.0005f)
        {
            if (on) Flash(m.healFlashColor, m.healFlashTime);
        }
        _last = v;

        // ── 피해 잔상 ──────────────────────────────────────────────
        if (trail != null)
        {
            if (!on || !m.trail) _trail = v;
            else if (_trail > v)
            {
                if (_hold > 0f) _hold -= dt;
                else
                {
                    if (_shrinkT < 0f) { _shrinkT = 0f; _shrinkFrom = _trail; }
                    _shrinkT += dt / Mathf.Max(0.01f, m.trailShrink);
                    float k = m.trailCurve != null && m.trailCurve.length > 0 ? m.trailCurve.Evaluate(Mathf.Clamp01(_shrinkT)) : _shrinkT;
                    _trail = Mathf.Lerp(_shrinkFrom, v, k);
                    if (_shrinkT >= 1f) { _trail = v; _shrinkT = -1f; }
                }
            }
            else _trail = v;

            bool show = _trail > v + 0.001f;
            if (trail.enabled != show) trail.enabled = show;
            if (show)
            {
                Write(trail, _trail);
                if (trail.color != m.trailColor) trail.color = m.trailColor;
            }
        }

        // ── 보호막 생김 ────────────────────────────────────────────
        if (shield != null)
        {
            bool has = shield.enabled && shield.gameObject.activeInHierarchy;
            if (has && !_lastShield && on) { Flash(m.shieldFlashColor, m.shieldFlashTime); Punch(m.shieldPunch, m.hitPunchTime); }
            _lastShield = has;
        }

        // ── 테두리 색 (위험 깜빡임 + 번쩍임) ───────────────────────
        if (frame != null)
        {
            Color c = FrameBase;
            if (on && m.danger && v > 0.0001f && v <= m.dangerThreshold)
            {
                _dangerPhase += dt / Mathf.Max(0.05f, m.dangerPeriod);
                float w = (0.5f - 0.5f * Mathf.Cos(_dangerPhase * Mathf.PI * 2f)) * m.dangerStrength;
                c = Color.Lerp(c, m.dangerColor, w);
            }
            else _dangerPhase = 0f;

            if (_flashT >= 0f)
            {
                _flashT += dt;
                float k = 1f - Mathf.Clamp01(_flashT / _flashDur);
                c = Color.Lerp(c, _flashColor, k);
                if (_flashT >= _flashDur) _flashT = -1f;
            }
            if (frame.color != c) frame.color = c;
        }

        // ── 툭 튀기 ────────────────────────────────────────────────
        _punch.Tick(dt);
    }
}

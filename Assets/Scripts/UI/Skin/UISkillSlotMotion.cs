using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 스킬 칸 연출. 쿨타임 표시 이미지를 '읽기만' 해서
///  · 쿨타임 중에는 테두리를 어둡게,
///  · 쿨타임이 끝나는 순간 테두리를 번쩍이고 칸을 툭 튀게 한다.
/// 값은 'UI 스킨 설정 > ⑦ 스킬 아이콘 연출'에서 조절한다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI 스킨/스킬 아이콘 연출")]
public class UISkillSlotMotion : MonoBehaviour
{
    [KoLabel("쿨타임 표시 이미지"), Tooltip("어둡게 덮이며 줄어드는 이미지(Image_CoolTime). 이게 사라지거나 0이 되면 '준비 완료'로 봅니다.")]
    public Image cooldown;
    [KoLabel("테두리")] public Image frame;
    [KoLabel("튀어오를 대상 (비우면 이 오브젝트)")] public RectTransform punchTarget;

    private bool _cooling, _init;
    private float _flashT = -1f;
    private readonly UIPunch _punch = new UIPunch();
    private UISkinPart _framePart;
    private Color _frameOwnColor = Color.white;

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
        _init = false;
        _flashT = -1f;
        _punch.Bind(PunchTarget);
    }

    private void OnDisable()
    {
        _punch.Stop();
        if (frame != null) frame.color = FrameBase;
    }

    private Color FrameBase => UISkinPart.BaseColorOf(_framePart, _frameOwnColor);

    private bool IsCooling =>
        cooldown != null && cooldown.isActiveAndEnabled &&
        (cooldown.type != Image.Type.Filled || cooldown.fillAmount > 0.001f);

    private void LateUpdate()
    {
        var s = UISkinSettings.Active;
        var m = s != null ? s.skill : null;
        bool on = m != null && m.enabled && UISkinSettings.MotionOn;
        float dt = UISkinSettings.DeltaTime;

        bool cooling = IsCooling;
        if (_init && _cooling && !cooling && on)
        {
            if (m.readyFlashTime > 0f) _flashT = 0f;
            _punch.Play(m.readyPunch, m.readyPunchTime);
        }
        _cooling = cooling;
        _init = true;

        if (frame != null)
        {
            Color c = FrameBase;
            if (on && cooling) c *= m.coolingFrameColor;
            if (on && _flashT >= 0f)
            {
                _flashT += dt;
                float k = 1f - Mathf.Clamp01(_flashT / Mathf.Max(0.01f, m.readyFlashTime));
                c = Color.Lerp(c, m.readyFlashColor, k);
                if (_flashT >= m.readyFlashTime) _flashT = -1f;
            }
            if (frame.color != c) frame.color = c;
        }

        if (on) _punch.Tick(dt);
        else if (_punch.IsPlaying) _punch.Stop();
    }
}

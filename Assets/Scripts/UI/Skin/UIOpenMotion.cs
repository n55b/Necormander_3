using System.Collections;
using UnityEngine;

/// <summary>
/// 창이 켜질 때(SetActive true) 크기·투명도·위치를 살짝 움직여 등장시킨다.
/// 값은 'UI 스킨 설정 > ⑤ 창 열림 연출'에서 조절한다. 창마다 다르게 하고 싶으면 '연출 종류'를 '직접 설정'으로.
/// 닫힘 요청은 입력을 즉시 해제하고 현재 자세에서 역재생한다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI 스킨/창 열림 연출")]
public class UIOpenMotion : MonoBehaviour
{
    public enum Kind
    {
        [InspectorName("큰 창")] Window,
        [InspectorName("작은 창 · 툴팁")] Popup,
        [InspectorName("직접 설정 (아래 값 사용)")] Custom,
    }

    [KoLabel("연출 종류"), Tooltip("'큰 창'/'작은 창'은 UI 스킨 설정의 공용 값을 씁니다. 이 창만 다르게 하려면 '직접 설정'.")]
    public Kind kind = Kind.Window;

    [KoLabel("직접 설정 값"), Tooltip("연출 종류가 '직접 설정'일 때만 쓰입니다.")]
    public OpenMotion custom = OpenMotion.Window();

    [KoLabel("움직일 대상 (비우면 이 오브젝트)")]
    public RectTransform target;

    private Coroutine _co;
    private RectTransform _t;
    private CanvasGroup _group;
    private Vector2 _basePos;
    private float _baseAlpha = 1f;
    private bool _moved, _faded;

    private float progress;
    private bool closing, posing, finishOnEnable;
    private CanvasGroup inputGroup;
    private bool raycasts, interactable;
    private bool fadeOnly;
    private static readonly OpenMotion PanelFade = new OpenMotion { duration = .15f, startScale = 1f, startOffset = Vector2.zero };
    private static readonly OpenMotion FallbackWindow = OpenMotion.Window(), FallbackPopup = OpenMotion.Popup();

    private OpenMotion Motion
    {
        get
        {
            if (fadeOnly) return PanelFade;
            if (kind == Kind.Custom) return custom;
            var s = UISkinSettings.Active;
            if (s == null) return kind == Kind.Window ? FallbackWindow : FallbackPopup;
            return kind == Kind.Window ? s.windowOpen : s.popupOpen;
        }
    }

    private void Awake()
    {
        _t = target != null ? target : transform as RectTransform;
    }

    public static bool IsShown(GameObject panel) => panel != null && panel.activeSelf
        && (!panel.TryGetComponent<UIOpenMotion>(out var motion) || !motion.closing);

    public static void SetVisible(GameObject panel, bool visible)
    {
        if (panel == null) return;
        if (!Application.isPlaying || !(panel.transform is RectTransform) || !UISkinSettings.MotionOn)
        {
            panel.SetActive(visible);
            return;
        }
        panel.TryGetComponent<UIOpenMotion>(out var motion);
        if (panel.activeSelf == visible && (motion == null || !motion.closing)) return;
        if (!panel.activeInHierarchy && panel.activeSelf)
        {
            if (motion != null) motion.closing = motion.finishOnEnable = false;
            panel.SetActive(visible);
            return;
        }
        if (motion == null)
        {
            // Adding to a visible panel invokes OnEnable immediately. Configure it before playing.
            motion = panel.AddComponent<UIOpenMotion>();
            motion.StopMotion();
            motion.Restore();
            motion.fadeOnly = true;
        }
        if (!motion.enabled || motion.Motion == null || !motion.Motion.enabled)
        {
            panel.SetActive(visible);
            return;
        }
        if (!panel.activeSelf) panel.SetActive(true);
        else motion.SetDirection(visible);
    }

    private void OnEnable()
    {
        if (!Application.isPlaying || _t == null || !UISkinSettings.MotionOn) return;
        var m = Motion;
        if (m == null || !m.enabled) return;
        if (finishOnEnable)
        {
            finishOnEnable = closing = false;
            SetDirection(false);
            progress = 0f;
            Pose(m, progress);
        }
        else SetDirection(true);
    }

    private void SetDirection(bool show)
    {
        if (closing == !show && _co != null) return;
        StopMotion();
        var m = Motion;
        if (!posing)
        {
            _basePos = _t.anchoredPosition;
            _moved = m.startOffset != Vector2.zero;
            _faded = m.startAlpha < .999f;
            if (_faded)
            {
                if (!_t.TryGetComponent(out _group)) _group = _t.gameObject.AddComponent<CanvasGroup>();
                _baseAlpha = _group.alpha;
            }
            progress = show ? 0f : 1f;
            posing = true;
        }

        if (show) RestoreInput();
        else if (!closing)
        {
            if (!TryGetComponent(out inputGroup)) inputGroup = gameObject.AddComponent<CanvasGroup>();
            interactable = inputGroup.interactable;
            raycasts = inputGroup.blocksRaycasts;
            inputGroup.interactable = inputGroup.blocksRaycasts = false;
        }
        closing = !show;
        Pose(m, progress);
        _co = StartCoroutine(Play(m, show));
    }

    private IEnumerator Play(OpenMotion m, bool show)
    {
        float wait = show && progress == 0f ? m.delay : 0f;
        float duration = show ? m.duration : Mathf.Min(.14f, m.duration);
        float end = show ? 1f : 0f;
        do
        {
            yield return null;
            // Closing a menu must also finish while gameplay is paused.
            float dt = closing || Time.timeScale == 0f ? Time.unscaledDeltaTime : UISkinSettings.DeltaTime;
            wait -= dt;
            if (wait > 0f) continue;
            progress = Mathf.MoveTowards(progress, end, dt / Mathf.Max(.01f, duration));
            Pose(m, progress);
        } while (progress != end || wait > 0f);
        _co = null;
        if (show) Restore();
        else gameObject.SetActive(false);
    }

    private void StopMotion()
    {
        if (_co != null) StopCoroutine(_co);
        _co = null;
    }

    private void RestoreInput()
    {
        if (inputGroup == null) return;
        inputGroup.interactable = interactable;
        inputGroup.blocksRaycasts = raycasts;
        inputGroup = null;
    }

    private void OnDisable()
    {
        StopMotion();
        RestoreInput();
        Restore();
        finishOnEnable = closing && gameObject.activeSelf && !gameObject.activeInHierarchy;
        closing = finishOnEnable;
    }

    private void Pose(OpenMotion m, float k)
    {
        float s = m.scaleCurve != null && m.scaleCurve.length > 0 ? m.scaleCurve.Evaluate(k) : k;
        if (_moved) _t.anchoredPosition = _basePos + m.startOffset * (1f - s);
        if (_faded && _group != null)
        {
            float a = m.alphaCurve != null && m.alphaCurve.length > 0 ? m.alphaCurve.Evaluate(k) : k;
            _group.alpha = Mathf.LerpUnclamped(m.startAlpha, _baseAlpha, a);
        }
    }

    private void Restore()
    {
        if (_t == null) return;
        if (_moved) _t.anchoredPosition = _basePos;
        if (_faded && _group != null) _group.alpha = _baseAlpha;
        _moved = _faded = posing = false;
    }
}

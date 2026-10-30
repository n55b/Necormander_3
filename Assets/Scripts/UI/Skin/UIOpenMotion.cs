using System.Collections;
using UnityEngine;

/// <summary>
/// 창이 켜질 때(SetActive true) 크기·투명도·위치를 살짝 움직여 등장시킨다.
/// 값은 'UI 스킨 설정 > ⑤ 창 열림 연출'에서 조절한다. 창마다 다르게 하고 싶으면 '연출 종류'를 '직접 설정'으로.
/// 창을 여닫는 코드(UIPopUpManager 등)는 건드리지 않는다.
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
    private Vector3 _baseScale;
    private Vector2 _basePos;
    private float _baseAlpha = 1f;
    private bool _moved, _faded, _scaled;

    private static readonly OpenMotion FallbackWindow = OpenMotion.Window(), FallbackPopup = OpenMotion.Popup();

    private OpenMotion Motion
    {
        get
        {
            if (kind == Kind.Custom) return custom;
            var s = UISkinSettings.Active;
            if (s == null) return kind == Kind.Window ? FallbackWindow : FallbackPopup;
            return kind == Kind.Window ? s.windowOpen : s.popupOpen;
        }
    }

    private void Awake()
    {
        _t = target != null ? target : transform as RectTransform;
        if (_t != null) _baseScale = _t.localScale;
    }

    private void OnEnable()
    {
        if (!Application.isPlaying || _t == null || !UISkinSettings.MotionOn) return;
        var m = Motion;
        if (m == null || !m.enabled) return;
        _co = StartCoroutine(Play(m));
    }

    private void OnDisable()
    {
        if (_co != null) { StopCoroutine(_co); _co = null; }
        Restore();
    }

    private IEnumerator Play(OpenMotion m)
    {
        _basePos = _t.anchoredPosition;
        _scaled = !Mathf.Approximately(m.startScale, 1f);
        _moved = m.startOffset != Vector2.zero;
        _faded = m.startAlpha < 0.999f;
        if (_faded)
        {
            if (_group == null) _group = _t.GetComponent<CanvasGroup>();
            if (_group == null) _group = _t.gameObject.AddComponent<CanvasGroup>();
            _baseAlpha = _group.alpha;
        }

        Pose(m, 0f);
        float wait = m.delay;
        while (wait > 0f) { wait -= UISkinSettings.DeltaTime; yield return null; }

        float time = 0f, duration = Mathf.Max(0.01f, m.duration);
        while (time < duration)
        {
            time += UISkinSettings.DeltaTime;
            Pose(m, Mathf.Clamp01(time / duration));
            yield return null;
        }
        Restore();
        _co = null;
    }

    private void Pose(OpenMotion m, float k)
    {
        float s = m.scaleCurve != null && m.scaleCurve.length > 0 ? m.scaleCurve.Evaluate(k) : k;
        if (_scaled) _t.localScale = _baseScale * Mathf.LerpUnclamped(m.startScale, 1f, s);
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
        if (_scaled) _t.localScale = _baseScale;
        if (_moved) _t.anchoredPosition = _basePos;
        if (_faded && _group != null) _group.alpha = _baseAlpha;
        _scaled = _moved = _faded = false;
    }
}

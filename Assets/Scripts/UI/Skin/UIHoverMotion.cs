using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 마우스를 올리면 살짝 커지고, 누르면 살짝 작아진다.
/// 값은 'UI 스킨 설정 > ⑧ 마우스 반응'에서 조절한다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI 스킨/마우스 반응")]
public class UIHoverMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [KoLabel("움직일 대상 (비우면 이 오브젝트)")]
    public RectTransform target;

    private RectTransform _t;
    private Vector3 _baseScale;
    private float _current = 1f;
    private bool _over, _down;

    private void Awake()
    {
        _t = target != null ? target : transform as RectTransform;
        if (_t != null) _baseScale = _t.localScale;
    }

    public void OnPointerEnter(PointerEventData e) => _over = true;
    public void OnPointerExit(PointerEventData e) { _over = false; _down = false; }
    public void OnPointerDown(PointerEventData e) => _down = true;
    public void OnPointerUp(PointerEventData e) => _down = false;

    private void Update()
    {
        if (_t == null) return;
        var s = UISkinSettings.Active;
        var m = s != null ? s.hover : null;
        bool on = m != null && m.enabled && UISkinSettings.MotionOn;

        float goal = 1f;
        if (on && _over) goal = _down ? m.pressScale : m.hoverScale;

        if (Mathf.Approximately(_current, goal)) return;
        float time = on ? m.time : 0f;
        if (time <= 0.0001f) _current = goal;
        else
        {
            // '바뀌는 시간' 안에 거의(98%) 도달하도록 부드럽게 따라간다.
            float k = 1f - Mathf.Exp(-4f * UISkinSettings.DeltaTime / time);
            _current = Mathf.Lerp(_current, goal, k);
            if (Mathf.Abs(_current - goal) < 0.001f) _current = goal;
        }
        _t.localScale = _baseScale * _current;
    }

    private void OnDisable()
    {
        _over = _down = false;
        _current = 1f;
        if (_t != null) _t.localScale = _baseScale;
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
    private Vector2 _basePosition;
    private float _lift;
    private Selectable _selectable;
    private Vector2 _lastPosition;
    private bool _applied;

    private void Awake()
    {
        _t = target != null ? target : transform as RectTransform;
        if (_t != null) _baseScale = _t.localScale;
        if (_t != null) _basePosition = _t.anchoredPosition;
        _selectable = GetComponent<Selectable>();
    }

    public void OnPointerEnter(PointerEventData e) => _over = true;
    public void OnPointerExit(PointerEventData e) { _over = false; _down = false; }
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) _down = true; }
    public void OnPointerUp(PointerEventData e) => _down = false;

    private void Update()
    {
        if (_t == null) return;
        // 레이아웃 배치 후의 위치를 기준으로 삼는다. 창 재배치/해상도 변경에도 옛 위치로 튀지 않는다.
        if (!_applied || (_t.anchoredPosition - _lastPosition).sqrMagnitude > .001f)
            _basePosition = _t.anchoredPosition;
        var s = UISkinSettings.Active;
        var m = s != null ? s.hover : null;
        bool on = m != null && m.enabled && UISkinSettings.MotionOn && (_selectable == null || _selectable.IsInteractable());

        float goal = 1f;
        if (on && _over) goal = _down ? m.pressScale : m.hoverScale;

        float liftGoal = on && _over && !_down ? m.liftPixels : 0f;
        if (Mathf.Approximately(_current, goal) && Mathf.Approximately(_lift, liftGoal)) return;
        float time = on ? m.time : 0f;
        if (time <= 0.0001f) { _current = goal; _lift = liftGoal; }
        else
        {
            // '바뀌는 시간' 안에 거의(98%) 도달하도록 부드럽게 따라간다.
            float k = 1f - Mathf.Exp(-4f * UISkinSettings.DeltaTime / time);
            _current = Mathf.Lerp(_current, goal, k);
            if (Mathf.Abs(_current - goal) < 0.001f) _current = goal;
            _lift = Mathf.Lerp(_lift, liftGoal, k);
            if (Mathf.Abs(_lift - liftGoal) < .01f) _lift = liftGoal;
        }
        _t.localScale = _baseScale * _current;
        _t.anchoredPosition = _basePosition + Vector2.up * _lift;
        _lastPosition = _t.anchoredPosition;
        _applied = _current != 1f || _lift != 0f;
    }

    private void OnDisable()
    {
        _over = _down = false;
        _current = 1f;
        _lift = 0f;
        if (_t != null) _t.localScale = _baseScale;
        if (_t != null && _applied) _t.anchoredPosition = _basePosition;
        _applied = false;
    }
}

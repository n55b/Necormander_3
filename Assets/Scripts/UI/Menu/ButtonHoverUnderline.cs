using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버튼(또는 그 안의 텍스트)에 마우스를 올리면 버튼 아래변에 밑줄이 차오른다.
///
/// ▶ 값 조절은 연결된 ButtonHoverUnderlineSettings 에셋에서 (이 오브젝트 인스펙터에 바로 펼쳐져 보임)
///   - 같은 에셋을 쓰는 버튼 전부에 한 번에 적용된다.
///   - Play 없이도 인스펙터의 '미리보기' 로 모양/속도를 확인할 수 있다.
///
/// [구성] Button 오브젝트에 붙인다. 자식으로 'HoverUnderline' Image 를 만들어 쓴다 (클릭 판정 없음).
///        텍스트(자식)에 마우스가 올라가도 이벤트가 버튼까지 올라오므로 그대로 동작한다.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ButtonHoverUnderline : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private const string LineName = "HoverUnderline";

    [SerializeField] private ButtonHoverUnderlineSettingsSO settings;

    [Tooltip("에디터 미리보기용 채워진 정도 (0 ~ 1). Play 중에는 마우스가 대신한다.")]
    [Range(0f, 1f)] [SerializeField] private float editorPreview;

    [SerializeField] private Image line;

    public ButtonHoverUnderlineSettingsSO Settings => settings;

    // 밑줄 구간: 방향 기준 0~1 중 [_tail, _head] 가 보인다
    private float _head, _tail, _alpha = 1f;
    private Tween _headTw, _tailTw, _alphaTw;
    private float _delayLeft;
    private bool _pendingShow;
    private bool _hovered, _selected;
    private bool _shown;
    private ButtonHoverUnderlineSettingsSO _subscribed;

#if UNITY_EDITOR
    private bool _editorAnimating;
    private double _editorLastTime;
#endif

    private struct Tween
    {
        public bool active;
        public float from, to, elapsed, duration;
        public AnimationCurve curve;

        public static Tween Start(float from, float to, float duration, AnimationCurve curve)
        {
            return new Tween { active = !Mathf.Approximately(from, to), from = from, to = to, duration = Mathf.Max(0.0001f, duration), curve = curve };
        }

        /// <returns>현재 값</returns>
        public float Step(float dt)
        {
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / duration);
            float k = curve != null && curve.length > 0 ? curve.Evaluate(t) : t;
            if (t >= 1f) { active = false; return to; }
            return Mathf.LerpUnclamped(from, to, k);
        }
    }

    // ───────────────────────── 수명 ─────────────────────────

    private void OnEnable()
    {
        EnsureLine();
        Subscribe();
        ResetState();
        if (!Application.isPlaying) _head = editorPreview;
        Apply();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        Unsubscribe();
        _hovered = _selected = false;
        ResetState();
        Apply();
    }

    private void Subscribe()
    {
        if (_subscribed == settings) return;
        Unsubscribe();
        _subscribed = settings;
        if (_subscribed != null) _subscribed.Changed += Refresh;
    }

    private void Unsubscribe()
    {
        if (_subscribed != null) _subscribed.Changed -= Refresh;
        _subscribed = null;
    }

    private void ResetState()
    {
        _head = _tail = 0f;
        _alpha = 1f;
        _headTw = _tailTw = _alphaTw = default;
        _pendingShow = false;
        _shown = false;
    }

    private void OnRectTransformDimensionsChange() => Apply();

    /// <summary>설정이 바뀌었을 때 즉시 다시 그린다.</summary>
    public void Refresh()
    {
        if (this == null) return;
        EnsureLine();
        Subscribe();
        if (!Application.isPlaying && !IsEditorAnimating) { _head = editorPreview; _tail = 0f; _alpha = 1f; }
        Apply();
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    // ───────────────────────── 입력 ─────────────────────────

    public void OnPointerEnter(PointerEventData e) { _hovered = true; UpdateWanted(); }
    public void OnPointerExit(PointerEventData e) { _hovered = false; UpdateWanted(); }
    public void OnSelect(BaseEventData e) { _selected = true; UpdateWanted(); }
    public void OnDeselect(BaseEventData e) { _selected = false; UpdateWanted(); }

    private void UpdateWanted()
    {
        if (!Application.isPlaying || settings == null) return;
        bool want = _hovered || (_selected && settings.showOnSelect);
        if (want == _shown) return;
        if (want) Show(); else Hide();
    }

    private void Show()
    {
        _shown = true;
        var s = settings;
        // 사라지는 중(빠져나가기/흐려지기)이었다면 처음부터 다시 차오름
        if (_tail > 0.0001f || _alphaTw.active || _alpha < 0.999f)
        {
            _tail = 0f; _head = 0f; _alpha = 1f;
            _tailTw = default; _alphaTw = default;
        }
        _headTw = default;
        if (s.showDelay > 0f && _head <= 0.0001f) { _pendingShow = true; _delayLeft = s.showDelay; }
        else StartFill();
    }

    private void StartFill()
    {
        _pendingShow = false;
        var s = settings;
        _headTw = Tween.Start(_head, 1f, s.showDuration * (1f - _head), s.showCurve);
    }

    private void Hide()
    {
        _shown = false;
        _pendingShow = false;
        var s = settings;
        var style = s.hideStyle;
        if (style == ButtonHoverUnderlineSettingsSO.HideStyle.SweepOut && s.direction == ButtonHoverUnderlineSettingsSO.FillDirection.CenterOut)
            style = ButtonHoverUnderlineSettingsSO.HideStyle.Rewind; // 가운데→양옆은 빠져나갈 방향이 없어 되감기로

        switch (style)
        {
            case ButtonHoverUnderlineSettingsSO.HideStyle.Rewind:
                _headTw = Tween.Start(_head, 0f, s.hideDuration * _head, s.hideCurve);
                break;
            case ButtonHoverUnderlineSettingsSO.HideStyle.SweepOut:
                // 머리는 끝까지 마저 가고, 꼬리가 뒤따라가며 지운다
                _headTw = Tween.Start(_head, 1f, s.hideDuration * (1f - _head), s.hideCurve);
                _tailTw = Tween.Start(_tail, 1f, s.hideDuration * (1f - _tail), s.hideCurve);
                break;
            case ButtonHoverUnderlineSettingsSO.HideStyle.Fade:
                _headTw = default;
                _alphaTw = Tween.Start(_alpha, 0f, s.hideDuration * _alpha, s.hideCurve);
                break;
            default:
                ResetState();
                break;
        }
    }

    // ───────────────────────── 진행 ─────────────────────────

    private void Update()
    {
        if (!Application.isPlaying) return;
        if (settings == null) return;
        Subscribe();
        float dt = settings.ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
        if (Tick(dt)) Apply();
    }

    /// <returns>값이 바뀌었으면 true</returns>
    private bool Tick(float dt)
    {
        bool changed = false;
        if (_pendingShow)
        {
            _delayLeft -= dt;
            if (_delayLeft <= 0f) StartFill();
        }
        if (_headTw.active) { _head = _headTw.Step(dt); changed = true; }
        if (_tailTw.active) { _tail = _tailTw.Step(dt); changed = true; }
        if (_alphaTw.active) { _alpha = _alphaTw.Step(dt); changed = true; }

        // 다 사라졌으면 초기 상태로
        if (!_shown && !_headTw.active && !_tailTw.active && !_alphaTw.active && (_tail >= 0.9999f || _alpha <= 0.0001f))
        {
            ResetState();
            changed = true;
        }
        return changed;
    }

    // ───────────────────────── 그리기 ─────────────────────────

    private void EnsureLine()
    {
        if (line != null) return;
        var existing = transform.Find(LineName);
        if (existing != null) line = existing.GetComponent<Image>();
        if (line != null) return;

        var go = new GameObject(LineName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        line = go.GetComponent<Image>();
        line.raycastTarget = false;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>현재 상태를 밑줄 Image 에 반영.</summary>
    private void Apply()
    {
        if (line == null) return;
        if (settings == null) { line.enabled = false; return; }
        var s = settings;
        var btn = (RectTransform)transform;
        Rect r = btn.rect;

        float baseLen = r.width * s.lengthRatio;
        float start = (r.width - baseLen) * s.align - s.extraLength;   // 버튼 왼쪽 끝 기준
        float full = Mathf.Max(0f, baseLen + s.extraLength * 2f);

        float a, b;
        switch (s.direction)
        {
            case ButtonHoverUnderlineSettingsSO.FillDirection.RightToLeft: a = 1f - _head; b = 1f - _tail; break;
            case ButtonHoverUnderlineSettingsSO.FillDirection.CenterOut: a = 0.5f - _head * 0.5f; b = 0.5f + _head * 0.5f; break;
            default: a = _tail; b = _head; break;
        }
        float len = Mathf.Max(0f, (b - a) * full);

        var lt = line.rectTransform;
        lt.anchorMin = lt.anchorMax = Vector2.zero;   // 버튼 왼쪽 아래 모서리 기준
        lt.pivot = new Vector2(0f, 0.5f);
        lt.anchoredPosition = new Vector2(start + a * full, s.offsetY);
        lt.sizeDelta = new Vector2(len, s.thickness);
        lt.localRotation = Quaternion.identity;
        lt.localScale = Vector3.one;

        if (line.sprite != s.sprite) line.sprite = s.sprite;
        line.type = Image.Type.Simple;
        line.raycastTarget = false;
        Color c = s.color;
        c.a *= _alpha;
        line.color = c;
        line.enabled = len > 0.01f && c.a > 0.001f;
    }

    // ───────────────────────── 에디터 미리보기 ─────────────────────────

    private bool IsEditorAnimating
    {
        get
        {
#if UNITY_EDITOR
            return _editorAnimating;
#else
            return false;
#endif
        }
    }

#if UNITY_EDITOR
    /// <summary>에디터(Play 아님)에서 실제 속도로 나타나기/사라지기를 재생해 본다.</summary>
    public void EditorPlay(bool show)
    {
        if (settings == null) return;
        EnsureLine();
        if (Application.isPlaying) { _hovered = show; UpdateWanted(); return; }
        if (show) { ResetState(); Show(); }
        else
        {
            if (!_shown) { ResetState(); _head = 1f; _shown = true; }
            Hide();
        }
        _editorAnimating = true;
        _editorLastTime = UnityEditor.EditorApplication.timeSinceStartup;
        Apply();
    }

    private int _lastDirtyCount = -1;

    private void EditorTick()
    {
        if (this == null || Application.isPlaying) return;

        if (_editorAnimating)
        {
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            float dt = (float)(now - _editorLastTime);
            _editorLastTime = now;
            Tick(dt);
            Apply();
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
            if (!_pendingShow && !_headTw.active && !_tailTw.active && !_alphaTw.active)
            {
                _editorAnimating = false;
                editorPreview = _head * (_tail > 0f ? 0f : 1f);
            }
            return;
        }

        int dirty = UnityEditor.EditorUtility.GetDirtyCount(this) + (settings != null ? UnityEditor.EditorUtility.GetDirtyCount(settings) : 0);
        if (dirty == _lastDirtyCount) return;
        _lastDirtyCount = dirty;
        Refresh();
    }

    private void OnValidate()
    {
        if (isActiveAndEnabled) UnityEditor.EditorApplication.delayCall += () => { if (this != null) { _editorAnimating = false; Refresh(); } };
    }
#endif
}

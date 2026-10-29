using UnityEngine;

/// <summary>방 출입구의 비석 외형을 표시한다. 충돌과 이동은 기존 문이 담당한다.</summary>
[ExecuteAlways]
public sealed class PortalDoorVisual : MonoBehaviour
{
    [Header("포탈 비석")]
    [SerializeField] private SpriteRenderer firstPillar;
    [SerializeField] private SpriteRenderer transitionOverlay;
    [SerializeField] private Sprite inactiveSprite;
    [SerializeField] private Sprite activeSprite;
    [InspectorName("비석 높이 보정")]
    [Tooltip("문 위치를 기준으로 비석을 위아래로 옮긴다.")]
    [SerializeField] private float pillarHeight = -1f;
    [InspectorName("비석 크기")]
    [SerializeField, Min(0.01f)] private float pillarScale = 1f;
    [InspectorName("켜짐·꺼짐 전환 시간")]
    [Tooltip("이전 그림이 새 그림 위에서 사라지는 시간(초). 0이면 즉시 전환한다.")]
    [SerializeField, Min(0f)] private float fadeDuration = .22f;

    private bool _open = true;
    private bool _needsRefresh;
    private bool _transitioning;
    private float _fadeElapsed;
    private float _fadeStartAlpha = 1f;
    private float _fadeTime;
    private Color _overlayColor = Color.white;

    private void OnEnable() { _transitioning = false; if (transitionOverlay != null) transitionOverlay.enabled = false; Refresh(); }
    private void OnDisable() { _transitioning = false; if (transitionOverlay != null) transitionOverlay.enabled = false; }
    private void OnValidate() => _needsRefresh = true;

    public void SetOpen(bool open)
    {
        bool changed = _open != open;
        if (changed && Application.isPlaying && isActiveAndEnabled && fadeDuration > 0f && firstPillar != null && transitionOverlay != null && firstPillar.sprite != null)
        {
            Sprite target = open ? activeSprite : inactiveSprite;
            float startAlpha = 1f;
            if (_transitioning && transitionOverlay.enabled && transitionOverlay.sprite == target && _overlayColor.a > .0001f)
                startAlpha = 1f - Mathf.Clamp01(transitionOverlay.color.a / _overlayColor.a);
            transitionOverlay.sprite = firstPillar.sprite;
            _overlayColor = firstPillar.color;
            var startColor = _overlayColor;
            startColor.a *= startAlpha;
            transitionOverlay.color = startColor;
            transitionOverlay.enabled = startAlpha > .001f;
            _fadeElapsed = 0f;
            _fadeStartAlpha = startAlpha;
            _fadeTime = fadeDuration * startAlpha;
            _transitioning = transitionOverlay.enabled;
        }
        else if (changed)
        {
            _transitioning = false;
            if (transitionOverlay != null) transitionOverlay.enabled = false;
        }
        _open = open;
        Refresh();
    }

    private void Refresh()
    {
        if (firstPillar == null) return;
        // 문이 상하좌우로 회전해도 비석은 중심에 똑바로 세운다.
        firstPillar.transform.position = transform.position + Vector3.up * pillarHeight;
        firstPillar.transform.rotation = Quaternion.identity;
        firstPillar.transform.localScale = Vector3.one * pillarScale;
        firstPillar.sprite = _open ? activeSprite : inactiveSprite;
        if (transitionOverlay != null)
        {
            transitionOverlay.transform.SetPositionAndRotation(firstPillar.transform.position, firstPillar.transform.rotation);
            transitionOverlay.transform.localScale = firstPillar.transform.localScale;
            transitionOverlay.sortingLayerID = firstPillar.sortingLayerID;
            transitionOverlay.sortingOrder = firstPillar.sortingOrder + 1;
        }
    }

    private void Update()
    {
        if (_needsRefresh) { _needsRefresh = false; Refresh(); }
        if (!_transitioning || transitionOverlay == null) return;
        _fadeElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_fadeElapsed / Mathf.Max(.001f, _fadeTime));
        var color = _overlayColor;
        color.a *= _fadeStartAlpha * (1f - Mathf.SmoothStep(0f, 1f, progress));
        transitionOverlay.color = color;
        if (progress >= 1f) { transitionOverlay.enabled = false; _transitioning = false; }
    }
}

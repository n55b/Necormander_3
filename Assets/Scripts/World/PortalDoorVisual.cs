using UnityEngine;

/// <summary>방 출입구의 비석 외형을 표시한다. 충돌과 이동은 기존 문이 담당한다.</summary>
[ExecuteAlways]
public sealed class PortalDoorVisual : MonoBehaviour
{
    [Header("포탈 비석")]
    [SerializeField] private SpriteRenderer firstPillar;
    [SerializeField, InspectorName("문 테두리 연출")] private AmbientOutlinePulse edgePulse;
    [SerializeField, InspectorName("비석 윤곽 스프라이트")]
    [Tooltip("비석과 같은 그림을 사용하며 전용 재질이 불투명한 부분의 가장자리만 표시합니다.")]
    private SpriteRenderer edgeOutline;
    [SerializeField] private SpriteRenderer transitionOverlay;
    [SerializeField] private Sprite inactiveSprite;
    [SerializeField] private Sprite activeSprite;
    [InspectorName("비석 높이 보정")]
    [Tooltip("문 위치를 기준으로 비석을 위아래로 옮긴다.")]
    [SerializeField] private float pillarHeight = -1f;
    [InspectorName("비석 크기")]
    [SerializeField, Min(0.01f)] private float pillarScale = 1f;
    [InspectorName("비석을 도트 격자에 맞춤")]
    [Tooltip("반 픽셀 피벗으로 인해 카메라 이동 시 비석 끝이 흔들리는 것을 줄입니다. 비석 위치만 최대 반 도트 보정합니다.")]
    [SerializeField] private bool snapPillarToPixelGrid = true;
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
    private MaterialPropertyBlock _edgeProperties;
    private static readonly int SpriteUVRectId = Shader.PropertyToID("_SpriteUVRect");

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
        if (edgePulse != null) edgePulse.SetOpen(open);
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
        if (snapPillarToPixelGrid && firstPillar.sprite != null)
        {
            var sprite = firstPillar.sprite;
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            Vector3 scale = firstPillar.transform.lossyScale;
            Vector3 position = firstPillar.transform.position;
            // Sprite pivot이 반 픽셀인 경우에도 그림의 좌하단이 월드 픽셀 격자에 놓이게 한다.
            float offsetX = sprite.pivot.x * Mathf.Abs(scale.x) / ppu;
            float offsetY = sprite.pivot.y * Mathf.Abs(scale.y) / ppu;
            position.x = Mathf.Round((position.x - offsetX) * ppu) / ppu + offsetX;
            position.y = Mathf.Round((position.y - offsetY) * ppu) / ppu + offsetY;
            firstPillar.transform.position = position;
        }
        if (transitionOverlay != null)
        {
            transitionOverlay.transform.SetPositionAndRotation(firstPillar.transform.position, firstPillar.transform.rotation);
            transitionOverlay.transform.localScale = firstPillar.transform.localScale;
            transitionOverlay.sortingLayerID = firstPillar.sortingLayerID;
            transitionOverlay.sortingOrder = firstPillar.sortingOrder + 1;
        }
        if (edgeOutline != null)
        {
            // 비석의 켜짐/꺼짐 그림·피벗·도트 격자 보정과 항상 동일하게 맞춘다.
            edgeOutline.sprite = firstPillar.sprite;
            edgeOutline.transform.SetPositionAndRotation(firstPillar.transform.position, firstPillar.transform.rotation);
            edgeOutline.sortingLayerID = firstPillar.sortingLayerID;
            edgeOutline.sortingOrder = firstPillar.sortingOrder + 2;
            edgeOutline.flipX = firstPillar.flipX;
            edgeOutline.flipY = firstPillar.flipY;
            if (edgeOutline.sprite != null)
            {
                var sprite = edgeOutline.sprite;
                Rect rect = sprite.textureRect;
                var texture = sprite.texture;
                _edgeProperties ??= new MaterialPropertyBlock();
                edgeOutline.GetPropertyBlock(_edgeProperties);
                _edgeProperties.SetVector(SpriteUVRectId, new Vector4(rect.xMin / texture.width, rect.yMin / texture.height,
                    rect.xMax / texture.width, rect.yMax / texture.height));
                edgeOutline.SetPropertyBlock(_edgeProperties);
            }
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

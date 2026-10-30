using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 타이틀 배경: 화면에 '수정구슬'(원) 안으로 타이틀 그림이 보이고, 반투명 원 2겹이 겹쳐 있다.
/// 마우스 방향에 따라 구슬 / 그림 / 원1 / 원2 가 서로 다른 양만큼 움직여(패럴랙스) 둘러보는 느낌을 준다.
///
/// ▶ 값 조절은 연결된 TitleCrystalBallSettings 에셋에서 (이 오브젝트 인스펙터에 바로 펼쳐져 보임)
///   - Play 를 누르지 않아도 Game 뷰에 바로 반영된다.
///   - 에셋이라서 Play 중에 바꾼 값도 Play 를 꺼도 남는다.
///
/// [구성] Canvas 의 첫 번째 자식(가장 뒤)에 화면 전체로 늘린 RawImage + 이 컴포넌트.
///        RawImage 머티리얼 = UI/TitleCrystalBall 셰이더 머티리얼.
///        머티리얼 에셋은 건드리지 않고, IMaterialModifier 로 런타임 복사본에 값을 넣는다.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RawImage))]
public class TitleCrystalBall : MonoBehaviour, IMaterialModifier
{
    [SerializeField] private TitleCrystalBallSettingsSO settings;

    [Tooltip("에디터 미리보기용 둘러보기 방향 (-1 ~ 1). Play 중에는 마우스가 대신한다.")]
    [SerializeField] private Vector2 editorPreviewLook;

    public TitleCrystalBallSettingsSO Settings => settings;

    private RawImage _image;
    private Material _mat;
    private Vector2 _look;          // -1~1 (둘러보기 방향/세기)
    private Vector2 _lookVelocity;
    private Vector2 _cover = Vector2.one;
    private TitleCrystalBallSettingsSO _subscribed;

    private static readonly int SpriteRectId = Shader.PropertyToID("_SpriteRect");
    private static readonly int ArtPxId = Shader.PropertyToID("_ArtPx");
    private static readonly int CoverId = Shader.PropertyToID("_Cover");
    private static readonly int OrbId = Shader.PropertyToID("_Orb");
    private static readonly int PanId = Shader.PropertyToID("_Pan");
    private static readonly int ImageTintId = Shader.PropertyToID("_ImageTint");
    private static readonly int BgColorId = Shader.PropertyToID("_BgColor");
    private static readonly int OutsideId = Shader.PropertyToID("_Outside");
    private static readonly int Layer1Id = Shader.PropertyToID("_Layer1");
    private static readonly int Layer1ColorId = Shader.PropertyToID("_Layer1Color");
    private static readonly int Layer2Id = Shader.PropertyToID("_Layer2");
    private static readonly int Layer2ColorId = Shader.PropertyToID("_Layer2Color");

    private void OnEnable()
    {
        _image = GetComponent<RawImage>();
        if (Application.isPlaying) _image.raycastTarget = false;
        Subscribe();
        _look = Application.isPlaying ? Vector2.zero : editorPreviewLook;
        _image.SetMaterialDirty();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
#endif
    }

#if UNITY_EDITOR
    private int _lastDirtyCount = -1;

    /// <summary>
    /// 에디터(Play 아님)에서 설정 에셋/이 컴포넌트가 어떤 경로로든 바뀌면(인스펙터, Project 창, 실행 취소 등)
    /// 바로 다시 그린다.
    /// </summary>
    private void EditorTick()
    {
        if (this == null || Application.isPlaying) return;
        int dirty = UnityEditor.EditorUtility.GetDirtyCount(this) + (settings != null ? UnityEditor.EditorUtility.GetDirtyCount(settings) : 0);
        if (dirty == _lastDirtyCount) return;
        _lastDirtyCount = dirty;
        Refresh();
    }
#endif

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        Unsubscribe();
        if (_image != null) _image.SetMaterialDirty();
    }

    private void OnDestroy()
    {
        if (_mat == null) return;
        if (Application.isPlaying) Destroy(_mat); else DestroyImmediate(_mat);
        _mat = null;
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

    // UI 가 그릴 머티리얼을 우리 복사본으로 바꿔 끼운다 (머티리얼 에셋/씬은 수정되지 않음)
    Material IMaterialModifier.GetModifiedMaterial(Material baseMaterial)
    {
        if (!isActiveAndEnabled || baseMaterial == null || baseMaterial == Graphic.defaultGraphicMaterial)
            return baseMaterial;
        if (_mat == null || _mat.shader != baseMaterial.shader)
        {
            if (_mat != null) DestroyImmediate(_mat);
            _mat = new Material(baseMaterial) { name = baseMaterial.name + " (runtime)", hideFlags = HideFlags.HideAndDontSave };
        }
        Apply();
        return _mat;
    }

    /// <summary>설정이 바뀌었을 때 즉시 다시 그린다.</summary>
    public void Refresh()
    {
        if (_image == null) _image = GetComponent<RawImage>();
        Subscribe();
        if (!Application.isPlaying) _look = editorPreviewLook;
        Apply();
        _image.SetMaterialDirty();
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    private void Update()
    {
        if (settings == null) return;
        Subscribe();

        if (Application.isPlaying)
        {
            Vector2 target = Vector2.zero;
            Vector2 art = ArtSize();
            Vector2 restPx = new Vector2(settings.orbCenter.x * art.x, settings.orbCenter.y * art.y);
            if (TryGetMouseArtPx(art, out Vector2 mousePx))
                target = Vector2.ClampMagnitude((mousePx - restPx) / (settings.lookRadius * art.y), 1f);

            float t = Time.unscaledTime * settings.idleBobSpeed;
            target += new Vector2(Mathf.Sin(t * 0.7f), 0.0f) * settings.idleBob;
            target = Vector2.ClampMagnitude(target, 1f);

            _look = Vector2.SmoothDamp(_look, target, ref _lookVelocity, settings.smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        }
        else
        {
            _look = Vector2.ClampMagnitude(editorPreviewLook, 1f);
        }

        Apply();
    }

    private Vector2 ArtSize()
    {
        return settings != null && settings.titleSprite != null ? settings.titleSprite.textureRect.size : new Vector2(480, 270);
    }

    /// <summary>현재 설정 + 둘러보기 값을 머티리얼에 넣는다.</summary>
    private void Apply()
    {
        if (_mat == null || settings == null || settings.titleSprite == null || _image == null) return;
        var s = settings;

        // 그림
        Texture2D tex = s.titleSprite.texture;
        if (_image.texture != tex) _image.texture = tex;
        Rect r = s.titleSprite.textureRect;
        _mat.SetVector(SpriteRectId, new Vector4(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height));
        _mat.SetVector(ArtPxId, new Vector4(r.width, r.height, 0, 0));

        Vector2 art = r.size;
        float h = art.y;   // 거리 단위 = 그림 높이
        Rect rect = _image.rectTransform.rect;
        float screenAspect = rect.height > 0 ? rect.width / rect.height : 16f / 9f;
        float artAspect = art.x / art.y;
        // 그림이 화면을 꽉 채우도록(넘치는 쪽은 잘라냄) 화면 uv → 그림 uv 배율
        _cover = screenAspect > artAspect
            ? new Vector2(1f, artAspect / screenAspect)
            : new Vector2(screenAspect / artAspect, 1f);
        _mat.SetVector(CoverId, new Vector4(_cover.x, _cover.y, 0, 0));

        Color tint = s.imageTint * s.imageBrightness;
        _mat.SetColor(ImageTintId, new Color(tint.r, tint.g, tint.b, 1f));
        _mat.SetColor(BgColorId, s.backgroundColor);
        _mat.SetVector(OutsideId, new Vector4(s.outsideBrightness, s.outsideFade * h, s.outsideFadeCurve, 0));

        // 각 층을 서로 다른 양만큼 이동. 픽셀 격자에 맞춰 반올림 → 도트가 흔들리지 않음
        Vector2 restPx = new Vector2(s.orbCenter.x * art.x, s.orbCenter.y * art.y);
        Vector2 orbPx = Snap(restPx + _look * (s.orbMove * h));
        Vector2 pan = Snap(_look * (s.imagePan * h));
        float orbR = s.orbRadius * h;

        _mat.SetVector(OrbId, new Vector4(orbPx.x, orbPx.y, orbR, 0));
        _mat.SetVector(PanId, new Vector4(pan.x, pan.y, 0, 0));
        _mat.SetVector(Layer1Id, LayerVector(s.layer1, orbPx, orbR, h));
        _mat.SetColor(Layer1ColorId, s.layer1.color);
        _mat.SetVector(Layer2Id, LayerVector(s.layer2, orbPx, orbR, h));
        _mat.SetColor(Layer2ColorId, s.layer2.color);
    }

    private Vector4 LayerVector(TitleCrystalBallSettingsSO.GlassLayer l, Vector2 orbPx, float orbR, float h)
    {
        Vector2 c = Snap(orbPx + l.offset * orbR + _look * (l.parallax * h));
        return new Vector4(c.x, c.y, l.radius * orbR, l.enabled ? l.alpha : 0f);
    }

    private static Vector2 Snap(Vector2 v) => new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));

    /// <summary>마우스 위치를 그림 픽셀 좌표로.</summary>
    private bool TryGetMouseArtPx(Vector2 art, out Vector2 artPx)
    {
        artPx = default;
        if (Mouse.current == null) return false;

        var canvas = _image.canvas;
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
        RectTransform rt = _image.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Mouse.current.position.ReadValue(), cam, out Vector2 local))
            return false;

        Rect rect = rt.rect;
        Vector2 screenUV = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
        Vector2 artUV = (screenUV - new Vector2(0.5f, 0.5f)) * _cover + new Vector2(0.5f, 0.5f);
        artPx = new Vector2(artUV.x * art.x, artUV.y * art.y);
        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (isActiveAndEnabled) UnityEditor.EditorApplication.delayCall += () => { if (this != null) Refresh(); };
    }
#endif
}

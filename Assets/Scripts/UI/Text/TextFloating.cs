using TMPro;
using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(1100)]
public class TextFloating : MonoBehaviour, UnityEngine.UI.IMaterialModifier
{
    private TextMeshProUGUI textMesh;

    private Canvas parentCanvas;
    private RectTransform rectTransform;
    private RectTransform parentRect;

    // Text Location
    private Vector3 spawnWorldPosition;
    private Vector3 offSet;
    private CanvasGroup opacity;
    private PixelText pixelText;

    [Header("텍스트 설정")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float fadeTime;
    [SerializeField] private float displaytime;

    [Header("치명타")]
    [Tooltip("치명타 데미지 숫자 색(데미지 타입 색을 덮어씀).")]
    [SerializeField] private Color critColor = new Color(1f, 0.9f, 0.15f);
    [SerializeField, Min(8)] private int normalPixels = 10;
    [SerializeField, Min(8)] private int criticalPixels = 15;

    [Min(0f), SerializeField] private float horizontalJitter = .5f;

    private float timer;
    private CombatTextLibrary.Style style;
    private int lane;
    private Vector2 screenOffset;
    internal bool IsDamageLabel { get; private set; }
    public Material GetModifiedMaterial(Material source)
    {
        var manager = FloatingTextManager.Instance;
        return IsDamageLabel && manager != null ? manager.InkFor(source) : source;
    }
    private TMPTextEffectPlayer effectPlayer;
    private static readonly List<TextEffectRange> tempEffectRanges = new List<TextEffectRange>();

    /// <summary>풀 안에 있는지. ReturnToPool 이 같은 객체를 두 번 넣지 않게 막는다.</summary>
    internal bool InPool { get; set; }

    /// <summary>알림 텍스트 줄 쌓기용 대상 키. 데미지 텍스트·풀에 있는 텍스트는 null.</summary>
    internal Transform NoticeKey { get; set; }

    /// <summary>현재 세로 오프셋(월드 단위). 줄 쌓기에서 위아래 간격을 잴 때 쓴다.</summary>
    public float OffsetY => offSet.y;

    private void Awake() => Bind();

    private void Bind()
    {
        if (opacity != null && effectPlayer != null) return;
        textMesh = GetComponent<TextMeshProUGUI>();
        pixelText = PixelText.Configure(textMesh, normalPixels);
        rectTransform = GetComponent<RectTransform>();
        opacity = GetComponent<CanvasGroup>();
        if (opacity == null) opacity = gameObject.AddComponent<CanvasGroup>();
        CacheParent();
        effectPlayer = GetComponent<TMPTextEffectPlayer>();
        if (effectPlayer == null) effectPlayer = gameObject.AddComponent<TMPTextEffectPlayer>();
    }

    private void OnTransformParentChanged() => CacheParent();

    internal void PrepareDamageMesh()
    {
        // TMP needs an active canvas to allocate its glyph and motion buffers.
        gameObject.SetActive(true);
        Bind();
        SetUpAt("8888", Color.white, Vector3.zero, false, 1f, "Hit", damageLabel: true);
        textMesh.enabled = true;
        pixelText.PrepareForRender();
        textMesh.ForceMeshUpdate();
        gameObject.SetActive(false);
    }

    private void CacheParent()
    {
        parentRect = transform.parent as RectTransform;
        parentCanvas = GetComponentInParent<Canvas>();
    }

    public void SetUp(string _text, Color _color, Transform _target, bool isCritical = false, float scaleMultiplier = 1f, string preset = null)
        => SetUpAt(_text, _color, _target != null ? _target.position : transform.position, isCritical, scaleMultiplier, preset);

    internal void SetUpAt(string _text, Color _color, Vector3 position, bool isCritical, float scaleMultiplier, string preset,
        CombatTextLibrary.Style combatStyle = null, int sequence = 0, bool damageLabel = false)
    {
        Bind();
        style = combatStyle;
        if (IsDamageLabel != damageLabel) textMesh.SetMaterialDirty();
        IsDamageLabel = damageLabel;
        effectPlayer.padCombatInk = damageLabel && FloatingTextManager.Instance != null && FloatingTextManager.Instance.HasInk;
        lane = sequence;
        timer = style != null ? Mathf.Max(.1f, style.lifetime) : displaytime;
        screenOffset = style != null ? style.Offset(0, lane, UISkinSettings.MotionOn) : Vector2.zero;

        string parsedText = TextEffectParser.Parse(_text, tempEffectRanges);
        textMesh.text = parsedText;
        textMesh.color = _color;
        NoticeKey = null;
        spawnWorldPosition = position;

        offSet = style != null ? Vector3.zero : new Vector3(PolishFeedback.VisualRange(-horizontalJitter, horizontalJitter), 0);
        opacity.alpha = 1f;

        // Set initial position immediately to avoid flashes at incorrect world coordinates.
        UpdatePosition();
        gameObject.SetActive(true);

        // Reset Transform
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        int pixels = style != null ? style.fontPixels : isCritical ? criticalPixels : normalPixels;
        pixelText.SetLogicalSize(Mathf.Max(8, Mathf.RoundToInt(scaleMultiplier * pixels)));
        if (isCritical) textMesh.color = critColor;
        if (tempEffectRanges.Count == 0) effectPlayer.PlayPreset(preset ?? (isCritical ? "Critical" : "Hit"));
        else effectPlayer.ApplyEffects(tempEffectRanges);
    }

    /// <summary>
    /// SetUp 직후 호출. 랜덤 x 흔들림 대신 지정한 오프셋에서 시작한다(알림 텍스트를 줄 맞춰 쌓을 때).
    /// </summary>
    public void OverrideOffset(Vector3 offset)
    {
        offSet = offset;
        UpdatePosition();
    }

    /// <summary>세로 오프셋을 y 까지 끌어올린다(새 알림 텍스트가 아래에 뜰 때 기존 텍스트를 밀어 올림).</summary>
    public void RaiseOffsetY(float y)
    {
        if (y <= offSet.y) return;
        offSet.y = y;
        UpdatePosition();
    }

    /// <summary>남은 표시 시간을 줄여 곧바로 사라지게 한다(한 대상에 줄이 너무 많이 쌓였을 때).</summary>
    public void ExpireSoon()
    {
        timer = Mathf.Min(timer, fadeTime * 0.5f);
    }



    private void Update()
    {
        // 생성 시점의 월드 위치를 기준으로 떠오른다. 대상의 수명과 독립적으로 표시 시간을 유지한다.
        timer -= Time.deltaTime;

        // Fade Out
        if (style != null)
        {
            float t = Mathf.Clamp01(1 - timer / Mathf.Max(.1f, style.lifetime));
            screenOffset = style.Offset(t, lane, UISkinSettings.MotionOn);
            opacity.alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(style.fadeFrom, 1, t));
        }
        else
        {
            opacity.alpha = Mathf.Clamp01(timer / Mathf.Max(.001f, fadeTime));
            offSet.y += moveSpeed * Time.deltaTime;
        }

        if (timer <= 0)
            gameObject.SetActive(false);
    }

    private void OnEnable() => Canvas.preWillRenderCanvases += RefreshDisplay;

    private void RefreshDisplay()
    {
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (rectTransform == null) return;
        bool projected = PixelViewport.TryWorldToGamePoint(spawnWorldPosition + offSet, out var point);
        point += screenOffset;
        var pixel = (Vector2)point;
        if (projected && PixelViewport.TryGamePointToCanvas(pixel, parentCanvas, parentRect, out Vector2 local))
        {
            if (rectTransform.localPosition != (Vector3)local) rectTransform.localPosition = local;
            textMesh.enabled = true;
        }
        else textMesh.enabled = false;
    }

    private void OnDisable()
    {
        Canvas.preWillRenderCanvases -= RefreshDisplay;
        // 씬 언로드 중에는 매니저가 먼저 사라질 수 있다.
        var manager = FloatingTextManager.Instance;
        if (manager != null) manager.ReturnToPool(this);
    }
}

using DG.Tweening;
using TMPro;
using UnityEngine;
using System.Collections.Generic;

public class TextFloating : MonoBehaviour
{
    private TextMeshProUGUI textMesh;

    private Camera cam;
    private Canvas parentCanvas;
    private RectTransform rectTransform;

    // Text Location
    private Transform target;
    private Vector3 spawnWorldPosition;
    private Vector3 offSet;

    [Header("[ Text Setting ]")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float fadeTime;
    [SerializeField] private float displaytime;
    [SerializeField] private float punchDuration;
    [SerializeField] private float CiriPunchDuration;
    private float timer;
    private TMPTextEffectPlayer effectPlayer;
    private static readonly List<TextEffectRange> tempEffectRanges = new List<TextEffectRange>();

    /// <summary>풀 안에 있는지. ReturnToPool 이 같은 객체를 두 번 넣지 않게 막는다.</summary>
    internal bool InPool { get; set; }

    /// <summary>알림 텍스트 줄 쌓기용 대상 키. 데미지 텍스트·풀에 있는 텍스트는 null.</summary>
    internal Transform NoticeKey { get; set; }

    /// <summary>현재 세로 오프셋(월드 단위). 줄 쌓기에서 위아래 간격을 잴 때 쓴다.</summary>
    public float OffsetY => offSet.y;

    private void Awake()
    {
        textMesh = GetComponent<TextMeshProUGUI>();
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        cam = parentCanvas != null ? parentCanvas.worldCamera : Camera.main;
        if (cam == null) cam = Camera.main;
        effectPlayer = GetComponent<TMPTextEffectPlayer>();
        if (effectPlayer == null) effectPlayer = gameObject.AddComponent<TMPTextEffectPlayer>();
    }

    public void SetUp(string _text, Color _color, Transform _target, bool isCritical = false, float scaleMultiplier = 1f)
    {
        DOTween.Kill(textMesh); // Kill previous tween
        DOTween.Kill(rectTransform);

        timer = displaytime;

        if (textMesh == null)
            textMesh = GetComponent<TextMeshProUGUI>();

        string parsedText = TextEffectParser.Parse(_text, tempEffectRanges);
        textMesh.text = parsedText;
        textMesh.color = _color;
        target = _target;
        NoticeKey = null;
        spawnWorldPosition = _target != null ? _target.position : transform.position;

        offSet = new Vector3(Random.Range(-0.5f, 0.5f), 0);

        // Set initial position immediately to avoid flashes at incorrect world coordinates.
        UpdatePosition();
        gameObject.SetActive(true);

        // Reset Transform
        rectTransform.localScale = Vector3.one * scaleMultiplier;
        rectTransform.localRotation = Quaternion.identity;

        if (isCritical)
        {
            // 1. Critical Scale Base (scaleMultiplier와 함께 적용)
            rectTransform.localScale = Vector3.one * 1.5f * scaleMultiplier;

            // 2. Powerful Animation Combo
            // Scale Punch ("Pop" effect)
            rectTransform.DOPunchScale(Vector3.one * CiriPunchDuration, 0.3f, 10, 1);
            // Rotation Shake ("Impact" effect)
            rectTransform.DOShakeRotation(0.3f, 30f, 20, 90f);

            // 3. Visuals (Keep it Black but Thick)
            textMesh.outlineColor = Color.black;
            textMesh.outlineWidth = 0.3f; // Thicker outline for emphasis
        }
        else
        {
            // Reset Visuals
            textMesh.outlineColor = Color.black;
            textMesh.outlineWidth = 0.2f; // Default thickness

            // Normal Punch
            rectTransform.DOPunchPosition(new Vector3(1f, 1f, 1f), 0.5f, 10, 1);
            rectTransform.DOPunchScale(Vector3.one * punchDuration, 0.25f, 8, 0.6f);
        }
        effectPlayer.ApplyEffects(tempEffectRanges);
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
        // 생성 위치(spawnWorldPosition)에 고정돼 떠오르므로 대상이 파괴돼도 끝까지 보여준다.
        // (예전엔 target == null 이면 즉시 꺼져서, 분해된 아이템·죽은 적 위의 텍스트가 바로 사라졌다.)
        timer -= Time.deltaTime;

        // Fade Out
        Color color = textMesh.color;
        color.a = timer / fadeTime;
        textMesh.color = color;

        offSet.y += moveSpeed * Time.deltaTime;

        UpdatePosition();

        if (timer <= 0)
        {
            target = null;
            gameObject.SetActive(false);
        }
    }

    private void UpdatePosition()
    {
        if (rectTransform == null) return;

        if (cam == null)
        {
            cam = parentCanvas != null ? parentCanvas.worldCamera : Camera.main;
            if (cam == null) return;
        }

        Vector3 worldPosition = spawnWorldPosition + offSet;
        Vector3 screenPos = cam.WorldToScreenPoint(worldPosition);

        if (parentCanvas == null)
        {
            transform.position = screenPos;
            return;
        }

        RectTransform canvasRect = parentCanvas.transform as RectTransform;
        if (canvasRect == null)
        {
            transform.position = screenPos;
            return;
        }

        if (parentCanvas.renderMode == RenderMode.WorldSpace)
        {
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, screenPos, parentCanvas.worldCamera, out Vector3 worldPoint))
            {
                rectTransform.position = worldPoint;
            }
        }
        else
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, parentCanvas.worldCamera, out Vector2 localPoint);
            rectTransform.anchoredPosition = localPoint;
        }
    }

    private void OnDisable()
    {
        // 씬 언로드 중에는 매니저가 먼저 사라질 수 있다.
        var manager = FloatingTextManager.Instance;
        if (manager != null) manager.ReturnToPool(this);
    }
}

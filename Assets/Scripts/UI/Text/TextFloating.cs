using DG.Tweening;
using TMPro;
using UnityEngine;

public class TextFloating : MonoBehaviour
{
    private TextMeshProUGUI textMesh;

    private Camera cam;
    private Canvas parentCanvas;
    private RectTransform rectTransform;

    // Text Location
    private Transform target;
    private Vector3 offSet;

    [Header("[ Text Setting ]")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float fadeTime;
    [SerializeField] private float displaytime;
    private float timer;

    private void Awake()
    {
        textMesh = GetComponent<TextMeshProUGUI>();
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        cam = parentCanvas != null ? parentCanvas.worldCamera : Camera.main;
        if (cam == null) cam = Camera.main;
    }

public void SetUp(string _text, Color _color, Transform _target, bool isCritical = false)
    {
        // 기존 호출 호환 (색을 직접 받는 레거시 시그니처)적용
        DOTween.Kill(textMesh);
        DOTween.Kill(rectTransform);

        timer = displaytime;

        if (textMesh == null)
            textMesh = GetComponent<TextMeshProUGUI>();

        textMesh.text = _text;
        target = _target;

        offSet = new Vector3(Random.Range(-0.5f, 0.5f), 0);

        UpdatePosition();
        gameObject.SetActive(true);

        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;

        textMesh.color = _color;

        if (isCritical)
        {
            rectTransform.localScale = Vector3.one * 1.5f;
            rectTransform.DOPunchScale(Vector3.one * 1.2f, 0.3f, 10, 1);
            rectTransform.DOShakeRotation(0.3f, 30f, 20, 90f);
            textMesh.outlineColor = Color.black;
            textMesh.outlineWidth = 0.3f;
        }
        else
        {
            textMesh.outlineColor = Color.black;
            textMesh.outlineWidth = 0.2f;
            rectTransform.DOPunchPosition(new Vector3(1f, 1f, 1f), 0.5f, 10, 1);
        }
    }

    /// <summary>
    /// FloatingTextStyleSO를 이용해 색상/크기/아웃라인/연출을 한번에 설정합니다.
    /// </summary>
    public void SetUp(string _text, FloatingTextStyleSO style, Transform _target)
    {
        DOTween.Kill(textMesh);
        DOTween.Kill(rectTransform);

        timer = displaytime;

        if (textMesh == null)
            textMesh = GetComponent<TextMeshProUGUI>();

        textMesh.text = _text;
        target = _target;

        offSet = new Vector3(Random.Range(-0.5f, 0.5f), 0);

        UpdatePosition();
        gameObject.SetActive(true);

        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;

        Color color         = style != null ? style.color : Color.white;
        float scale         = style != null ? style.scale : 1f;
        Color outlineColor  = style != null ? style.outlineColor : Color.black;
        float outlineWidth  = style != null ? style.outlineWidth : 0.2f;
        float punchStrength = style != null ? style.punchStrength : 1f;
        bool  useShake      = style != null && style.useShakeRotation;
        float shakeStrength = style != null ? style.shakeStrength : 30f;

        textMesh.color = color;
        textMesh.outlineColor = outlineColor;
        textMesh.outlineWidth = outlineWidth;

        rectTransform.localScale = Vector3.one * scale;

        if (useShake)
        {
            rectTransform.DOPunchScale(Vector3.one * 1.2f * punchStrength, 0.3f, 10, 1);
            rectTransform.DOShakeRotation(0.3f, shakeStrength, 20, 90f);
        }
        else
        {
            rectTransform.DOPunchPosition(new Vector3(1f, 1f, 1f) * punchStrength, 0.5f, 10, 1);
        }
    }

    /// <summary>
    /// FloatingTextStyleSO를 이용해 색상/크기/아웃라인/연출을 한번에 설정합니다.
    /// </summary>
    public void SetUp(string _text, FloatingTextStyleSO style, Transform _target, bool isCritical = false)
    {
        DOTween.Kill(textMesh);
        DOTween.Kill(rectTransform);

        timer = displaytime;

        if (textMesh == null)
            textMesh = GetComponent<TextMeshProUGUI>();

        textMesh.text = _text;
        target = _target;

        offSet = new Vector3(Random.Range(-0.5f, 0.5f), 0);

        UpdatePosition();
        gameObject.SetActive(true);

        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;

        // 스타일 적용 (null이면 기반 기본값)
        Color color           = style != null ? style.color : Color.white;
        float scale           = style != null ? style.scale : 1f;
        Color outlineColor    = style != null ? style.outlineColor : Color.black;
        float outlineWidth    = style != null ? style.outlineWidth : 0.2f;
        float punchStrength   = style != null ? style.punchStrength : 1f;
        bool  useShake        = style != null && style.useShakeRotation;
        float shakeStrength   = style != null ? style.shakeStrength : 30f;

        textMesh.color = color;
        textMesh.outlineColor = outlineColor;
        textMesh.outlineWidth = outlineWidth;

        rectTransform.localScale = Vector3.one * scale;

        if (useShake)
        {
            rectTransform.DOPunchScale(Vector3.one * 1.2f * punchStrength, 0.3f, 10, 1);
            rectTransform.DOShakeRotation(0.3f, shakeStrength, 20, 90f);
        }
        else
        {
            rectTransform.DOPunchPosition(new Vector3(1f, 1f, 1f) * punchStrength, 0.5f, 10, 1);
        }
    }

    private void Update()
    {
        if (target == null)
        {
            gameObject.SetActive(false);
            return;
        }

        // Animation
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
        if (target == null || rectTransform == null) return;

        if (cam == null)
        {
            cam = parentCanvas != null ? parentCanvas.worldCamera : Camera.main;
            if (cam == null) return;
        }

        Vector3 worldPosition = target.position + offSet;
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
        FloatingTextManager.instance.ReturnToPool(this);
    }
}

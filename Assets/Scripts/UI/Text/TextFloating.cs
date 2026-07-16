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
    private float timer;
    private TMPTextEffectPlayer effectPlayer;
    private static readonly List<TextEffectRange> tempEffectRanges = new List<TextEffectRange>();

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

        if(textMesh == null)
            textMesh = GetComponent<TextMeshProUGUI>();

        string parsedText = TextEffectParser.Parse(_text, tempEffectRanges);
        textMesh.text = parsedText;
        textMesh.color = _color;
        target = _target;
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
            rectTransform.DOPunchScale(Vector3.one * 1.2f, 0.3f, 10, 1);
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
            rectTransform.DOPunchScale(Vector3.one * 0.2f, 0.25f, 8, 0.6f);
        }
        effectPlayer.ApplyEffects(tempEffectRanges);
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
        FloatingTextManager.Instance.ReturnToPool(this);
    }
}

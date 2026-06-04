using DG.Tweening;
using TMPro;
using UnityEngine;

public class TextFloating : MonoBehaviour
{
    private TextMeshProUGUI textMesh;

    private Camera cam;
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
        cam = Camera.main;
        rectTransform = GetComponent<RectTransform>();
    }

    public void SetUp(string _text, Color _color, Transform _target, bool isCritical = false)
    {
        DOTween.Kill(textMesh); // Kill previous tween
        DOTween.Kill(rectTransform);

        timer = displaytime;

        if(textMesh == null)
            textMesh = GetComponent<TextMeshProUGUI>();

        textMesh.text = _text;
        textMesh.color = _color;
        target = _target;

        offSet = new Vector3(Random.Range(-0.5f, 0.5f), 0);

        gameObject.SetActive(true);

        // Reset Transform
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;

        if (isCritical)
        {
            // 1. Critical Scale Base
            rectTransform.localScale = Vector3.one * 1.5f;

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
        }
    }

    private void Update()
    {
        if(target == null)
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

        Vector3 screenPos = cam.WorldToScreenPoint(target.position + offSet);
        transform.position = screenPos;

        if (timer <= 0)
        {
            target = null;
            gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        FloatingTextManager.instance.ReturnToPool(this);
    }
}

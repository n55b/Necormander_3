using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Animates the two-sided fill of the directional attack gauge under the actor's HUD.</summary>
public class CenterMeetFill : MonoBehaviour
{
    [SerializeField] private RectTransform gaugeAnchor;
    [SerializeField] private Color backgroundColor = Color.white;
    [SerializeField] private Color fillColor = Color.red;

    private SpriteRenderer gauge;
    private float elapsed;
    private float duration;

    public bool IsFilling { get; private set; }

    private void Awake()
    {
        if (gaugeAnchor == null)
        {
            Debug.LogError("The attack gauge is missing its position and size reference.", this);
            enabled = false;
            return;
        }

        var canvas = GetComponentInParent<Canvas>();
        int layer = canvas != null ? canvas.sortingLayerID : 0;
        int order = canvas != null ? canvas.sortingOrder + 1 : 1;
        gauge = PixelVectorShape.Create("Attack Gauge", PixelVectorShape.Kind.Gauge, backgroundColor, order,
            PixelVectorShape.Stroke.Boundary, sortingLayer: layer);
        gauge.transform.SetParent(gaugeAnchor, false);
        if (WorldDepthOrder.FindEffectiveGroup(gauge) != null)
        {
            var overlay = gauge.gameObject.AddComponent<SortingGroup>();
            overlay.sortAtRoot = true;
            overlay.sortingLayerID = layer;
            overlay.sortingOrder = order;
        }
        Vector2 size = gaugeAnchor.rect.size;
        gauge.transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    private void Update()
    {
        if (!IsFilling) return;
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        PixelVectorShape.SetGaugeProgress(gauge, progress, fillColor, duration);
        if (progress >= 1f) IsFilling = false;
    }

    public void StartFill(float seconds)
    {
        duration = seconds > 0f ? seconds : 0.05f;
        elapsed = 0f;
        IsFilling = true;
        if (gauge != null) PixelVectorShape.SetGaugeProgress(gauge, 0f, fillColor, duration);
    }

    public void SetColor(Color color) => fillColor = color;

    public void StopFill() => IsFilling = false;
}

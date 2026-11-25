using UnityEngine;

[DisallowMultipleComponent]
public sealed class HudMotionElement : MonoBehaviour
{
    public enum Motion { Responsive, Idle, Pinned }
    internal Motion Behavior { get; private set; }
    private RectTransform rect, parent;
    private Canvas canvas;
    private Vector2 home, applied, written, filteredPixels;
    private Vector2 exit, departure;
    private bool departing;
    private CanvasGroup travelGroup;
    private float restAlpha;
    private bool ownsTravelGroup;
    internal void Depart(float time)
    {
        if (parent == null || canvas == null || !isActiveAndEnabled) return;
        if (time > 0 && !departing)
        {
            if (parent.GetComponentInParent<HudMotionElement>() != null) return;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = FrameCenter, max = min;
            foreach (var corner in corners)
            {
                Vector2 p = PixelViewport.CanvasToGamePoint(canvas, corner);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            var delta = FrameCenter - FloorTravel.Mouth;
            exit = Mathf.Abs(delta.x) / PixelViewport.Width > Mathf.Abs(delta.y) / PixelViewport.Height
                ? Vector2.right * (delta.x < 0 ? -max.x - 6 : PixelViewport.Width - min.x + 6)
                : Vector2.up * (delta.y < 0 ? -max.y - 6 : PixelViewport.Height - min.y + 6);
            travelGroup = GetComponent<CanvasGroup>();
            ownsTravelGroup = travelGroup == null;
            if (ownsTravelGroup) travelGroup = gameObject.AddComponent<CanvasGroup>();
            restAlpha = travelGroup.alpha;
            departing = true;
        }
        if (!departing) return;
        departure = exit * FloorTravel.Ease(FloorTravel.PoseStart, .34f, time);
        travelGroup.alpha = restAlpha * (1 - FloorTravel.Ease(.16f, .33f, time));
        if (time <= 0) EndDeparture();
        ApplyPixels(filteredPixels + departure);
    }
    internal float Phase { get; private set; }
    internal float Response { get; private set; }
    internal Vector2 FrameCenter => PixelViewport.CanvasToGamePoint(canvas, rect.TransformPoint(rect.rect.center));

    public static void Attach(RectTransform target, float phase, Motion behavior = Motion.Responsive)
    {
        if (target == null) return;
        var item = target.GetComponent<HudMotionElement>() ?? target.gameObject.AddComponent<HudMotionElement>();
        item.Behavior = behavior;
        item.Phase = phase;
        item.Response = .86f + .18f * Mathf.Cos(phase);
    }
    private void OnEnable()
    {
        rect = (RectTransform)transform;
        parent = rect.parent as RectTransform;
        canvas = GetComponentInParent<Canvas>();
        home = written = rect.anchoredPosition;
        CombatHudMotion.Register(this);
    }
    internal void Offset(Vector2 pixels)
    {
        filteredPixels = Vector2.Lerp(filteredPixels, pixels, 1f - Mathf.Exp(-35f * Time.unscaledDeltaTime));
        ApplyPixels(filteredPixels + departure);
    }
    private void ApplyPixels(Vector2 pixels)
    {
        if (parent == null || canvas == null) return;
        var root = canvas.rootCanvas;
        var rectSize = ((RectTransform)root.transform).rect.size;
        var local = new Vector3(pixels.x * rectSize.x / PixelViewport.Width, pixels.y * rectSize.y / PixelViewport.Height);
        var next = (Vector2)parent.InverseTransformVector(root.transform.TransformVector(local));
        Write(next);
    }
    private void Write(Vector2 next)
    {
        var current = rect.anchoredPosition;
        // Screen-space camera canvases can round-trip anchored positions with small float errors.
        // A driven layout can also replace our last position with a genuinely new resting position.
        if (Behavior == Motion.Responsive && (current - written).sqrMagnitude > .0001f) home = current;
        applied = next;
        written = home + next;
        if ((current - written).sqrMagnitude > .000001f) rect.anchoredPosition = written;
    }
    internal void Rest() { filteredPixels = Vector2.zero; if (departing || applied != Vector2.zero) ApplyPixels(departure); }
    private void EndDeparture()
    {
        departing = false;
        departure = Vector2.zero;
        if (travelGroup != null)
        {
            travelGroup.alpha = restAlpha;
            if (ownsTravelGroup) Destroy(travelGroup);
        }
        travelGroup = null;
    }
    private void OnDisable() { EndDeparture(); Rest(); CombatHudMotion.Unregister(this); }
}

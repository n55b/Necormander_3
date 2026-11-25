using System.Collections.Generic;
using UnityEngine;

/// <summary>Pools world-sorted arrows and positions them against the 640 × 360 frame.</summary>
[DefaultExecutionOrder(10000)]
public class OffscreenEnemyArrowManager : MonoBehaviour
{
    public static OffscreenEnemyArrowManager Instance { get; private set; }

    [SerializeField] private bool overlayEnabled = true;
    [SerializeField] private int arrowSortingOrder = 10000;
    [SerializeField] private string sortingLayerName = "";
    [SerializeField] private Color arrowColor = new Color32(209, 86, 97, 255);
    [Min(0)]
    [SerializeField] private int maxArrows = 16;

    private static readonly Vector2 FrameSize = new Vector2(PixelViewport.Width, PixelViewport.Height);
    private readonly List<SpriteRenderer> _arrows = new List<SpriteRenderer>();
    private readonly List<Marker> offscreen = new List<Marker>();
    private Transform pool;
    private bool initialized;

    private struct Marker
    {
        public Vector3 Viewport;
        public float DistanceSquared;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    public void Initialize()
    {
        if (initialized) return;
        Instance = this;
        pool = new GameObject("~OffscreenArrows").transform;
        pool.SetParent(transform, false);
        initialized = true;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnDisable() => HideFrom(0);

    private void LateUpdate()
    {
        var camera = PixelViewport.WorldCamera;
        var settings = CameraSettingsSO.Instance;
        if (!initialized || !overlayEnabled || maxArrows == 0 || camera == null || !camera.orthographic || settings == null)
        {
            HideFrom(0);
            return;
        }

        offscreen.Clear();
        var origin = camera.transform.position;
        foreach (var indicator in EntityDirectionIndicator.EnemyIndicators)
        {
            if (indicator == null || !indicator.IsActive) continue;
            Vector3 position = indicator.WorldPosition;
            Vector3 viewport = camera.WorldToViewportPoint(position);
            if (viewport.z <= 0f || (viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f))
                continue;

            offscreen.Add(new Marker { Viewport = viewport, DistanceSquared = (position - origin).sqrMagnitude });
        }

        if (offscreen.Count > maxArrows)
            offscreen.Sort((a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));

        int used = Mathf.Min(offscreen.Count, maxArrows);
        float size = settings.offscreenArrowPixels;
        float guard = size * 0.5f + settings.vectorIndicatorStrokePixels + 1f;
        for (int i = 0; i < used; i++)
        {
            var marker = offscreen[i];
            Vector2 aim = Vector2.Scale((Vector2)marker.Viewport - Vector2.one * 0.5f, FrameSize);
            Vector3 edge = FrameEdge(aim, Mathf.Max(guard, settings.offscreenArrowInsetPixels));
            edge.z = marker.Viewport.z;

            var renderer = GetArrow(i);
            renderer.transform.position = camera.ViewportToWorldPoint(edge);
            PixelVectorShape.SetScreenShape(renderer, new Vector2(size, size), aim.normalized);
            renderer.enabled = true;
        }

        HideFrom(used);
    }

    private static Vector2 FrameEdge(Vector2 direction, float inset)
    {
        float x = Mathf.Max(1f, FrameSize.x * 0.5f - inset);
        float y = Mathf.Max(1f, FrameSize.y * 0.5f - inset);
        float scale = 1f / Mathf.Max(Mathf.Abs(direction.x) / x, Mathf.Abs(direction.y) / y);
        return Vector2.one * 0.5f + Vector2.Scale(direction * scale, new Vector2(1f / FrameSize.x, 1f / FrameSize.y));
    }

    private SpriteRenderer GetArrow(int index)
    {
        while (_arrows.Count <= index)
        {
            var renderer = PixelVectorShape.Create("Arrow" + _arrows.Count, PixelVectorShape.Kind.Arrow,
                arrowColor, arrowSortingOrder, PixelVectorShape.Stroke.Indicator);
            renderer.transform.SetParent(pool, false);
            if (!string.IsNullOrEmpty(sortingLayerName)) renderer.sortingLayerName = sortingLayerName;
            renderer.enabled = false;
            _arrows.Add(renderer);
        }

        return _arrows[index];
    }

    private void HideFrom(int startIndex)
    {
        for (int i = startIndex; i < _arrows.Count; i++)
            if (_arrows[i] != null) _arrows[i].enabled = false;
    }
}

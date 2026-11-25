using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Opens the foreground itself so the visible colors, animation and separation of both hands survive.
[Unity.VisualScripting.RenamedFrom("PlayerOcclusionSilhouette")]
[RequireComponent(typeof(SpriteRenderer)), DisallowMultipleComponent, DefaultExecutionOrder(9000)]
public sealed class PlayerOcclusionMist : MonoBehaviour
{
    [SerializeField, Range(0,1)] private float bodyCoverageHeight = .58f;
    [SerializeField] private LayerMask occluderLayers;
    [SerializeField, Min(.02f)] private float spreadTime = .36f;
    [SerializeField, Min(.02f)] private float clearTime = .58f;
    [SerializeField, Min(.02f)] private float followTime = .18f;
    private static readonly int Progress = Shader.PropertyToID("_OcclusionFogProgress");
    private static readonly int Shape = Shader.PropertyToID("_OcclusionFogShape");
    private static readonly int Wake = Shader.PropertyToID("_OcclusionFogWake");
    private static readonly int Ppu = Shader.PropertyToID("_OcclusionFogPPU");
    private readonly List<Surface> surfaces = new();
    private readonly Dictionary<Material, Material> variants = new();
    private MaterialPropertyBlock block;
    private SpriteRenderer body;
    private YSortableObject depth;
    private Shader defaultMist;
    public int ActiveSurfaces => surfaces.Count;

    private sealed class Surface
    {
        internal SpriteRenderer renderer;
        internal Material original, replacement;
        internal bool covered;
        internal float visibility;
        internal Vector2 center, wake;
    }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        body = GetComponent<SpriteRenderer>();
        depth = GetComponent<YSortableObject>();
        if (occluderLayers.value == 0) occluderLayers = Layers.WallMask | Layers.ObjectMask;
        defaultMist = Resources.Load<Shader>("WorldAtmosphere/WorldPixelSprite");
        Shader.SetGlobalTexture("_OcclusionFogPalette", Resources.Load<Texture2D>("WorldAtmosphere/necrohanderPalette"));
    }

    private void LateUpdate() => UpdateMist(Time.deltaTime);

    private void UpdateMist(float elapsed)
    {
        if (!PolishFeedback.ExistingEnabled(ExistingFeedbackCue.OcclusionMist) || !body.enabled ||
            body.forceRenderingOff || body.sprite == null || body.color.a <= .01f)
        {
            Clear();
            return;
        }
        var bounds = body.bounds;
        var layerGroup = depth != null ? depth.Group : null;
        WorldDepthOrder.ReadOrder(body, layerGroup, out int layerId, out int order);
        int layer = SortingLayer.GetLayerValueFromID(layerId);
        float torso = Mathf.Lerp(bounds.min.y, bounds.max.y, bodyCoverageHeight);
        foreach (var surface in surfaces) surface.covered = false;
        var candidates = WorldDepthOrder.Entries;
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate == depth || !(candidate.OccludesPlayer ||
                ((1 << candidate.gameObject.layer) & occluderLayers.value) != 0)) continue;
            var scenery = candidate.Renderer;
            if (scenery == null || !scenery.enabled || scenery.forceRenderingOff || scenery.sprite == null || scenery.color.a < .08f) continue;
            var cover = scenery.bounds;
            if (torso < cover.min.y || torso > cover.max.y || !cover.Intersects(bounds)) continue;
            WorldDepthOrder.ReadOrder(scenery, candidate.Group, out int coverLayer, out int coverOrder);
            int rank = SortingLayer.GetLayerValueFromID(coverLayer);
            if (rank > layer || (rank == layer && coverOrder > order)) Cover(scenery, bounds.center);
        }
        if (surfaces.Count == 0) return;
        float ppu = 1 / Mathf.Max(.0001f, PixelViewport.WorldUnitsPerPixel);
        float size = CameraSettingsSO.Instance != null ? CameraSettingsSO.Instance.occlusionMistSize : 1;
        float rx = Mathf.Clamp(bounds.extents.x+.34f,.68f,1.32f) * ppu * size;
        float ry = Mathf.Clamp(bounds.extents.y+.20f,.57f,1.24f) * ppu * size;
        Vector2 target = bounds.center;
        float radius = Mathf.Min(rx,ry) / ppu;
        float follow = 1 - Mathf.Exp(-elapsed / followTime);
        float drift = 1 - Mathf.Exp(-elapsed / (followTime * 2));
        for (int i = surfaces.Count-1; i >= 0; --i)
        {
            var surface = surfaces[i];
            if (surface.covered)
            {
                // Start a fresh clearing after a warp across the same foreground sprite.
                if ((surface.center - target).sqrMagnitude > 9 * radius * radius)
                {
                    surface.center = surface.wake = target;
                    surface.visibility = 0;
                }
                surface.center = target + Vector2.ClampMagnitude((surface.center - target) * (1-follow), radius * .55f);
                surface.wake = surface.center + Vector2.ClampMagnitude((surface.wake - surface.center) * (1-drift), radius * .8f);
            }
            surface.visibility = Mathf.MoveTowards(surface.visibility, surface.covered ? 1 : 0,
                elapsed / (surface.covered ? spreadTime : clearTime));
            if (surface.renderer == null || !surface.renderer.enabled || !surface.renderer.gameObject.activeInHierarchy ||
                (!surface.covered && surface.visibility <= 0))
            {
                Restore(surface);
                surfaces.RemoveAt(i);
                continue;
            }
            var renderer = surface.renderer;
            renderer.GetPropertyBlock(block);
            block.SetFloat(Progress,surface.visibility);
            block.SetVector(Shape,new Vector4(surface.center.x,surface.center.y,Mathf.Round(rx),Mathf.Round(ry)));
            block.SetVector(Wake,new Vector4(surface.wake.x,surface.wake.y,0,0));
            block.SetFloat(Ppu,ppu);
            renderer.SetPropertyBlock(block);
        }
    }

    private void Cover(SpriteRenderer scenery, Vector2 center)
    {
        foreach (var existing in surfaces)
        {
            if (existing.renderer != scenery) continue;
            existing.covered = true;
            return;
        }
        var original = scenery.sharedMaterial;
        if (original == null) return;
        var surface = new Surface { renderer = scenery, original = original, covered = true, center = center, wake = center };
        if (!original.HasProperty(Progress))
        {
            if (defaultMist == null) return;
            if (!variants.TryGetValue(original, out var variant))
            {
                variant = new Material(defaultMist);
                variant.CopyPropertiesFromMaterial(original);
                variant.SetFloat("_MistUnlit", original.shader.name.Contains("Unlit") || original.shader.name == "Sprites/Default" ? 1 : 0);
                variants.Add(original, variant);
            }
            surface.replacement = variant;
            scenery.sharedMaterial = variant;
        }
        surfaces.Add(surface);
    }

    private void Restore(Surface surface)
    {
        var renderer = surface.renderer;
        if (renderer == null) return;
        renderer.GetPropertyBlock(block);
        block.SetFloat(Progress,0);
        renderer.SetPropertyBlock(block);
        if (surface.replacement != null && renderer.sharedMaterial == surface.replacement)
            renderer.sharedMaterial = surface.original;
    }

    private void Clear()
    {
        foreach (var surface in surfaces) Restore(surface);
        surfaces.Clear();
    }

    private void OnDisable() => Clear();
    private void OnDestroy()
    {
        foreach (var variant in variants.Values) Destroy(variant);
    }
}

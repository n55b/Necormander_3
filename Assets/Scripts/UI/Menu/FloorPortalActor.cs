using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Keep a GPU copy of the actual pose. Its world renderer, AI and facing can resume unchanged.
internal sealed class FloorPortalActor : RawImage
{
    private RenderTexture pose;
    private Material ink;
    private SpriteRenderer[] originals;
    private Color[] worldColors;
    private float previousBlend = -1;
    private Vector2 start, size;
    private int index;
    private bool turn;
    internal Vector2 StartPoint => start;

    internal static FloorPortalActor Capture(Transform parent, Camera camera, Transform visual, SpriteRenderer body,
        bool facingRight, int index, Material captureMaterial, Shader inkShader)
    {
        var parts = visual.GetComponentsInChildren<SpriteRenderer>()
            .Where(r => r.enabled && !r.forceRenderingOff && r.sprite != null &&
                r.maskInteraction == SpriteMaskInteraction.None && r.sortingLayerName == body.sortingLayerName)
            .OrderBy(r => r.sortingOrder).ToArray();
        if (parts.Length == 0) return null;
        var bounds = parts[0].bounds;
        foreach (var part in parts) bounds.Encapsulate(part.bounds);
        Vector2 lo = PixelViewport.ProjectToLogical(camera, bounds.min), hi = PixelViewport.ProjectToLogical(camera, bounds.max);
        var middle = (Vector2)Vector2Int.RoundToInt((lo + hi) * .5f);
        // Even dimensions keep pixel centers aligned after recentering the camera projection.
        int width = 2 * Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(hi.x-lo.x)*.5f)+6, 8, PixelViewport.Width/2);
        int height = 2 * Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(hi.y-lo.y)*.5f)+6, 8, PixelViewport.Height/2);
        var node = new GameObject(index == 0 ? "Portal hero" : "Portal summon", typeof(RectTransform), typeof(CanvasRenderer));
        node.transform.SetParent(parent,false);
        var actor = node.AddComponent<FloorPortalActor>();
        actor.raycastTarget = false;
        actor.pose = RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);
        actor.pose.filterMode = FilterMode.Point;
        actor.pose.wrapMode = TextureWrapMode.Clamp;
        // A SpriteRenderer can be outside the previous camera cull or still in an SRP batch.
        // Submit its current atlas geometry explicitly, in the cropped target's pixel coordinates.
        using (var cmd = new CommandBuffer { name = "Floor travel pose" })
        {
            var meshes = new List<Mesh>(parts.Length);
            var properties = new MaterialPropertyBlock();
            try
            {
                cmd.SetRenderTarget(actor.pose);
                cmd.SetViewport(new Rect(0, 0, width, height));
                cmd.ClearRenderTarget(false, true, Color.clear);
                foreach (var part in parts)
                {
                    var sprite = part.sprite;
                    var vertices = sprite.vertices;
                    var points = new Vector3[vertices.Length];
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        var local = vertices[v];
                        if (part.flipX) local.x = -local.x;
                        if (part.flipY) local.y = -local.y;
                        Vector2 screen = PixelViewport.ProjectToLogical(camera, part.transform.TransformPoint(local));
                        points[v] = new Vector3((screen.x - middle.x) * 2f / width, (screen.y - middle.y) * 2f / height);
                    }
                    var mesh = new Mesh { name = "Portal pose geometry", vertices = points, uv = sprite.uv };
                    meshes.Add(mesh);
                    mesh.SetIndices(sprite.triangles, MeshTopology.Triangles, 0);
                    properties.SetTexture("_MainTex", sprite.texture);
                    properties.SetColor("_Color", part.color);
                    cmd.DrawMesh(mesh, Matrix4x4.identity, captureMaterial, 0, 0, properties);
                }
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally { foreach (var mesh in meshes) Destroy(mesh); }
        }
        actor.texture = actor.pose;
        actor.material = actor.ink = new Material(inkShader);
        actor.ink.SetColor("_Rim", FloorPortalGraphic.TravelColor(0,7));
        actor.start = middle;
        actor.index = index;
        actor.turn = facingRight != (index == 0);
        actor.rectTransform.anchorMin = actor.rectTransform.anchorMax = Vector2.zero;
        actor.rectTransform.sizeDelta = actor.size = new Vector2(width,height);
        actor.originals = parts;
        actor.worldColors = parts.Select(r => r.color).ToArray();
        actor.ink.SetVector("_Mouth", new Vector4(FloorTravel.Mouth.x, FloorTravel.Mouth.y, 0, 0));
        FloorPortalDust.Attach(actor, actor.pose);
        return actor;
    }

    internal void Present(float time, int count)
    {
        float transfer = FloorTravel.Ease(FloorTravel.PoseStart, FloorTravel.LiftStart, time);
        if (previousBlend != transfer)
        {
            for (int i = 0; i < originals.Length; i++)
            {
                var part = originals[i];
                if (part == null) continue;
                var tint = worldColors[i];
                part.forceRenderingOff = transfer >= 1;
                if (!part.forceRenderingOff) tint.a *= 1 - transfer;
                part.color = tint;
            }
            previousBlend = transfer;
        }
        float pull = FloorTravel.Intake(index,count,time);
        ink.SetFloat("_Pull",pull);
        ink.SetFloat("_Opacity", transfer * (1 - FloorTravel.Ease(.42f, 1, pull)));
        enabled = pull < 1;
        if (!enabled) return;
        Vector2 ring = FloorTravel.Hover(start,index,time) - FloorTravel.Mouth;
        float angle = pull * (index == 0 ? 2.6f : 3.3f);
        Vector2 spin = new(ring.x * Mathf.Cos(angle) - ring.y * Mathf.Sin(angle),
            ring.x * Mathf.Sin(angle) * .55f + ring.y * Mathf.Cos(angle));
        rectTransform.anchoredPosition = Vector2Int.RoundToInt(FloorTravel.Mouth + spin * (1f-pull));
        float facing = turn ? FloorTravel.Ease(FloorTravel.LiftStart,.24f,time) : 0;
        ink.SetFloat("_Turn", facing);
        float scale = Mathf.Lerp(1,.12f,pull);
        rectTransform.sizeDelta = Vector2Int.Max(Vector2Int.one,Vector2Int.RoundToInt(size * scale));
        rectTransform.localScale = new Vector3(1 - .12f * Mathf.Sin(facing * Mathf.PI),1,1);
        var center = rectTransform.anchoredPosition;
        ink.SetVector("_Pose", new Vector4(center.x, center.y, rectTransform.sizeDelta.x, rectTransform.sizeDelta.y));
    }

    protected override void OnDestroy()
    {
        if (originals != null)
            for (int i = 0; i < originals.Length; i++)
                if (originals[i] != null)
                {
                    originals[i].color = worldColors[i];
                    originals[i].forceRenderingOff = false;
                }
        texture = null;
        if (pose != null) RenderTexture.ReleaseTemporary(pose);
        if (ink != null) Destroy(ink);
        base.OnDestroy();
    }
}

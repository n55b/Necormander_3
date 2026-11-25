using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent, RequireComponent(typeof(Camera)), DefaultExecutionOrder(8500)]
public sealed class GroundShadowField : MonoBehaviour
{
    private const int MaxLights = 8;
    private readonly Vector4[] lights = new Vector4[MaxLights], cones = new Vector4[MaxLights], lampColors = new Vector4[MaxLights];
    private readonly float[] priorities = new float[MaxLights];
    private readonly Dictionary<Sprite, Pose> poses = new();
    private readonly Dictionary<Texture, Batch> batches = new();
    private Camera worldCamera;
    private Shader shader;
    private Rect visible;
    private Bounds batchBounds;

    private sealed class Pose
    {
        internal Vector2[] points, uv;
        internal ushort[] triangles;
        internal Vector4 rect;
    }

    private sealed class Batch
    {
        internal Mesh mesh;
        internal MeshRenderer renderer;
        internal Material material;
        internal readonly List<Vector3> points = new();
        internal readonly List<Vector2> uv = new();
        internal readonly List<Vector4> floor = new(), wind = new(), edges = new();
        internal readonly List<int> indices = new();

        internal void Vertex(Vector3 point, Vector2 texcoord, Vector4 contact, Vector4 gust, Vector4 limits)
        {
            points.Add(point); uv.Add(texcoord); floor.Add(contact); wind.Add(gust); edges.Add(limits);
        }

        internal void Flush(Bounds bounds)
        {
            renderer.enabled = indices.Count != 0;
            if (!renderer.enabled) return;
            mesh.Clear();
            mesh.SetVertices(points); mesh.SetUVs(0, uv); mesh.SetUVs(1, floor);
            mesh.SetUVs(2, wind); mesh.SetUVs(3, edges);
            mesh.SetTriangles(indices, 0, false); mesh.bounds = bounds;
            points.Clear(); uv.Clear(); floor.Clear(); wind.Clear(); edges.Clear(); indices.Clear();
        }
    }

    private void Awake()
    {
        worldCamera = GetComponent<Camera>();
        WallContactShadows.BindScene(PixelArtUpscaleRig.SceneGround);
        shader = Resources.Load<Shader>("WorldAtmosphere/GroundProjection");
    }

    private void LateUpdate()
    {
        if (UnityNote.SceneLoader.FloorIsCovered) return;
        var settings = CameraSettingsSO.Instance;
        if (shader == null || settings == null) return;
        Vector2 center = worldCamera.transform.position;
        float halfHeight = worldCamera.orthographicSize;
        Vector2 halfSize = new Vector2(halfHeight * worldCamera.aspect, halfHeight);
        // Include sources whose projected tips can enter before their feet.
        visible = new Rect(center - halfSize - Vector2.one * 4, 2 * halfSize + Vector2.one * 8);
        batchBounds = new Bounds((Vector3)center, new Vector3(visible.width + 12, visible.height + 12, 20));
        BindLights(center, settings);
        if ((LightingPreview.Active?.shadows ?? settings.groundShadowStrength) > 0)
        {
            var actors = WorldDepthOrder.Entries;
            for (int i = 0; i < actors.Count; ++i)
            {
                var actor = actors[i];
                if (!actor.CastsGroundShadow || actor.Renderer == null) continue;
                // Baked contacts lie inside the art, absent contacts fall back to the sorting pivot.
                if (!actor.HasManualShadowContact && !visible.Contains(actor.SampledGround))
                {
                    var bounds = actor.Renderer.bounds;
                    if (bounds.max.x < visible.xMin || bounds.min.x > visible.xMax ||
                        bounds.max.y < visible.yMin || bounds.min.y > visible.yMax) continue;
                }
                Append(actor.Renderer, actor.ShadowGroundPosition, false, actor.Canopy);
            }
            var pieces = AirborneShadow.Active;
            for (int i = 0; i < pieces.Count; ++i)
            {
                var part = pieces[i];
                Append(part.Picture, part.Floor, part.IsHand, null, true);
            }
        }
        foreach (var batch in batches.Values) batch.Flush(batchBounds);
    }

    private void BindLights(Vector2 center, CameraSettingsSO settings)
    {
        int count = 0;
        float ambient = 1;
        var active = GroundShadowLight.Active;
        for (int i = 0; i < active.Count; ++i)
        {
            var lamp = active[i];
            if (!lamp.isActiveAndEnabled) continue;
            float luminance = lamp.intensity * lamp.color.grayscale;
            if (lamp.lightType == Light2D.LightType.Global)
            {
                ambient = luminance;
                continue;
            }
            if (luminance <= 0) continue;
            float radius = lamp.pointLightOuterRadius;
            Vector2 position = lamp.transform.position;
            var nearest = new Vector2(Mathf.Clamp(position.x, visible.xMin, visible.xMax),
                Mathf.Clamp(position.y, visible.yMin, visible.yMax));
            if (radius <= 0 || (position - nearest).sqrMagnitude > radius * radius) continue;
            float priority = luminance / (1 + (position - center).sqrMagnitude / (radius * radius));
            int slot = count;
            while (slot > 0 && priority > priorities[slot - 1]) --slot;
            if (slot >= MaxLights) continue;
            for (int j = Mathf.Min(count, MaxLights - 1); j > slot; --j)
            {
                lights[j] = lights[j-1]; cones[j] = cones[j-1]; lampColors[j] = lampColors[j-1]; priorities[j] = priorities[j-1];
            }
            count = Mathf.Min(count + 1, MaxLights);
            priorities[slot] = priority;
            lights[slot] = new Vector4(position.x, position.y, 1/radius, luminance);
            lampColors[slot] = lamp.color.linear;
            float outer = Mathf.Cos(Mathf.Deg2Rad * .5f * lamp.pointLightOuterAngle);
            float inner = Mathf.Cos(Mathf.Deg2Rad * .5f * lamp.pointLightInnerAngle);
            var forward = lamp.transform.up;
            cones[slot] = new Vector4(forward.x, forward.y, outer,
                lamp.pointLightOuterAngle >= 359.99f ? 0 : 1 / Mathf.Max(.001f, inner - outer));
        }
        var preview = LightingPreview.Active;
        float angle = (preview != null ? preview.azimuth + 180 : settings.groundShadowAngle) * Mathf.Deg2Rad;
        float length = preview != null ? 1f / Mathf.Tan(preview.elevation * Mathf.Deg2Rad) : settings.groundShadowLength;
        Shader.SetGlobalFloat("_GroundSunWeight",preview?.intensity ?? 1);
        Shader.SetGlobalVector("_GroundKey", new Vector4(Mathf.Cos(angle) * length, Mathf.Sin(angle) * length,
            preview?.shadows ?? settings.groundShadowStrength, ambient));
        Shader.SetGlobalInt("_GroundLightCount", count);
        Shader.SetGlobalVectorArray("_GroundLights", lights);
        Shader.SetGlobalVectorArray("_GroundCones", cones);
        Shader.SetGlobalVectorArray("_GroundLampColors", lampColors);
    }

    private void Append(SpriteRenderer picture, Vector2 ground, bool hand, TreeCanopyWind canopy, bool airborne = false)
    {
        if (picture == null || !picture.enabled || picture.forceRenderingOff || !picture.gameObject.activeInHierarchy ||
            !visible.Contains(ground) || picture.sprite == null || picture.color.a <= .01f) return;
        var sprite = picture.sprite;
        if (!poses.TryGetValue(sprite, out var pose))
        {
            pose = new Pose { points = sprite.vertices, uv = sprite.uv, triangles = sprite.triangles,
                rect = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite) };
            poses.Add(sprite, pose);
        }
        var bounds = picture.bounds;
        if (hand)
        {
            // Part of the vertical hand motion is reach along the floor; the rest is lift.
            ground.x = bounds.center.x;
            ground.y += (bounds.center.y - ground.y - .65f) * .46f;
        }
        float clearance = airborne ? Mathf.Max(0, bounds.center.y - ground.y) : 0;
        float opacity = picture.color.a / (1 + clearance * .42f);
        var floor = new Vector4(ground.x, ground.y, 0, opacity);
        var gust = Vector4.zero;
        if (canopy != null) gust = new Vector4(pose.rect.y, 1 / Mathf.Max(.0001f, pose.rect.w - pose.rect.y),
            canopy.Phase, 1f / sprite.texture.width);
        var batch = GetBatch(sprite.texture);
        int start = batch.points.Count;
        var matrix = picture.localToWorldMatrix;
        for (int i = 0; i < pose.points.Length; ++i)
        {
            Vector2 p = pose.points[i];
            if (picture.flipX) p.x = -p.x;
            if (picture.flipY) p.y = -p.y;
            batch.Vertex(matrix.MultiplyPoint3x4(p), pose.uv[i], floor, gust, pose.rect);
        }
        for (int i = 0; i < pose.triangles.Length; ++i) batch.indices.Add(start + pose.triangles[i]);

        // The dense contact lobe remains visible when a side light makes the cast silhouette edge-on.
        float width = Mathf.Clamp(bounds.extents.x * (hand ? .64f : .36f), .075f, .56f);
        width /= 1 + clearance * .32f;
        floor.z = -1;
        floor.w = opacity * (airborne ? .45f : .83f);
        AppendContact(batch, ground, width, floor);
    }

    private static void AppendContact(Batch batch, Vector2 ground, float width, Vector4 floor)
    {
        int i = batch.points.Count;
        Vector2 size = new Vector2(width, Mathf.Max(.038f, width * .37f));
        batch.Vertex(ground - size, Vector2.zero, floor, default, default);
        batch.Vertex(ground + new Vector2(-size.x,size.y), Vector2.up, floor, default, default);
        batch.Vertex(ground + size, Vector2.one, floor, default, default);
        batch.Vertex(ground + new Vector2(size.x,-size.y), Vector2.right, floor, default, default);
        batch.indices.Add(i); batch.indices.Add(i+1); batch.indices.Add(i+2);
        batch.indices.Add(i); batch.indices.Add(i+2); batch.indices.Add(i+3);
    }

    private Batch GetBatch(Texture atlas)
    {
        if (batches.TryGetValue(atlas, out var batch)) return batch;
        var surface = new GameObject("Ground shadows - " + atlas.name, typeof(MeshFilter), typeof(MeshRenderer));
        SceneManager.MoveGameObjectToScene(surface, gameObject.scene);
        batch = new Batch { renderer = surface.GetComponent<MeshRenderer>(), mesh = new Mesh { name = "Ground shadows" },
            material = new Material(shader) { mainTexture = atlas } };
        batch.mesh.indexFormat = IndexFormat.UInt32;
        batch.mesh.MarkDynamic();
        surface.GetComponent<MeshFilter>().sharedMesh = batch.mesh;
        batch.renderer.sharedMaterial = batch.material;
        batch.renderer.sortingLayerName = "Shadow";
        batch.renderer.sortingOrder = 2;
        batch.renderer.shadowCastingMode = ShadowCastingMode.Off;
        batch.renderer.receiveShadows = false;
        batches.Add(atlas, batch);
        return batch;
    }

    private void OnDisable()
    {
        foreach (var batch in batches.Values)
            if (batch.renderer != null) batch.renderer.enabled = false;
    }

    private void OnDestroy()
    {
        foreach (var batch in batches.Values)
        {
            if (batch.renderer != null) Destroy(batch.renderer.gameObject);
            Destroy(batch.material);
            Destroy(batch.mesh);
        }
    }
}

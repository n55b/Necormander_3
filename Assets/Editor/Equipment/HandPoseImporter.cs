using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(2, "handposes")]
public sealed class HandPoseImporter : ScriptedImporter
{
    private static string[] GatherDependenciesFromSourceFile(string assetPath)
        => File.ReadAllLines(assetPath).Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.TrimStart('@')).Distinct().ToArray();

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var catalog = ScriptableObject.CreateInstance<HandPoseCatalog>();
        var poses = new List<HandPoseCatalog.Pose>();
        var textures = new Dictionary<Texture2D, Texture2D>();
        try
        {
            foreach (var path in File.ReadAllLines(ctx.assetPath).Where(p => p.Length != 0 && !p.StartsWith("@")))
            {
                ctx.DependsOnArtifact(path);
                foreach (var frame in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
                {
                    if (!textures.TryGetValue(frame.texture, out var copy))
                    {
                        ctx.DependsOnArtifact(AssetDatabase.GetAssetPath(frame.texture));
                        textures.Add(frame.texture, copy = ReadTexture(frame.texture));
                    }
                    var r = new RectInt(Vector2Int.RoundToInt(frame.rect.position), Vector2Int.RoundToInt(frame.rect.size));
                    var pixels = copy.GetPixels(r.x, r.y, r.width, r.height);
                    var a = new List<Vector2Int>();
                    var b = new List<Vector2Int>();
                    var islands = Islands(pixels, r.width, r.height);
                    if (islands.Count == 0) continue;
                    a.AddRange(islands[0]);
                    if (islands.Count > 1)
                    {
                        b.AddRange(islands[1]);
                        Vector2 ca = BoundsOf(a).center, cb = BoundsOf(b).center;
                        foreach (var island in islands.Skip(2))
                        {
                            Vector2 c = BoundsOf(island).center;
                            (Vector2.SqrMagnitude(c - ca) < Vector2.SqrMagnitude(c - cb) ? a : b).AddRange(island);
                        }
                        if (ca.x > cb.x) (a, b) = (b, a);
                    }
                    var pose = new HandPoseCatalog.Pose { frame = frame };
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(frame, out string guid, out long id);
                    string key = guid + "_" + id;
                    MakePart(ctx, key + "_L", frame, pixels, r.width, a, out pose.left, out pose.leftOffset);
                    MakePart(ctx, key + "_R", frame, pixels, r.width, b, out pose.right, out pose.rightOffset);
                    poses.Add(pose);
                }
            }
        }
        finally { foreach (var texture in textures.Values) DestroyImmediate(texture); }
        catalog.poses = poses.ToArray();
        ctx.AddObjectToAsset("poses", catalog);
        ctx.SetMainObject(catalog);
    }

    private static Texture2D ReadTexture(Texture2D source)
    {
        var previous = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            copy.Apply();
            return copy;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
    }

    private static List<List<Vector2Int>> Islands(Color[] pixels, int width, int height)
    {
        var groups = new List<List<Vector2Int>>();
        var visited = new bool[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            if (visited[i] || pixels[i].a == 0) continue;
            var group = new List<Vector2Int> { new(i % width, i / width) };
            visited[i] = true;
            for (int k = 0; k < group.Count; k++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = group[k].x + dx, y = group[k].y + dy;
                        if (x < 0 || x >= width || y < 0 || y >= height) continue;
                        int n = y * width + x;
                        if (visited[n] || pixels[n].a == 0) continue;
                        visited[n] = true;
                        group.Add(new Vector2Int(x, y));
                    }
            groups.Add(group);
        }
        return groups.OrderByDescending(group => group.Count).ToList();
    }

    private static RectInt BoundsOf(List<Vector2Int> points)
    {
        Vector2Int min = points[0], max = min;
        foreach (var p in points) { min = Vector2Int.Min(min, p); max = Vector2Int.Max(max, p); }
        return new RectInt(min, max - min + Vector2Int.one);
    }

    private static void MakePart(AssetImportContext ctx, string key, Sprite frame, Color[] pixels, int stride,
        List<Vector2Int> points, out Sprite sprite, out Vector2 offset)
    {
        sprite = null;
        offset = Vector2.zero;
        if (points.Count == 0) return;
        var bounds = BoundsOf(points);
        var original = frame.rect;
        var rect = new Rect(original.x + bounds.x, original.y + bounds.y, bounds.width, bounds.height);
        Texture2D texture = frame.texture;
        // Reuse the atlas unless the other hand also draws inside this rectangle.
        int drawn = 0;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                if (pixels[y * stride + x].a != 0) drawn++;
        if (drawn != points.Count)
        {
            texture = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var colors = new Color[bounds.width * bounds.height];
            foreach (var p in points) colors[(p.y - bounds.y) * bounds.width + p.x - bounds.x] = pixels[p.y * stride + p.x];
            texture.SetPixels(colors);
            texture.Apply(false, true);
            ctx.AddObjectToAsset(key + "_pixels", texture);
            rect.position = Vector2.zero;
        }
        sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), frame.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprite.name = frame.name + key.Substring(key.Length - 2);
        offset = (bounds.center - frame.pivot) / frame.pixelsPerUnit;
        ctx.AddObjectToAsset(key, sprite);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal sealed class SpriteGroundContactBaker : AssetPostprocessor
{
    private const string Output = "Assets/Resources/" + SpriteGroundContacts.ResourcePath + ".json";
    private const string Root = "Assets/Resources/Sprites/";
    private static bool queued;
    private static readonly HashSet<string> pending = new(StringComparer.Ordinal);

    private static bool IsSource(string path)
    {
        if (!path.StartsWith(Root, StringComparison.Ordinal) ||
            !(path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".aseprite", StringComparison.OrdinalIgnoreCase))) return false;
        string relative = path.Substring(Root.Length);
        return relative.StartsWith("Player/") || relative.StartsWith("NPC/") || relative.StartsWith("Enemy/") ||
            relative.StartsWith("Minion/") || relative.StartsWith("Object/") || relative.StartsWith("Traps/") ||
            relative.StartsWith("Tile/Field_Object") || relative.StartsWith("Tile/Field_AnimatedObject") ||
            relative.StartsWith("Tile/Legacy/Field_Object");
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] oldPaths)
    {
        foreach (var path in imported.Concat(deleted).Concat(moved).Concat(oldPaths))
            if (IsSource(path)) pending.Add(path);
        if (queued || pending.Count == 0) return;
        queued = true;
        EditorApplication.delayCall += RefreshChanged;
    }

    private static void RefreshChanged()
    {
        queued = false;
        var changed = new HashSet<string>(pending, StringComparer.Ordinal);
        pending.Clear();
        Bake(changed);
    }

    [MenuItem("Tools/Art/Rebuild Sprite Ground Contacts")]
    public static void Rebuild() => Bake(null);

    private static void Bake(HashSet<string> changed)
    {
        Dictionary<string, SpriteGroundContacts.Sheet> existing = null;
        if (changed != null && File.Exists(Output))
            existing = JsonUtility.FromJson<SpriteGroundContacts.Table>(File.ReadAllText(Output))
                .sheets.ToDictionary(sheet => sheet.texture, StringComparer.Ordinal);
        var sheets = new List<SpriteGroundContacts.Sheet>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in AssetDatabase.GetAllAssetPaths().Where(IsSource).OrderBy(p => p,StringComparer.Ordinal))
        {
            if (existing != null && !changed.Contains(path) &&
                existing.TryGetValue(Path.GetFileNameWithoutExtension(path), out var saved))
            {
                if (!names.Add(saved.texture)) throw new InvalidDataException("Duplicate ground-contact atlas name: " + path);
                sheets.Add(saved);
                continue;
            }
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            if (sprites.Length == 0) continue;
            var atlas = sprites[0].texture;
            if (!names.Add(atlas.name)) throw new InvalidDataException("Duplicate ground-contact atlas name: " + path);
            var pixels = ReadAtlas(atlas);
            var frames = new Dictionary<RectInt,SpriteGroundContacts.Entry>();
            foreach (var sprite in sprites)
            {
                var source = sprite.rect;
                var r = new RectInt((int)source.x,(int)source.y,(int)source.width,(int)source.height);
                if (frames.ContainsKey(r)) continue;
                var low = Support(r, pixels, atlas.width, false);
                if (low.y < 0) continue;
                frames.Add(r, new SpriteGroundContacts.Entry { rect = r, low = low, high = Support(r,pixels,atlas.width,true) });
            }
            sheets.Add(new SpriteGroundContacts.Sheet { texture = atlas.name,
                frames = frames.Values.OrderBy(f => f.rect.y).ThenBy(f => f.rect.x).ThenBy(f => f.rect.width).ThenBy(f => f.rect.height).ToArray() });
        }
        string json = JsonUtility.ToJson(new SpriteGroundContacts.Table { sheets = sheets.ToArray() });
        if (!File.Exists(Output) || File.ReadAllText(Output) != json)
        {
            File.WriteAllText(Output,json);
            AssetDatabase.ImportAsset(Output);
        }
    }

    private static Vector2 Support(RectInt rect, Color32[] pixels, int stride, bool top)
    {
        for (int row = 0; row < rect.height; row++)
        {
            int y = top ? rect.height-1-row : row;
            int start = (rect.y+y)*stride+rect.x;
            int first = rect.width, last = -1;
            for (int x = 0; x < rect.width; x++)
            {
                if (pixels[start+x].a < 128) continue;
                first = Mathf.Min(x,first);
                last = x;
            }
            if (last < first) continue;
            int mid = (first+last)/2;
            for (int d = 0; d <= last-first; d++)
            {
                if (mid-d >= first && pixels[start+mid-d].a >= 128) { mid -= d; break; }
                if (mid+d <= last && pixels[start+mid+d].a >= 128) { mid += d; break; }
            }
            return new Vector2(mid+.5f,top ? y+1 : y);
        }
        return new Vector2(0,-1);
    }

    internal static Color32[] ReadAtlas(Texture2D atlas)
    {
        var previous = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(atlas.width,atlas.height,0,RenderTextureFormat.ARGB32);
        var copy = new Texture2D(atlas.width,atlas.height,TextureFormat.RGBA32,false);
        try
        {
            Graphics.Blit(atlas,rt);
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);
            copy.Apply();
            return copy.GetPixels32();
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(copy); }
    }
}

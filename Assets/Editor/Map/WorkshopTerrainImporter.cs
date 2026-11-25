using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

// The brush is an imported TileBase. Editing its artist sheet regenerates the masks without changing its ID.
[ScriptedImporter(4, "terrainbrush")]
public sealed class WorkshopTerrainImporter : ScriptedImporter
{
    [Serializable] private sealed class Recipe
    {
        public string title, sheetGuid, prefix;
        // center, north/east/south/west edges, then NW/NE/SW/SE outer and inner corners.
        public int[] samples;
        public WorkshopTerrainTile.Surface surface;
        public string fillSprite, shallowSprite;
        public string[] connects;
    }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var recipe = JsonUtility.FromJson<Recipe>(File.ReadAllText(ctx.assetPath));
        string path = AssetDatabase.GUIDToAssetPath(recipe.sheetGuid);
        ctx.DependsOnArtifact(path);
        var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
        if (recipe.samples.Length != 13) throw new InvalidDataException("A terrain brush requires thirteen source tiles.");
        var sources = recipe.samples.Select(i => sprites[recipe.prefix + i]).ToArray();
        int size = (int)sources[0].rect.width;
        if (size <= 0 || size % 2 != 0 || sources.Any(s => s.rect.width != size || s.rect.height != size))
            throw new InvalidDataException("Terrain samples must be equally sized, even square sprites.");
        var tile = ScriptableObject.CreateInstance<WorkshopTerrainTile>();
        tile.name = recipe.title;
        var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(sheet, File.ReadAllBytes(path))) throw new InvalidDataException(path);
            var atlas = new Texture2D(size * 8, size * 6, TextureFormat.RGBA32, false)
                { name = recipe.title, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var index = new Dictionary<int, Sprite>();
            var variants = new Sprite[256];
            var sourcePixels = sheet.GetPixels32();
            Color32 Read(Sprite sample, int x, int y)
            {
                var rect = sample.rect;
                return sourcePixels[((int)rect.y + y) * sheet.width + (int)rect.x + x];
            }
            var fill = string.IsNullOrEmpty(recipe.fillSprite) ? null : sprites[recipe.fillSprite];
            var interior = Read(sources[0], size / 2, size / 2);
            var shallow = string.IsNullOrEmpty(recipe.shallowSprite) ? default : Read(sprites[recipe.shallowSprite], size / 2, size / 2);
            var shape = new Color32[size * size];
            var pixels = new Color32[atlas.width * atlas.height];
            for (int mask = 0; mask < variants.Length; mask++)
            {
                int key = mask & 15;
                if ((mask & 19) == 19) key |= 16;
                if ((mask & 38) == 38) key |= 32;
                if ((mask & 76) == 76) key |= 64;
                if ((mask & 137) == 137) key |= 128;
                if (!index.TryGetValue(key, out var sprite))
                {
                    int left = (index.Count % 8) * size, bottom = (index.Count / 8) * size;
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        bool west = x < size / 2, north = y >= size / 2;
                        int corner = (north ? 0 : 2) + (west ? 0 : 1);
                        bool h = (key & (west ? 8 : 2)) != 0, v = (key & (north ? 1 : 4)) != 0;
                        int diagonal = north ? (west ? 128 : 16) : (west ? 64 : 32);
                        bool inner = h && v && (key & diagonal) == 0;
                        int sample = inner ? 9 + corner : !v && !h ? 5 + corner :
                            !v ? (north ? 1 : 3) : !h ? (west ? 4 : 2) : 0;

                        int u = !h || inner ? 2 * (x % (size / 2)) : x;
                        int w = !v || inner ? 2 * (y % (size / 2)) : y;
                        if (recipe.surface == WorkshopTerrainTile.Surface.Shore)
                        {
                            // The path samples put the bank near their center. Use the inward-facing
                            // half at native density so a one-cell water channel stays visibly wide.
                            if (!h || inner) u = west ? x + size / 2 : x - size / 2;
                            if (!v || inner) w = north ? y - size / 2 : y + size / 2;
                        }
                        shape[y * size + x] = Read(sources[sample], u, w);
                    }
                    bool Inside(int x, int y)
                    {
                        if (x < 0 && (key & 8) == 0 || x >= size && (key & 2) == 0 ||
                            y < 0 && (key & 4) == 0 || y >= size && (key & 1) == 0) return false;
                        return shape[Mathf.Clamp(y, 0, size-1) * size + Mathf.Clamp(x, 0, size-1)].Equals(interior);
                    }
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Color32 color = shape[y * size + x];
                        if (fill != null && Inside(x, y))
                        {
                            bool bank = recipe.surface == WorkshopTerrainTile.Surface.Shore &&
                                !(Inside(x-1,y) && Inside(x+1,y) && Inside(x,y-1) && Inside(x,y+1));
                            color = bank ? shallow : Read(fill, x, y);
                        }
                        else if (recipe.surface == WorkshopTerrainTile.Surface.Shore)
                            color = Inside(x-1,y) || Inside(x+1,y) || Inside(x,y-1) || Inside(x,y+1)
                                ? interior : default; // A one-pixel earth lip over the original floor.
                        pixels[(bottom+y)*atlas.width+left+x] = color;
                    }
                    sprite = Sprite.Create(atlas, new Rect(left, bottom, size, size), Vector2.one * .5f,
                        sources[0].pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = recipe.title + "-" + key;
                    ctx.AddObjectToAsset("mask-" + key, sprite);
                    index.Add(key, sprite);
                }
                variants[mask] = sprite;
            }
            atlas.SetPixels32(pixels);
            atlas.Apply(false, true);
            ctx.AddObjectToAsset("atlas", atlas);
            var connections = recipe.connects is { Length: > 0 } ? recipe.connects.Select(name => sprites[name]).ToArray() : sources;
            tile.Initialize(recipe.title, recipe.surface, variants, connections);
            ctx.AddObjectToAsset("terrain", tile);
            ctx.SetMainObject(tile);
        }
        finally { DestroyImmediate(sheet); }
    }
}

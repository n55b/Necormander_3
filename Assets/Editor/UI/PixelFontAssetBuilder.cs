using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

public static class PixelFontAssetBuilder
{
    private const string Sources = "Assets/TextMesh Pro/Fonts/";
    private const string Destination = "Assets/Resources/PixelFonts";
    private static readonly string[] ContentRoots =
        { "Assets/Scenes", "Assets/Prefabs", "Assets/Resources", "Assets/SOData", "Assets/Settings/Localization", "Assets/Scripts", "DATA" };
    private static readonly HashSet<string> ContentExtensions = new() { ".cs", ".asset", ".prefab", ".unity" };
    private static readonly Regex EscapedCharacter = new(@"\\u([A-Fa-f0-9]{4})", RegexOptions.CultureInvariant);

    [MenuItem("Tools/UI/Rebuild Pixel Font Atlases")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        if (!AssetDatabase.IsValidFolder(Destination))
            AssetDatabase.CreateFolder("Assets/Resources", "PixelFonts");

        uint[] characters = CollectCharacters();
        Bake("Galmuri7", "Micro", 8, characters, 512);
        Bake("Galmuri9", "Small", 10, characters, 512);
        var bold = Bake("Galmuri11-Bold", "Emphasis", 12, characters, 512);
        var body = Bake("Galmuri11", "Body", 12, characters, 512);
        Bake("Galmuri14", "Large", 15, characters, 1024);

        body.fontWeightTable[7] = new TMP_FontWeightPair { regularTypeface = bold };
        EditorUtility.SetDirty(body);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PixelFontAssetBuilder] Five static bitmap faces baked from {characters.Length} used character candidates.");
    }

    private static uint[] CollectCharacters()
    {
        var result = new SortedSet<uint>(Enumerable.Range(32, 95).Select(value => (uint)value));
        foreach (char symbol in "×÷±−–—…·％°↑↓←→★☆♥♡▶◀◆■※　")
            result.Add(symbol);

        foreach (string directory in ContentRoots)
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (path.Replace('\\', '/').StartsWith(Destination + "/", StringComparison.Ordinal) ||
                !(directory == "DATA" ? Path.GetExtension(path) == ".json" : ContentExtensions.Contains(Path.GetExtension(path))))
                continue;
            string contents = File.ReadAllText(path);
            foreach (char value in contents)
                if (IsGameCharacter(value)) result.Add(value);
            foreach (Match match in EscapedCharacter.Matches(contents))
            {
                char value = (char)Convert.ToInt32(match.Groups[1].Value, 16);
                if (IsGameCharacter(value)) result.Add(value);
            }
        }
        return result.ToArray();
    }

    private static bool IsGameCharacter(char value)
        => value is >= '\u00A0' and <= '\u024F'
            or >= '\u1100' and <= '\u11FF'
            or >= '\u2010' and <= '\u2044'
            or >= '\u2190' and <= '\u27BF'
            or >= '\u3000' and <= '\u3163'
            or >= '\uAC00' and <= '\uD7A3';

    private static TMP_FontAsset Bake(string source, string key, int size, uint[] characters, int atlasSize)
    {
        var face = AssetDatabase.LoadAssetAtPath<Font>(Sources + source + ".ttf");
        if (face == null) throw new FileNotFoundException($"Pixel font source {source}.");

        string path = $"{Destination}/{key}.asset";
        var saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        // Published room names and older saves may still use glyphs absent from today's sources.
        if (saved != null)
            characters = characters.Concat(saved.characterTable.Select(c => c.unicode)).Distinct().OrderBy(c => c).ToArray();
        var baked = TMP_FontAsset.CreateFontAsset(face, size, 1, GlyphRenderMode.RASTER_HINTED,
            atlasSize, atlasSize, AtlasPopulationMode.Dynamic, false);
        baked.name = "Pixel " + key;
        baked.TryAddCharacters(characters, out uint[] missing);
        if (missing != null && missing.Contains(0x2212u) && baked.characterLookupTable.TryGetValue('-', out var hyphen))
        {
            baked.characterTable.Add(new TMP_Character(0x2212, hyphen.glyph));
            missing = missing.Where(value => value != 0x2212u).ToArray();
        }
        baked.atlasPopulationMode = AtlasPopulationMode.Static;
        baked.boldSpacing = 0f;
        baked.boldStyle = 0f;
        baked.normalSpacingOffset = 0f;
        var atlas = baked.atlasTexture;
        atlas.name = baked.name + " Atlas";
        atlas.filterMode = FilterMode.Point;
        atlas.wrapMode = TextureWrapMode.Clamp;
        baked.material.name = baked.name + " Material";
        if (saved == null)
        {
            AssetDatabase.CreateAsset(baked, path);
            AssetDatabase.AddObjectToAsset(atlas, baked);
            AssetDatabase.AddObjectToAsset(baked.material, baked);
            saved = baked;
        }
        else
        {
            var material = baked.material;
            EditorUtility.CopySerialized(atlas, saved.atlasTexture);
            EditorUtility.CopySerialized(material, saved.material);
            saved.material.mainTexture = saved.atlasTexture;
            baked.atlasTextures = saved.atlasTextures;
            baked.material = saved.material;
            EditorUtility.CopySerialized(baked, saved);
            Object.DestroyImmediate(atlas);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(baked);
        }

        saved.ReadFontAssetDefinition();
        EditorUtility.SetDirty(saved);
        Debug.Log($"[PixelFontAssetBuilder] {key} {size}px — {saved.characterTable.Count} glyphs, missing {string.Join(" ", (missing ?? Array.Empty<uint>()).Select(value => $"U+{value:X4}"))}.");
        return saved;
    }
}

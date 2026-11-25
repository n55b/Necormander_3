using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Import art sprites without resampling or block compression.</summary>
public sealed class PixelSpriteImportPolicy : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (assetImporter is not TextureImporter importer ||
            (importer.textureType != TextureImporterType.Sprite &&
             assetPath != "Assets/TextMesh Pro/Sprites/EmojiOne.png")) return;

        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.streamingMipmaps = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;

        // A platform max smaller than a strip silently changes its imported PPU.
        int required = 0;
        if (assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
        {
            string path = Path.GetFullPath(assetPath);
#if UNITY_EDITOR_WIN
            if (path.Length >= 260)
                path = path.StartsWith(@"\\",System.StringComparison.Ordinal)
                    ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
#endif
            using var file = File.OpenRead(path);
            var header = new byte[24];
            if (file.Read(header, 0, header.Length) == header.Length && header[0] == 137 && header[1] == 80)
            {
                int WidthAt(int i) => (header[i] << 24) | (header[i+1] << 16) | (header[i+2] << 8) | header[i+3];
                required = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(WidthAt(16), WidthAt(20))), 32, 16384);
                if (importer.maxTextureSize < required) importer.maxTextureSize = required;
            }
        }
        foreach (string platform in new[] { "Standalone", "Android", "iOS", "WebGL" })
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            if (!settings.overridden) continue;
            settings.maxTextureSize = Mathf.Max(required,settings.maxTextureSize);
            settings.format = TextureImporterFormat.RGBA32;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}

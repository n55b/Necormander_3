using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

public static class PixelTypographyCheck
{
    private static readonly string[] Roots = { "Assets/Prefabs", "Assets/Resources" };
    private static readonly Regex RichTextTag = new("<[^<>]+>");

    [MenuItem("Tools/UI/Verify Pixel Typography")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run in Edit Mode.");
        foreach (var (name, pixels) in new[] { ("Micro", 8), ("Small", 10), ("Body", 12), ("Emphasis", 12), ("Large", 15) })
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"Assets/Resources/PixelFonts/{name}.asset");
            Require(font != null && font.faceInfo.pointSize == pixels && IsBitmap(font), $"native font {name} at {pixels}px");
            Require(font.HasCharacters("English 한국어 체력 콘텐츠 수치 123 / +−%"), $"{name} bilingual sample");
            foreach (var texture in font.atlasTextures)
            {
                Require(texture.format == TextureFormat.Alpha8 && texture.isReadable, name + " glyph alpha texture");
                foreach (byte alpha in texture.GetRawTextureData<byte>())
                    if (alpha != 0 && alpha != 255)
                        throw new InvalidOperationException($"[PixelTypographyCheck] {name} contains filtered glyph alpha ({alpha}).");
            }
        }

        int prefabCount = 0;
        int sceneCount = 0;
        var report = new System.Text.StringBuilder("asset\tlabel\tanchor\tlocalSize\tparentScale\tfont\n");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", Roots))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root != null) prefabCount += Inspect(root, path, report);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    sceneCount += Inspect(root, path, report);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        if (Application.isBatchMode)
        {
            Directory.CreateDirectory(ProjectVerification.Output);
            File.WriteAllText(Path.Combine(ProjectVerification.Output, "pixel-typography.tsv"), report.ToString());
        }
        Require(prefabCount > 0 && sceneCount > 0, "authored text inventory");
        Debug.Log($"[PixelTypographyCheck] PASS — {prefabCount} prefab labels, {sceneCount} scene instances, five monochrome static atlases.");
    }

    private static int Inspect(GameObject root, string file, System.Text.StringBuilder report)
    {
        var legacy = root.GetComponentInChildren<Text>(true);
        Require(legacy == null, $"{file}: legacy UI Text at {legacy?.name}");

        var labels = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (var label in labels)
        {
            string asset = $"{file}: {label.name}";
            Require(label.TryGetComponent<PixelText>(out _) && IsBitmap(label.font) && !label.enableAutoSizing,
                $"{asset}: authored PixelText and bitmap");
            Require(label.fontSharedMaterial != null && label.fontSharedMaterial.mainTexture == label.font.atlasTexture,
                $"{asset}: font material uses the bitmap atlas");
            string words = RichTextTag.Replace(label.text ?? "", "").Trim();
            Require(words.Length == 0 || label.font.HasCharacters(words),
                $"{asset}: authored glyph coverage ({string.Join(" ", words.Distinct().Where(c => !char.IsWhiteSpace(c) && !label.font.HasCharacter(c)))})");
            var rect = label.rectTransform;
            report.Append(file).Append('\t').Append(AnimationUtility.CalculateTransformPath(rect, root.transform)).Append('\t')
                .Append(rect.anchoredPosition).Append('\t').Append(rect.rect.size).Append('\t')
                .Append(rect.lossyScale).Append('\t').AppendLine(label.font.name);
        }
        return labels.Length;
    }

    private static bool IsBitmap(TMP_FontAsset font)
        => font != null && font.atlasPopulationMode == AtlasPopulationMode.Static
            && AssetDatabase.GetAssetPath(font).StartsWith("Assets/Resources/PixelFonts/", StringComparison.Ordinal)
            && font.atlasRenderMode == GlyphRenderMode.RASTER_HINTED
            && font.atlasTextures.All(texture => texture != null && texture.filterMode == FilterMode.Point && texture.mipmapCount == 1);

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("[PixelTypographyCheck] " + label);
    }
}

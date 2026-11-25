using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Texel coordinates are relative to the sprite rectangle, so changes to pivot, PPU and pose are respected.
public static class SpriteGroundContacts
{
    public const string ResourcePath = "WorldAtmosphere/SpriteGroundContacts";
    [Serializable] public sealed class Table { public Sheet[] sheets; }
    [Serializable] public sealed class Sheet { public string texture; public Entry[] frames; }
    [Serializable] public struct Entry { public RectInt rect; public Vector2 low, high; }
    private static Dictionary<(string, RectInt), Entry> entries;
    private static readonly Dictionary<Sprite, Entry> poses = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        entries = null;
        poses.Clear();
        SceneManager.sceneUnloaded -= ReleasePoses;
        SceneManager.sceneUnloaded += ReleasePoses;
    }

    // Keep the lookup table, but release sprite and atlas references from the departing floor.
    private static void ReleasePoses(Scene scene) => poses.Clear();

    internal static Vector3 Resolve(SpriteRenderer picture, Vector3 fallback)
    {
        var sprite = picture != null ? picture.sprite : null;
        if (sprite == null || picture.drawMode != SpriteDrawMode.Simple) return fallback;
        if (!poses.TryGetValue(sprite, out var entry))
        {
            if (entries == null)
            {
                entries = new();
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (asset != null)
                    foreach (var sheet in JsonUtility.FromJson<Table>(asset.text).sheets)
                    foreach (var frame in sheet.frames) entries[(sheet.texture, frame.rect)] = frame;
            }
            var r = sprite.rect;
            entries.TryGetValue((sprite.texture.name, new RectInt((int)r.x,(int)r.y,(int)r.width,(int)r.height)), out entry);
            poses.Add(sprite, entry);
        }
        if (entry.rect.width == 0) return fallback;
        var matrix = picture.localToWorldMatrix;
        bool inverted = (matrix.m11 < 0) != picture.flipY;
        Vector2 local = ((inverted ? entry.high : entry.low) - sprite.pivot) / sprite.pixelsPerUnit;
        if (picture.flipX) local.x = -local.x;
        if (picture.flipY) local.y = -local.y;
        return matrix.MultiplyPoint3x4(local);
    }
}

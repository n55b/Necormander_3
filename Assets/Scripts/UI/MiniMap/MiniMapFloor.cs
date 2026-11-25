using System.Collections.Generic;
using UnityEngine;

/// <summary>Room-local floor geometry shared by terrain rendering and marker projection.</summary>
public sealed class MiniMapFloor
{
    public IReadOnlyList<Vector2> Tiles { get; }
    public Vector2 Center { get; }
    public Vector2 Span { get; }

    public MiniMapFloor(List<Vector2> tiles)
    {
        Tiles = tiles;
        if (tiles.Count == 0) return;

        Vector2 min = tiles[0], max = tiles[0];
        foreach (var tile in tiles)
        {
            min = Vector2.Min(min, tile);
            max = Vector2.Max(max, tile);
        }
        Center = (min + max) * 0.5f;
        Span = max - min + Vector2.one;

        // Draw from top to bottom and left to right so neighboring dots cover interior shadows.
        tiles.Sort((a, b) => !Mathf.Approximately(a.y, b.y) ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));
    }
}

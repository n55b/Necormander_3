using UnityEngine;
using UnityEngine.Tilemaps;

// Cardinal neighbors choose the silhouette. A diagonal matters only when both adjacent sides connect.
public sealed class WorkshopTerrainTile : TileBase
{
    [SerializeField] private Sprite[] masks;
    [SerializeField] private Sprite[] sourceSprites;

    public enum Surface { Floor, Wall, Cliff, Shore }
    [SerializeField] private Surface surface;
    public Surface Kind => surface;
    public int PaintLayer => surface == Surface.Floor ? 0 : 1;

    [SerializeField] private string title;
    public string Title => title;
    public Sprite PaletteSprite => masks[0];
    public void Initialize(string label, Surface kind, Sprite[] variants, Sprite[] sources)
    {
        surface = kind;
        title = label;
        masks = variants;
        sourceSprites = sources;
    }

    public override void GetTileData(Vector3Int position, ITilemap map, ref TileData data)
    {
        int mask = 0;
        bool Connect(int x, int y)
        {
            var neighbor = map.GetTile(position + new Vector3Int(x, y, 0));
            if (neighbor == this) return true;
            Sprite sprite = neighbor switch
            {
                Tile authored => authored.sprite,
                AnimatedTile animated when animated.m_AnimatedSprites is { Length: > 0 } => animated.m_AnimatedSprites[0],
                _ => null
            };
            return sprite != null && System.Array.IndexOf(sourceSprites, sprite) >= 0;
        }
        if (Connect(0, 1)) mask |= 1;
        if (Connect(1, 0)) mask |= 2;
        if (Connect(0, -1)) mask |= 4;
        if (Connect(-1, 0)) mask |= 8;
        if ((mask & 3) == 3 && Connect(1, 1)) mask |= 16;
        if ((mask & 6) == 6 && Connect(1, -1)) mask |= 32;
        if ((mask & 12) == 12 && Connect(-1, -1)) mask |= 64;
        if ((mask & 9) == 9 && Connect(-1, 1)) mask |= 128;
        data.sprite = masks[mask];
        data.transform = Matrix4x4.identity;
        data.color = Color.white;
        data.flags = TileFlags.LockTransform;
        data.colliderType = surface == Surface.Floor ? Tile.ColliderType.Sprite : Tile.ColliderType.Grid;
    }

    // A hand-painted Tile does not notify neighboring terrain brushes when its art changes.
    public static void RefreshNeighbors(Tilemap map, Vector3Int center)
    {
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            var at = center + new Vector3Int(x, y, 0);
            if (map.GetTile(at) is WorkshopTerrainTile) map.RefreshTile(at);
        }
    }

    public override void RefreshTile(Vector3Int position, ITilemap map)
    {
        map.RefreshTile(position);
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0) continue;
            var neighbor = position + new Vector3Int(x, y, 0);
            if (map.GetTile(neighbor) is WorkshopTerrainTile) map.RefreshTile(neighbor);
        }
    }
}

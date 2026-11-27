#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

internal sealed partial class WorkshopPreview
{
    internal static readonly Color32 RemovedColor = new(246, 116, 103, 145);
    internal static readonly Color32 AddedColor = new(129, 175, 248, 145);
    internal static readonly Color32 ReachColor = new(96, 203, 166, 66);
    internal static readonly Color32 PassageColor = new(96, 221, 194, 142);
    internal static readonly Color32 ArrivalColor = new(255, 212, 103, 215);
    private Tilemap overlay;
    private Tile overlayTile, outlinedTile;
    private Texture2D outlineTexture;

    private Tile CreateOverlayTile(Sprite sprite)
    {
        var tile=ScriptableObject.CreateInstance<Tile>();
        tile.sprite=sprite;
        tile.flags=TileFlags.None;
        tile.colliderType=Tile.ColliderType.None;
        return tile;
    }

    private Tile CreateOutlinedTile()
    {
        const int size=16;
        outlineTexture=new Texture2D(size,size,TextureFormat.RGBA32,false) { filterMode=FilterMode.Point, wrapMode=TextureWrapMode.Clamp };
        var pixels=new Color32[size*size];
        for(int y=0;y<size;y++)
        for(int x=0;x<size;x++)
            pixels[y*size+x]=new Color32(255,255,255,(byte)(x==0 || y==size-1 ? 250 : 128));
        outlineTexture.SetPixels32(pixels);
        outlineTexture.Apply(false,true);
        return CreateOverlayTile(Sprite.Create(outlineTexture,new Rect(0,0,size,size),Vector2.one*.5f,size,0,SpriteMeshType.FullRect));
    }

    private readonly List<(int layer, Vector3Int cell)> passageChanges = new();
    internal bool HasDoor(Vector2Int direction) => doors.ContainsKey(direction) && maps[1] != null;

    private void PaintOverlay(Dictionary<Vector3Int, Color32> colors)
    {
        if (overlay == null)
        {
            var node = new GameObject("Passage overlay", typeof(Tilemap), typeof(TilemapRenderer)) { layer = Layer };
            node.transform.SetParent(Ground.transform.parent, false);
            node.transform.localPosition = Ground.transform.localPosition;
            node.transform.localRotation = Ground.transform.localRotation;
            node.transform.localScale = Ground.transform.localScale;
            overlay = node.GetComponent<Tilemap>();
            overlay.tileAnchor = Ground.tileAnchor;
            var renderer = node.GetComponent<TilemapRenderer>();
            renderer.sortingLayerName = "UI";
            renderer.sortingOrder = 14999;
            renderer.sharedMaterial = unlit;
            overlayTile = CreateOverlayTile(marker);
            outlinedTile = CreateOutlinedTile();
        }
        overlay.ClearAllTiles();
        var scale = Ground.layoutGrid.cellSize;
        var matrix = Matrix4x4.Scale(new Vector3(scale.x, scale.y, 1));
        overlay.SetTiles(colors.Select(p => new TileChangeData(p.Key,
            p.Value.Equals(ReachColor) || p.Value.Equals(ArrivalColor) ? overlayTile : outlinedTile, p.Value, matrix)).ToArray(), true);
    }

    internal void ShowOverlay(bool visible)
    {
        if (overlay != null) overlay.gameObject.SetActive(visible);
    }

    internal void ShowDoorMarkers(bool visible)
    {
        foreach (var node in doors.Values) node.gameObject.SetActive(visible);
        ShowExitPassages(visible);
    }

    // The editor's uncut drawing is the reference for restoring both tile identity and painted styles.
    internal (Vector3 focus, int removed, int added, int reachable) PreviewPassage(
        WorkshopPreview original, Vector2Int direction, TileBase floor)
    {
        foreach (var (layer, cell) in passageChanges)
        {
            var source = original.maps[layer];
            maps[layer].SetTile(new TileChangeData(cell, source.GetTile(cell), source.GetColor(cell), source.GetTransformMatrix(cell)), true);
            if (layer <= 1) WorkshopTerrainTile.RefreshNeighbors(maps[layer], cell);
        }
        passageChanges.Clear();
        var node = doors[direction];
        var anchor = node.GetComponent<RoomAnchor>() ?? node.gameObject.AddComponent<RoomAnchor>();
        anchor.direction = direction;
        var before = new Dictionary<Vector3Int, Color32>();
        var removed = new HashSet<Vector3Int>();
        int added = 0;
        anchor.OpenPassage(Ground, maps[1], maps[2], maps[3], floor, (map, cell) =>
        {
            passageChanges.Add((System.Array.IndexOf(maps, map), cell));
            if (map == Ground) { added++; before.TryAdd(cell, AddedColor); }
            else { removed.Add(cell); before[cell] = RemovedColor; }
        });
        original.PaintOverlay(before);

        var arrival = maps[1].WorldToCell(anchor.ArrivalPoint(maps[1]));
        var after = new Dictionary<Vector3Int, Color32>();
        var pending = new Queue<Vector3Int>();
        void Visit(Vector3Int cell)
        {
            if (after.ContainsKey(cell) || !Walkable(Ground.GetCellCenterWorld(cell))) return;
            after.Add(cell, ReachColor);
            pending.Enqueue(cell);
        }
        Visit(arrival);
        while (pending.Count > 0)
        {
            var cell = pending.Dequeue();
            Visit(cell + Vector3Int.up); Visit(cell + Vector3Int.down);
            Visit(cell + Vector3Int.left); Visit(cell + Vector3Int.right);
        }
        foreach (var cell in anchor.PassageCells(maps[1], false))
            if (after.ContainsKey(cell)) after[cell] = PassageColor;
        if (after.ContainsKey(arrival)) after[arrival] = ArrivalColor;
        PaintOverlay(after);
        var focus = Local((anchor.ArrivalPoint(maps[1]) + node.position) * .5f);
        return (focus, removed.Count, added, after.Count);
    }
}
#endif

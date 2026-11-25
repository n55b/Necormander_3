using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// An R8 cell stores open floor (255) or solid/void (0). Rebuilt once per floor and patched on tile changes.
[DisallowMultipleComponent]
public sealed class WorldFloorMask : MonoBehaviour
{
    private Tilemap ground, wall, blocked;
    private BoundsInt bounds;
    private Texture2D cells;
    private byte[] bytes;
    private bool fullUpload, resize;
    private readonly Dictionary<DoorUpController,HashSet<Vector3Int>> doorFootprints=new();
    private readonly Dictionary<Vector3Int,int> barriers=new();
    internal void DoorChanged(DoorUpController door,bool closed)
    {
        if(bytes==null || door.gameObject.scene!=gameObject.scene) return;
        if(doorFootprints.TryGetValue(door,out var previous))
        {
            foreach(var cell in previous)
            {
                if(--barriers[cell]==0) barriers.Remove(cell);
                Mark(cell);
            }
            doorFootprints.Remove(door);
        }
        if(!closed) return;
        var cells=new HashSet<Vector3Int>();
        foreach(var barrier in door.Barriers)
        {
            if(!barrier.enabled || barrier.isTrigger || !barrier.gameObject.activeInHierarchy) continue;
            var region=barrier.bounds;
            var first=ground.WorldToCell(region.min);
            var last=ground.WorldToCell(region.max-Vector3.one*.0001f);
            for(int y=first.y;y<=last.y;y++)
            for(int x=first.x;x<=last.x;x++)
            {
                var cell=new Vector3Int(x,y,bounds.zMin);
                if(bounds.Contains(cell)) cells.Add(cell);
            }
        }
        if(cells.Count==0)
        {
            var anchor=ground.WorldToCell(door.transform.position);
            if(bounds.Contains(anchor)) cells.Add(anchor);
        }
        foreach(var cell in cells) {barriers.TryGetValue(cell,out int count);barriers[cell]=count+1;Mark(cell);}
        doorFootprints.Add(door,cells);
    }
    private void Mark(Vector3Int cell)
    {
        if(!bounds.Contains(cell) || !Write(cell)) return;
        int x=(cell.x-bounds.xMin)/PageSize, y=(cell.y-bounds.yMin)/PageSize;
        changedPages.Add(y*PageColumns+x);
    }
    private const int PageSize = 32;
    private readonly HashSet<int> changedPages = new();
    private readonly byte[] patchBytes = new byte[PageSize * PageSize];
    private Texture2D patch;
    private Matrix4x4 worldToGrid;
    internal bool IsOpen(Vector3 point)
    {
        if (ground == null || bytes == null) return false;
        var cell = ground.WorldToCell(point);
        return bounds.Contains(cell) && bytes[(cell.y - bounds.yMin) * bounds.size.x + cell.x - bounds.xMin] != 0;
    }
    public Texture2D Texture => cells;
    public Matrix4x4 WorldToUV => worldToGrid;

    internal static WorldFloorMask Attach(Tilemap ground)
    {
        if (ground == null) return null;
        if (ground.TryGetComponent<WorldFloorMask>(out var field)) return field;
        field = ground.gameObject.AddComponent<WorldFloorMask>();
        field.ground = ground;
        field.wall = ground.transform.parent?.Find("Wall")?.GetComponent<Tilemap>();
        field.blocked = ground.transform.parent?.Find("Unsteppable")?.GetComponent<Tilemap>();
        field.resize = true;
        return field;
    }
    private bool trackingChanges;
    private void OnEnable() => resize = true;
    private void OnDisable() => StopTracking();

    internal void BeginFloorGeneration()
    {
        resize = true;
        StopTracking();
    }

    private void StopTracking()
    {
        if (!trackingChanges) return;
        Tilemap.tilemapTileChanged -= Changed;
        trackingChanges = false;
    }

    private void TrackChanges()
    {
        if (trackingChanges || !isActiveAndEnabled) return;
        Tilemap.tilemapTileChanged += Changed;
        trackingChanges = true;
    }
    private void Changed(Tilemap map, Tilemap.SyncTile[] changes)
    {
        if (ground == null || (map != ground && map != wall && map != blocked) || resize) return;
        if (bytes == null || (MapGenerator.Instance != null && MapGenerator.Instance.IsBuilding(ground))) { resize = true; return; }
        foreach (var tile in changes)
        {
            if (!bounds.Contains(tile.position)) { resize = true; return; }
            Mark(tile.position);
        }
    }
    private int PageColumns => (bounds.size.x + PageSize - 1) / PageSize;
    private bool Write(Vector3Int cell)
    {
        int index = (cell.y - bounds.yMin) * bounds.size.x + cell.x - bounds.xMin;
        byte next = !barriers.ContainsKey(cell) && ground.HasTile(cell) && !(wall != null && wall.HasTile(cell)) && !(blocked != null && blocked.HasTile(cell)) ? (byte)255 : (byte)0;
        if (bytes[index] == next) return false;
        bytes[index] = next;
        return true;
    }
    internal void Prepare()
    {
        if (MapGenerator.Instance != null && MapGenerator.Instance.IsBuilding(ground)) return;
        if (resize)
        {
            var build = Rebuild(false);
            while (build.MoveNext()) { }
            TrackChanges();
        }
        Upload();
    }

    internal IEnumerator PrepareGeneratedFloor()
    {
        yield return Rebuild(true);
        Upload();
        TrackChanges();
    }

    private IEnumerator Rebuild(bool staged)
    {
        resize = false;
        bounds = ground.cellBounds;
        int w = bounds.size.x, h = bounds.size.y;
        changedPages.Clear();
        if (w == 0 || h == 0 || w > SystemInfo.maxTextureSize || h > SystemInfo.maxTextureSize)
        {
            if (cells != null) Destroy(cells);
            cells = null; bytes = null; fullUpload = false;
            yield break;
        }
        if (cells == null || cells.width != w || cells.height != h)
        {
            if (cells != null) Destroy(cells);
            cells = new Texture2D(w, h, TextureFormat.R8, false, true) { name = "Room floor mask", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }
        bytes = new byte[w * h];
        doorFootprints.Clear(); barriers.Clear();
        var origin = ground.CellToWorld(bounds.min);
        var x = (ground.CellToWorld(bounds.min + Vector3Int.right) - origin) * w;
        var y = (ground.CellToWorld(bounds.min + Vector3Int.up) - origin) * h;
        var toWorld = Matrix4x4.identity;
        toWorld.SetColumn(0, x); toWorld.SetColumn(1, y);
        toWorld.SetColumn(3, new Vector4(origin.x, origin.y, origin.z, 1));
        worldToGrid = toWorld.inverse;
        // Bound each native read as well as the managed loop. Sparse dungeon bounds can span several rooms.
        const int cellsPerRead = 2048;
        int rows = Mathf.Clamp(cellsPerRead / w, 1, h);
        var tiles = System.Buffers.ArrayPool<TileBase>.Shared.Rent(rows * w);
        try
        {
            double deadline = Time.realtimeSinceStartupAsDouble + .002;
            foreach (var layer in new[] {ground, wall, blocked})
            {
                if (layer == null) continue;
                byte value = layer == ground ? (byte)255 : (byte)0;
                for (int z = bounds.zMin; z < bounds.zMax; z++)
                for (int row = 0; row < h; row += rows)
                {
                    int height = Mathf.Min(rows, h - row);
                    var strip = new BoundsInt(bounds.xMin, bounds.yMin + row, z, w, height, 1);
                    layer.GetTilesBlockNonAlloc(strip, tiles);
                    int offset = row * w;
                    for (int i = 0; i < w * height; i++)
                        if (tiles[i] != null) bytes[offset + i] = value;
                    if (staged && Time.realtimeSinceStartupAsDouble >= deadline)
                    {
                        yield return null;
                        deadline = Time.realtimeSinceStartupAsDouble + .002;
                    }
                }
            }
        }
        finally { System.Buffers.ArrayPool<TileBase>.Shared.Return(tiles, clearArray: true); }
        foreach(var door in DoorUpController.Closed) if(door!=null) DoorChanged(door,true);
        fullUpload = true;
    }

    private void Upload()
    {
        if (cells == null || (!fullUpload && changedPages.Count == 0)) return;
        if (fullUpload || (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) == 0)
        {
            cells.LoadRawTextureData(bytes);
            cells.Apply(false, false);
            fullUpload = false;
        }
        else
        {
            if (patch == null) patch = new Texture2D(PageSize,PageSize,TextureFormat.R8,false,true) { name = "Floor mask update" };
            foreach (int page in changedPages)
            {
                int x = (page % PageColumns) * PageSize, y = (page / PageColumns) * PageSize;
                int w = Mathf.Min(PageSize, cells.width - x), h = Mathf.Min(PageSize,cells.height - y);
                for (int row=0; row<h; row++)
                    System.Buffer.BlockCopy(bytes,(y+row)*cells.width+x,patchBytes,row*PageSize,w);
                patch.LoadRawTextureData(patchBytes);
                patch.Apply(false,false);
                Graphics.CopyTexture(patch,0,0,0,0,w,h,cells,0,0,x,y);
            }
        }
        changedPages.Clear();
    }
    internal void Bind(Material target)
    {
        if (cells == null) { target.SetFloat("_HasFloorMask", 0); return; }
        target.SetFloat("_HasFloorMask", 1);
        target.SetTexture("_FloorMask", cells);
        target.SetMatrix("_FloorUV", worldToGrid);
    }
    private void OnDestroy() { if (cells != null) Destroy(cells); if (patch != null) Destroy(patch); }
}

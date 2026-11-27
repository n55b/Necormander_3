#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

// The room drawing on the workbench: stage, copied artwork and cleanup.
// A rendering-only copy keeps traps, enemy AI, shops and persistent events dormant.
internal sealed partial class WorkshopPreview : System.IDisposable
{
    public const int Width = 434, Height = 272;
    private readonly GameObject stage;
    private readonly Camera camera;
    private readonly Material unlit;
    private readonly Dictionary<Texture,Material> spriteMaterials = new();
    private readonly Transform art;
    private readonly Dictionary<string,Transform> pieces = new();
    private readonly Dictionary<string, SpriteRenderer[]> artwork = new();
    private readonly Dictionary<Vector2Int,Transform> doors = new();
    private readonly Sprite marker;
    private Transform brush, startMarker;
    public bool Walkable(Vector3 point)
    {
        bool Covers(Tilemap map)=>map!=null && map.HasTile(map.WorldToCell(point));
        return Covers(Ground) && !Covers(maps[1]) && !Covers(maps[3]);
    }

    private readonly Tilemap[] maps = new Tilemap[4];
    public RenderTexture Frame { get; }
    public Vector2 Center;
    public int Zoom = 1;
    public Tilemap Ground => maps[0];
    public Tilemap Map(int layer) => maps[layer];
    public Vector3 World(Vector3 local) => art.TransformPoint(local);
    public Vector3 Local(Vector3 world) => art.InverseTransformPoint(world);
    public IEnumerable<KeyValuePair<string,Transform>> Pieces => pieces;
    public string PieceName(string key) => pieces.TryGetValue(key,out var value) ? value.name : "";
    private const int Layer = 31;

    public WorkshopPreview(WorkshopCatalog.Template source, WorkshopRoom doc, WorkshopCatalog library,bool comparison=false)
    {
        stage = new GameObject("Room drawing");
        stage.transform.position = new Vector3(comparison ? 8192 : 4096,4096);
        unlit = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        marker = Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,Texture2D.whiteTexture.width,Texture2D.whiteTexture.height),
            Vector2.one*.5f,Texture2D.whiteTexture.width);
        art = Copy(source.room.transform,stage.transform,source.room.transform,true);
        for (int i=0; i<maps.Length; i++) maps[i]=WorkshopCatalog.Map(art,i);
        WorkshopGeometry.ApplyTiles(doc,art,library);
        foreach (var anchor in source.room.GetComponentsInChildren<RoomAnchor>())
            if (!doors.ContainsKey(anchor.direction)) MarkDoor(anchor.direction,source.room.transform.InverseTransformPoint(anchor.transform.position));
        foreach (var e in doc.exits) MarkDoor(e.direction,e.local);
        foreach (var item in doc.pieces)
        {
            if (string.IsNullOrEmpty(item.source))
            {
                if (item.removed) continue;
                var piece = library.FindPiece(item.catalog);
                var node = Copy(piece.prefab.transform,art,null,true);
                pieces[item.id] = node;

            }
            if (pieces.TryGetValue(item.id,out var obj))
            { obj.position = World(item.local); obj.gameObject.SetActive(!item.removed); }
        }
        foreach (var piece in pieces)
        {
            artwork[piece.Key] = piece.Value.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(renderer => renderer.sprite != marker).ToArray();
            if (!artwork[piece.Key].Any(Visible) && piece.Value.gameObject.activeInHierarchy)
                AddMarker(piece.Value, WorkshopUI.Paper, .4f);
        }
        var holder = new GameObject("Workshop camera");
        holder.transform.SetParent(stage.transform,false);
        camera = holder.AddComponent<Camera>();
        camera.orthographic=true; camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color32(28,27,38,255); camera.cullingMask=1<<Layer;
        camera.nearClipPlane=.1f; camera.farClipPlane=100; camera.depth=-90;
        camera.transparencySortMode = TransparencySortMode.CustomAxis;
        camera.transparencySortAxis = Vector3.up;
        camera.allowHDR=false; camera.allowMSAA=false;
        Frame = new RenderTexture(Width,Height,16) {name="Workshop map",filterMode=FilterMode.Point};
        Frame.Create(); camera.targetTexture=Frame;
        float cellWidth=Vector3.Distance(Ground.CellToWorld(Vector3Int.zero),Ground.CellToWorld(Vector3Int.right));
        nativeDensity=16f/Mathf.Max(.001f,cellWidth);
        CellWidth=cellWidth;
        Overview();
        // An empty room has nothing to centre on yet; the fitted view shows its exits instead.
        if (maps.Any(map => map != null && map.GetUsedTilesCount() > 0)) Home();
    }

    private Transform Copy(Transform src, Transform parent, Transform root, bool first)
    {
        var go = new GameObject(src.name) { layer=Layer };
        // Minimap icons have their own gameplay camera; they never belong to the room artwork.
        bool mapIcon=src.gameObject.layer==LayerMask.NameToLayer("MiniMap") || src.name=="MiniMapIcons";
        var dst = go.transform;
        dst.SetParent(parent,false);
        dst.localPosition=first ? Vector3.zero : src.localPosition;
        dst.localRotation=first ? Quaternion.identity : src.localRotation;
        dst.localScale=src.localScale;
        if (src.TryGetComponent<UnityEngine.Rendering.SortingGroup>(out var group))
        {
            var copy=go.AddComponent<UnityEngine.Rendering.SortingGroup>();
            copy.sortingLayerID=group.sortingLayerID; copy.sortingOrder=group.sortingOrder;
            copy.sortAtRoot=group.sortAtRoot; copy.enabled=group.enabled;
        }
        if (src.TryGetComponent<Grid>(out var grid))
        {
            var copy=go.AddComponent<Grid>(); copy.cellSize=grid.cellSize; copy.cellGap=grid.cellGap;
            copy.cellLayout=grid.cellLayout; copy.cellSwizzle=grid.cellSwizzle;
        }
        if (src.TryGetComponent<Tilemap>(out var map))
        {
            var copy=go.AddComponent<Tilemap>();
            copy.tileAnchor=map.tileAnchor; copy.orientation=map.orientation; copy.color=map.color;
            if (map.orientation==Tilemap.Orientation.Custom) copy.orientationMatrix=map.orientationMatrix;
            var bounds=map.cellBounds;
            if (bounds.size.x>0 && bounds.size.y>0)
            {
                var end=bounds.max-Vector3Int.one;
                int count=map.GetTilesRangeCount(bounds.min,end);
                if (count>0)
                {
                    var positions=new Vector3Int[count]; var tiles=new TileBase[count];
                    map.GetTilesRangeNonAlloc(bounds.min,end,positions,tiles);
                    var data=new TileChangeData[count];
                    for (int i=0;i<count;i++)
                        data[i]=new TileChangeData(positions[i],tiles[i],map.GetColor(positions[i]),map.GetTransformMatrix(positions[i]));
                    copy.SetTiles(data,true);
                }
            }
            if (src.TryGetComponent<TilemapRenderer>(out var original))
            {
                var r=go.AddComponent<TilemapRenderer>();
                r.mode=original.mode; r.sortOrder=original.sortOrder; Setup(r,original);
            }
        }
        if (src.TryGetComponent<SpriteRenderer>(out var sprite) && sprite.sprite != null)
        {
            var r=go.AddComponent<SpriteRenderer>(); r.sprite=sprite.sprite; r.color=sprite.color;
            r.spriteSortPoint=sprite.spriteSortPoint;
            r.flipX=sprite.flipX; r.flipY=sprite.flipY; r.drawMode=sprite.drawMode;
            if (r.drawMode!=SpriteDrawMode.Simple) r.size=sprite.size;
            Setup(r,sprite);
            r.sharedMaterial=SpriteMaterial(sprite.sprite.texture);
        }
        if (root!=null && WorkshopGeometry.IsEditablePiece(root,src))
            pieces[WorkshopCatalog.PathOf(root,src)]=dst;
        for (int i=0;i<src.childCount;i++) Copy(src.GetChild(i),dst,root,false);
        // Saved fixed enemies wait in an inactive group until combat starts; the workbench still shows them.
        go.SetActive(!mapIcon && (first || src.gameObject.activeSelf || src.name == "Workshop encounter"));
        return dst;
    }
    private void Setup(Renderer copy,Renderer original)
    {
        copy.sharedMaterial=unlit; copy.sortingLayerID=original.sortingLayerID;
        copy.sortingOrder=original.sortingOrder; copy.enabled=original.enabled;
    }
    // Each sprite sheet owns a material binding. Changing visible tile chunks must not
    // replace the atlas sampled by a prop that happens to share its draw batch.
    private Material SpriteMaterial(Texture texture)
    {
        if (!spriteMaterials.TryGetValue(texture,out var material))
            spriteMaterials.Add(texture,material=new Material(unlit){mainTexture=texture});
        return material;
    }

    private Transform AddMarker(Transform parent,Color color,float scale)
    {
        var go=new GameObject("Map marker") {layer=Layer};
        go.transform.SetParent(parent,false);
        go.transform.localScale=new Vector3(scale,scale,1);
        var r=go.AddComponent<SpriteRenderer>();
        r.sprite=marker; r.color=color; r.sharedMaterial=unlit; r.sortingLayerName="UI"; r.sortingOrder=15000;
        return go.transform;
    }

    private bool disposed;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopFeedback();
        // Scene unload may destroy either root first. The render target and material are owned here.
        if (camera!=null)
        {
            camera.enabled=false;
            camera.targetTexture=null;
        }
        if (stage!=null)
        {
            stage.SetActive(false);
            Object.Destroy(stage);
        }
        if (Frame!=null)
        {
            Frame.Release();
            Object.Destroy(Frame);
        }
        if (outlinedTile != null) Object.Destroy(outlinedTile.sprite);
        Object.Destroy(outlinedTile);
        Object.Destroy(outlineTexture);
        Object.Destroy(overlayTile);
        DestroyCoverage();
        if (gridTile != null) Object.Destroy(gridTile.sprite);
        Object.Destroy(gridTile);
        Object.Destroy(gridTexture);
        Object.Destroy(marker);
        foreach (var material in spriteMaterials.Values) Object.Destroy(material);
        Object.Destroy(unlit);
    }
}
#endif

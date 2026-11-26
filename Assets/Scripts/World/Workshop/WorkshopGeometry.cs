using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class WorkshopGeometry
{
    public static bool IsEditablePiece(Transform root, Transform t)
    {
        if (t == null || t == root || t.parent == null || !t.IsChildOf(root)) return false;
        return t.parent.name is "DecorateObject" or "TrapObject" or "Tutorial Enemies";
    }

    public static void ApplyTiles(WorkshopRoom doc, Transform root, WorkshopCatalog library)
    {
        foreach (var group in doc.cells.GroupBy(c => c.layer))
        {
            var map = WorkshopCatalog.Map(root,group.Key);
            if (map == null) throw new System.IO.InvalidDataException("방에 타일 레이어가 없습니다.");
            var cells = group.ToArray();
            map.SetTiles(cells.Select(c => (Vector3Int)c.at).ToArray(),cells.Select(c => library.Tile(c.tile)).ToArray());
            var styled=cells.Where(c=>c.style>0).Select(c=>{
                var style=doc.styles[c.style-1];
                var at=(Vector3Int)c.at;
                var tile=library.Tile(c.tile);
                return new TileChangeData(at,tile,style.color,tile is Tile ? style.matrix : map.GetTransformMatrix(at));
            }).ToArray();
            if(styled.Length>0) map.SetTiles(styled,true);
            if (library.tiles.Any(entry => entry.tile is WorkshopTerrainTile && map.ContainsTile(entry.tile)))
                map.RefreshAllTiles();
        }
    }

    public static void ApplyToGame(WorkshopRoom doc, RoomInstance room, WorkshopCatalog library,
        System.Func<GameObject,Transform,GameObject> createPiece=null)
    {
        ApplyTiles(doc,room.transform,library);
        foreach (var e in doc.exits)
        {
            var anchor = room.GetComponentsInChildren<RoomAnchor>().FirstOrDefault(a => a.direction == e.direction);
            if (anchor == null)
            {
                var go = new GameObject("Workshop door");
                go.transform.SetParent(room.transform,false);
                anchor = go.AddComponent<RoomAnchor>();
                anchor.direction = e.direction;
            }
            anchor.transform.position = room.transform.TransformPoint(e.local);
        }
        Transform enemies = library.FindRoom(doc.template).tutorialOrder>0 ? room.transform.Find("Tutorial Enemies") : null;
        foreach (var p in doc.pieces)
        {
            if (!string.IsNullOrEmpty(p.source))
            {
                var target = WorkshopCatalog.Resolve(room.transform,p.source);
                if (p.removed) target.gameObject.SetActive(false);
                else target.position = room.transform.TransformPoint(p.local);
                continue;
            }
            if (p.removed) continue;
            var item = library.FindPiece(p.catalog);
            Transform parent = room.transform;
            if (item.enemy)
            {
                if (enemies == null)
                {
                    enemies = new GameObject("Workshop encounter").transform;
                    enemies.SetParent(room.transform,false);
                    enemies.gameObject.SetActive(false);
                }
                parent = enemies;
            }
            var go = createPiece!=null ? createPiece(item.prefab,parent) : Object.Instantiate(item.prefab,parent);
            go.transform.position = room.transform.TransformPoint(p.local);
        }
        var combat = room.GetComponent<NormalRoomEvent>();
        if(doc.waves.Count>0 && combat!=null)
            combat.ConfigureWaves(doc.waves.Select(id=>library.FindCluster(id).asset).ToArray());
        if (enemies != null && combat != null) combat.SetWorkshopEnemies(enemies);
    }
}

// A rendering-only copy keeps traps, enemy AI, shops and persistent events dormant on the workbench.
internal sealed partial class WorkshopPreview : System.IDisposable
{
    public const int Width = 478, Height = 272;
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
    public void Hover(Tilemap map,Vector3 point,bool over,Color color)
    {
        if(!over || map==null)
        {
            if(brush!=null) brush.gameObject.SetActive(false);
            return;
        }
        if(brush==null) brush=AddMarker(stage.transform,Color.white,1);
        brush.gameObject.SetActive(true);
        var cell=map.WorldToCell(point);
        var p=map.GetCellCenterWorld(cell);
        brush.position=p;
        var extent=map.CellToWorld(cell+Vector3Int.one)-map.CellToWorld(cell);
        brush.localScale=new Vector3(Mathf.Abs(extent.x),Mathf.Abs(extent.y),1f);
        brush.GetComponent<SpriteRenderer>().color=color;
    }
    public void MarkStart(Vector3? local)
    {
        if(startMarker!=null) startMarker.gameObject.SetActive(local.HasValue);
        if(!local.HasValue) return;
        if(startMarker==null) startMarker=AddMarker(art,new Color32(252,203,103,255),.55f);
        startMarker.position=World(local.Value);
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
        Overview();
        Home();
    }

    public void Home()
    {
        Zoom=1;
        var bounds=Ground.localBounds;
        Center=Local(Ground.transform.TransformPoint(bounds.center));
        SetView();
        PositionOutlines();
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
            // Each sprite sheet owns a material binding. Changing visible tile chunks must not
            // replace the atlas sampled by a prop that happens to share its draw batch.
            var texture=sprite.sprite.texture;
            if (!spriteMaterials.TryGetValue(texture,out var material))
                spriteMaterials.Add(texture,material=new Material(unlit){mainTexture=texture});
            r.sharedMaterial=material;
        }
        if (root!=null && WorkshopGeometry.IsEditablePiece(root,src))
            pieces[WorkshopCatalog.PathOf(root,src)]=dst;
        for (int i=0;i<src.childCount;i++) Copy(src.GetChild(i),dst,root,false);
        go.SetActive(!mapIcon && (first || src.gameObject.activeSelf));
        return dst;
    }
    private void Setup(Renderer copy,Renderer original)
    {
        copy.sharedMaterial=unlit; copy.sortingLayerID=original.sortingLayerID;
        copy.sortingOrder=original.sortingOrder; copy.enabled=original.enabled;
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
    private Transform area;
    private readonly List<Transform> edges = new();
    private readonly List<string> highlighted = new();
    private static bool Visible(SpriteRenderer sprite) => sprite.enabled && sprite.gameObject.activeInHierarchy && sprite.color.a > 0;

    public Bounds PieceBounds(string key)
    {
        var bounds = new Bounds(pieces[key].position, Vector3.one * .4f);
        bool found = false;
        foreach (var sprite in artwork[key])
        {
            if (!Visible(sprite)) continue;
            if (found) bounds.Encapsulate(sprite.bounds);
            else { bounds = sprite.bounds; found = true; }
        }
        return bounds;
    }

    public string Pick(Vector3 world, string after = null)
    {
        var hits = new List<(string key, (int layer, int order, float depth, int childOrder) rank)>();
        foreach (var piece in pieces)
        {
            if (!piece.Value.gameObject.activeInHierarchy) continue;
            bool drawn = false, hit = false;
            (int, int, float, int) rank = default;
            foreach (var sprite in artwork[piece.Key])
            {
                if (!Visible(sprite)) continue;
                drawn = true;
                if (!ContainsXY(sprite.bounds, world)) continue;
                var group = WorldDepthOrder.FindEffectiveGroup(sprite);
                WorldDepthOrder.ReadOrder(sprite, group, out int layer, out int order);
                var candidate = (SortingLayer.GetLayerValueFromID(layer), order,
                    -(group != null ? group.transform.position.y : sprite.bounds.center.y), sprite.sortingOrder);
                if (!hit || candidate.CompareTo(rank)>0) rank=candidate;
                hit=true;
            }
            if (hit) hits.Add((piece.Key, rank));
            else if (!drawn && ContainsXY(PieceBounds(piece.Key), world))
                hits.Add((piece.Key,(int.MinValue, 0, -piece.Value.position.y, 0)));
        }
        if (hits.Count==0) return null;
        hits.Sort((a,b) => a.rank!=b.rank ? b.rank.CompareTo(a.rank) : string.CompareOrdinal(b.key,a.key));
        int current=after!=null ? hits.FindIndex(item=>item.key==after) : -1;
        return hits[(current+1)%hits.Count].key;
    }

    private static bool ContainsXY(Bounds bounds, Vector3 point) =>
        point.x >= bounds.min.x && point.x <= bounds.max.x && point.y >= bounds.min.y && point.y <= bounds.max.y;

    public void HighlightSelection(IEnumerable<string> keys)
    {
        highlighted.Clear();
        highlighted.AddRange(keys);
        PositionOutlines();
    }

    public void PositionOutlines()
    {
        int edge = 0;
        float pixel = 1f / Density;
        foreach (string key in highlighted)
        {
            var b = PieceBounds(key);
            var size = b.size + Vector3.one * (2 * pixel);
            var center = b.center;
            Line(center + Vector3.up * size.y / 2, size.x + pixel, pixel);
            Line(center - Vector3.up * size.y / 2, size.x + pixel, pixel);
            Line(center + Vector3.right * size.x / 2, pixel, size.y);
            Line(center - Vector3.right * size.x / 2, pixel, size.y);
        }
        for (int i = edge; i < edges.Count; i++) edges[i].gameObject.SetActive(false);

        void Line(Vector3 center, float width, float height)
        {
            if (edge == edges.Count)
            {
                var outline=AddMarker(stage.transform, WorkshopUI.Mint, 1);
                outline.name="Selection outline";
                edges.Add(outline);
            }
            var line = edges[edge++];
            line.gameObject.SetActive(true);
            line.position = center;
            line.localScale = new Vector3(width, height, 1);
        }
    }
    public void Area(Vector3 from,Vector3 to)
    {
        if(area==null) area=AddMarker(stage.transform,new Color(.57f,.92f,.79f,.28f),1);
        area.gameObject.SetActive(true);
        area.position=(from+to)*.5f;
        area.localScale=new Vector3(Mathf.Max(.1f,Mathf.Abs(to.x-from.x)),Mathf.Max(.1f,Mathf.Abs(to.y-from.y)),1);
    }
    public void HideArea() { if(area!=null) area.gameObject.SetActive(false); }
    public void MarkDoor(Vector2Int direction,Vector3 local)
    {
        if (!doors.TryGetValue(direction,out var node))
            doors[direction]=node=AddMarker(art,new Color(.25f,.95f,.75f),.55f);
        node.position=World(local);
    }
    // Several room grids have a half-scale parent. Their 16-texel cells occupy half a world unit.
    private float nativeDensity, overviewDensity;
    public float DragThreshold => 2f / Density;
    private float Density => Zoom == 0 ? overviewDensity : nativeDensity * Zoom;
    public void Overview()
    {
        var bounds=Ground.GetComponent<TilemapRenderer>().bounds;
        foreach(var map in maps)
            if(map!=null && map.TryGetComponent<TilemapRenderer>(out var renderer) && renderer.enabled &&
                renderer.bounds.size.sqrMagnitude>0) bounds.Encapsulate(renderer.bounds);
        foreach (var piece in pieces)
            if (piece.Value.gameObject.activeInHierarchy) bounds.Encapsulate(PieceBounds(piece.Key));
        Center=Local(bounds.center);
        float fit=Mathf.Min((Frame.width-16)/Mathf.Max(.01f,bounds.size.x),(Frame.height-16)/Mathf.Max(.01f,bounds.size.y));
        overviewDensity=nativeDensity;
        while (overviewDensity>fit) overviewDensity*=.5f;
        Zoom=0;
        SetView();
        PositionOutlines();
    }
    public void ZoomAt(Vector2 uv, int zoom)
    {
        var anchor = Local(Point(uv));
        Zoom = Mathf.Clamp(zoom, 1, 8);
        SetView();
        Center += (Vector2)(anchor - Local(Point(uv)));
        SetView();
        PositionOutlines();
    }
    public void Pan(Vector2 previousUV, Vector2 currentUV)
    {
        Center += (Vector2)(Local(Point(previousUV)) - Local(Point(currentUV)));
        SetView();
    }
    public void ResizeFrame(int width,int height)
    {
        if (Frame.width==width && Frame.height==height) return;
        camera.targetTexture=null;
        Frame.Release(); Frame.width=width; Frame.height=height; Frame.Create();
        camera.targetTexture=Frame;
        if (Zoom==0) Overview(); else SetView();
    }

    public void MatchView(WorkshopPreview other)
    {
        Center=other.Center; Zoom=other.Zoom;
        overviewDensity=other.overviewDensity;
        SetView();
    }

    public void SetView()
    {
        float density = Density;
        camera.aspect = Frame.width / (float)Frame.height;
        camera.orthographicSize = Frame.height / (2f * density);
        Vector3 p = World(Center);
        Vector3 gridOrigin=Ground.CellToWorld(Vector3Int.zero);
        camera.transform.position = new Vector3(gridOrigin.x + Mathf.Round((p.x-gridOrigin.x)*density)/density,
            gridOrigin.y + Mathf.Round((p.y-gridOrigin.y)*density)/density, -30);
    }
    public Vector3 Point(Vector2 uv) => camera.ViewportToWorldPoint(new Vector3(uv.x,uv.y,30));
    private bool disposed;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
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
        Object.Destroy(marker);
        foreach (var material in spriteMaterials.Values) Object.Destroy(material);
        Object.Destroy(unlit);
    }
}

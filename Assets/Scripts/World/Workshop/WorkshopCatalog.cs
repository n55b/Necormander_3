using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class WorkshopCatalog : ScriptableObject
{
    [Serializable] public sealed class Template
    {
        public string id, revision, title;
        public RoomInstance room;
        public RoomType type;
        public int tutorialOrder;
        public bool inPool;
    }
    [Serializable] public sealed class TileEntry
    {
        public string id;
        public TileBase tile;
        public Sprite Sample => tile switch
        {
            WorkshopTerrainTile terrain => terrain.PaletteSprite,
            Tile plain => plain.sprite,
            AnimatedTile animated when animated.m_AnimatedSprites is { Length: >0 } => animated.m_AnimatedSprites[0],
            _ => null
        };
        public string Title => tile is WorkshopTerrainTile terrain ? terrain.Title : tile.name;
    }
    [Serializable] public sealed class Piece { public string id; public GameObject prefab; public bool enemy; }

    [Serializable] public sealed class Cluster { public string id; public EnemyClusterSO asset; }
    public Cluster[] clusters = Array.Empty<Cluster>();
    public Cluster FindCluster(string id) => Array.Find(clusters, entry => entry.id == id);

    [Serializable] public sealed class EditableFont { public TMPro.TMP_FontAsset bitmap; public Font source; }
    public EditableFont[] editableFonts = Array.Empty<EditableFont>();
    private readonly Dictionary<TMPro.TMP_FontAsset,TMPro.TMP_FontAsset> liveFonts = new();

    // Only free-form author text needs glyphs outside the shipped static atlas.
    internal TMPro.TMP_FontAsset FontForInput(TMPro.TMP_FontAsset bitmap)
    {
        if (liveFonts.TryGetValue(bitmap,out var face) && face!=null) return face;
        var entry=Array.Find(editableFonts,item=>item.bitmap==bitmap);
        if (entry?.source==null) return bitmap;
        face=TMPro.TMP_FontAsset.CreateFontAsset(entry.source,Mathf.RoundToInt(bitmap.faceInfo.pointSize),1,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.RASTER_HINTED,2048,2048,TMPro.AtlasPopulationMode.Dynamic,false);
        face.name=bitmap.name+" (workshop)";
        face.atlasTexture.filterMode=FilterMode.Point;
        face.atlasTexture.wrapMode=TextureWrapMode.Clamp;
        liveFonts[bitmap]=face;
        return face;
    }

    internal void ReleaseInputFonts()
    {
        foreach (var face in liveFonts.Values) if (face!=null) Destroy(face);
        liveFonts.Clear();
    }

    public string[] villageActors = Array.Empty<string>();
    public Template[] rooms;
    public TileEntry[] tiles;
    public Piece[] pieces;
    public MapGenerationDataSO generation;
    public GameObject playerPrefab;
    public DialogueTableSO dialogueTable;
    public RoomPrefabDataSO roomPool;
    public EnemyMinionDataSO[] elites;
    public Template DefaultRoom => rooms.FirstOrDefault(r => r.inPool && r.title == "Room_Normal 3") ?? rooms[0];
    public Template FindRoom(string id) => Array.Find(rooms, r => r.id == id);
    private TileEntry[] indexedTiles;
    private Dictionary<string,TileBase> tilesById;
    private Dictionary<TileBase,string> idsByTile;
    private void IndexTiles()
    {
        if (indexedTiles==tiles && tilesById!=null) return;
        tilesById=tiles.ToDictionary(entry=>entry.id,entry=>entry.tile);
        idsByTile=tiles.ToDictionary(entry=>entry.tile,entry=>entry.id);
        indexedTiles=tiles;
    }
    public TileBase Tile(string id)
    {
        IndexTiles();
        return id!=null && tilesById.TryGetValue(id,out var tile) ? tile : null;
    }
    public string Id(TileBase tile)
    {
        IndexTiles();
        return tile!=null && idsByTile.TryGetValue(tile,out var id) ? id : null;
    }
    public Piece FindPiece(string id) => Array.Find(pieces, p => p.id == id);

    public static readonly string[] Layers = { "Ground", "Wall", "Shadow", "Unsteppable" };
    public static Tilemap Map(Transform room, int layer) =>
        Array.Find(room.GetComponentsInChildren<Tilemap>(true), map => map.name == Layers[layer]);

    // Sibling indices address an authored instance within a content-hashed template.
    public static string PathOf(Transform root, Transform node) =>
        node == root ? "" : PathOf(root,node.parent) + "/" + node.GetSiblingIndex();
    public static Transform Resolve(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var token in path.Substring(1).Split('/'))
        {
            if (!int.TryParse(token,out int index) || index < 0 || index >= root.childCount) return null;
            root = root.GetChild(index);
        }
        return root;
    }
}

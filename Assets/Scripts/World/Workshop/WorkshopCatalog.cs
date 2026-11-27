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

    // DATA의 그림 참조 후보. 키는 "<텍스처 GUID>:<조각 이름>"(주 에셋은 "<GUID>"). WorkshopBuild.Bake가 굽는다.
    // 플레이어 빌드에는 GUID가 없으므로 DATA의 그림 값은 이 표로만 풀린다.
    [Serializable] public sealed class AssetRef { public string key; public UnityEngine.Object asset; }
    public AssetRef[] assets = Array.Empty<AssetRef>();

    private static WorkshopCatalog loaded;
    private static AssetRef[] indexedAssets;
    private static Dictionary<string,UnityEngine.Object> assetsByKey;
    private static Dictionary<UnityEngine.Object,string> keysByAsset;
    internal static WorkshopCatalog Shared => loaded != null ? loaded : loaded = Resources.Load<WorkshopCatalog>("WorkshopCatalog");
    // 편집 모드 재굽기가 assets 배열을 바꾸면 색인을 다시 만든다.
    private static void IndexAssets()
    {
        var table = Shared != null ? Shared.assets ?? Array.Empty<AssetRef>() : Array.Empty<AssetRef>();
        if (indexedAssets == table && assetsByKey != null) return;
        assetsByKey = new Dictionary<string,UnityEngine.Object>(StringComparer.Ordinal);
        keysByAsset = new Dictionary<UnityEngine.Object,string>();
        foreach (var entry in table)
        {
            if (entry?.asset == null || string.IsNullOrEmpty(entry.key)) continue;
            assetsByKey.TryAdd(entry.key, entry.asset);
            keysByAsset.TryAdd(entry.asset, entry.key);
        }
        indexedAssets = table;
    }
    // null → "", 표에 없으면 null.
    internal static string AssetKey(UnityEngine.Object asset)
    {
        if (asset == null) return "";
        IndexAssets();
        return keysByAsset.TryGetValue(asset, out var key) ? key : null;
    }
    internal static UnityEngine.Object FindAsset(string key, Type type)
    {
        IndexAssets();
        if (key != null && assetsByKey.TryGetValue(key, out var asset) && type.IsInstanceOfType(asset)) return asset;
        throw new System.IO.InvalidDataException("그림을 찾을 수 없습니다. 원본 카탈로그를 다시 구우세요: " + key);
    }
#if UNITY_EDITOR
    // WorkshopBuild.Bake가 부른다(ProjectParameters.Fields는 런타임 어셈블리 internal이다).
    // SOData·Resources의 Sprite 필드가 지금 가리키는 시트의 모든 조각. 키 = 텍스처 GUID + ":" + 조각 이름.
    public static AssetRef[] BakeAssets()
    {
        var sheets = ProjectParameterBindings.All()
            .SelectMany(e => ProjectParameters.Fields(e.source).Where(f => f.type == typeof(Sprite)).Select(f => f.read() as Sprite))
            .Where(s => s != null && s.texture != null).Select(s => UnityEditor.AssetDatabase.GetAssetPath(s.texture))
            .Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p, StringComparer.Ordinal);
        return sheets.SelectMany(path => UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .Select(s => new AssetRef { key = UnityEditor.AssetDatabase.AssetPathToGUID(path) + ":" + s.name, asset = s }))
            .GroupBy(a => a.key).Select(g => g.First()).OrderBy(a => a.key, StringComparer.Ordinal).ToArray();
    }
#endif

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
    // 시험장 허수아비. 전투 없는 방에 바로 서 있어야 하므로 소품(enemy=false)으로 굽는다.
    public const string DummyName = "Enemy Scarecrow";
    public Piece Dummy => Array.Find(pieces, p => p.prefab != null && p.prefab.name == DummyName);

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

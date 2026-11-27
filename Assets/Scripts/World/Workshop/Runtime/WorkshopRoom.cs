using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

[Serializable]
public sealed class WorkshopRoom
{
    [Serializable] public sealed class Cell { public int layer; public Vector2Int at; public string tile; public int style; }
    [Serializable] public sealed class Piece
    {
        public string id, catalog, source;
        public Vector3 local;
        public bool removed;
    }
    [Serializable] public sealed class Exit { public Vector2Int direction; public Vector3 local; }
    [Serializable] public sealed class TileStyle { public Matrix4x4 matrix; public Color color; }
    public List<TileStyle> styles = new();
    public List<string> waves = new();
    // Zero inherits the floor distribution. Other values encode NormalRewardType + 1.
    public int reward;
    // An empty room starts from a cleared copy of its base: every authored tile is dropped before cells apply.
    public bool clearTiles;
    // Keeps one exit per direction; bases such as the boss room carry several alternatives per side.
    public bool singleDoors;
    public const int MaxWaves = 8;
    public int version = 2, seed = 907;
    public string id, title, template, baseRevision;
    public List<Cell> cells = new();
    [NonSerialized] private Dictionary<(int, Vector2Int), Cell> cellIndex;
    public List<Piece> pieces = new();
    public List<Exit> exits = new();

    public static WorkshopRoom Create(WorkshopCatalog.Template source) => new()
    {
        id = Guid.NewGuid().ToString("N"), title = source.title + " · 복사본",
        template = source.id, baseRevision = source.revision
    };

    // An unedited document for an existing room prefab: same id and name, no changes yet.
    public static WorkshopRoom FromTemplate(WorkshopCatalog.Template source)
    {
        var room = Create(source);
        room.id = source.id; room.title = source.title;
        return room;
    }

    public void Validate(WorkshopCatalog library)
    {
        if (version == 1)
        {
            styles = new List<TileStyle>();
            waves = new List<string>();
            reward = 0;
            version = 2;
        }
        if (version != 2 || !Guid.TryParseExact(id,"N",out _) || string.IsNullOrWhiteSpace(title))
            throw new InvalidDataException("방 파일의 형식 또는 이름을 확인하세요.");
        var asset = library.FindRoom(template);
        if (asset == null || asset.revision != baseRevision)
            throw new InvalidDataException("원본 방의 버전이 다릅니다. 같은 제작 빌드에서 열어 주세요.");
        if (cells == null || pieces == null || exits == null || cells.Count > 131072 || pieces.Count > 2048 || exits.Count > 4)
            throw new InvalidDataException("방의 편집 범위를 초과했습니다.");
        if (styles == null || styles.Count > 131072 || styles.Any(style => style==null || !ValidStyle(style.matrix,style.color)) ||
            waves == null || waves.Count > MaxWaves || waves.Any(id=>library.FindCluster(id)==null) ||
            (waves.Count>0 && asset.room.GetComponent<NormalRoomEvent>()==null) ||
            reward<0 || reward>3 || (reward>0 && asset.type!=RoomType.Normal))
            throw new InvalidDataException("방의 타일 모양, 전투 또는 보상 설정을 확인하세요.");
        var keys = new HashSet<(int,Vector2Int)>();
        foreach (var c in cells)
            if (c == null || c.layer < 0 || c.layer >= WorkshopCatalog.Layers.Length ||
                c.style<0 || c.style>styles.Count || !keys.Add((c.layer,c.at)) || Math.Abs((long)c.at.x)>1024 || Math.Abs((long)c.at.y)>1024 ||
                (!string.IsNullOrEmpty(c.tile) && library.Tile(c.tile) == null))
                throw new InvalidDataException("읽을 수 없는 타일이 있습니다.");
        var ids = new HashSet<string>();
        foreach (var p in pieces)
            if (p == null || string.IsNullOrEmpty(p.id) || !ids.Add(p.id) || !ValidPoint(p.local) ||
                (string.IsNullOrEmpty(p.source) ? library.FindPiece(p.catalog) == null :
                 !WorkshopGeometry.IsEditablePiece(asset.room.transform,WorkshopCatalog.Resolve(asset.room.transform,p.source))))
                throw new InvalidDataException("오브젝트 참조를 확인하세요.");
        var directions = new HashSet<Vector2Int>();
        foreach (var e in exits)
            if (e == null || !ValidPoint(e.local) || !RoomConnectivity.IsCardinal(e.direction) || !directions.Add(e.direction))
                throw new InvalidDataException("출구의 위치와 방향을 확인하세요.");
    }
    private static bool ValidPoint(Vector3 p) => Mathf.Abs(p.x)<=512 && Mathf.Abs(p.y)<=512 && Mathf.Abs(p.z)<=10;

    internal static bool ValidStyle(Matrix4x4 matrix, Color color)
    {
        for(int i=0;i<16;i++)
            if(!float.IsFinite(matrix[i])) return false;
        for(int i=0;i<4;i++)
            if(!float.IsFinite(color[i])) return false;
        return true;
    }
    public int StyleFor(Matrix4x4 matrix, Color color)
    {
        int index=styles.FindIndex(s=>s.matrix.Equals(matrix) && s.color.Equals(color));
        if(index>=0) return index+1;
        styles.Add(new TileStyle {matrix=matrix,color=color});
        return styles.Count;
    }
    public void Paint(int layer, Vector2Int cell, string tile, int style=0)
    {
        if (cellIndex==null || cellIndex.Count!=cells.Count)
            cellIndex=cells.ToDictionary(c=>(c.layer,c.at));
        if (!cellIndex.TryGetValue((layer,cell),out var entry))
        {
            entry=new Cell { layer=layer, at=cell };
            cells.Add(entry);
            cellIndex.Add((layer,cell),entry);
        }
        entry.tile=tile;
        entry.style=style;
    }
    public void SetDoor(Vector2Int direction, Vector3 local)
    {
        var entry = exits.Find(e => e.direction == direction);
        if (entry == null) exits.Add(new Exit { direction=direction, local=local });
        else entry.local = local;
    }
    public string Serialize()
    {
        cells.Sort((a,b) => a.layer != b.layer ? a.layer.CompareTo(b.layer) :
            a.at.y != b.at.y ? a.at.y.CompareTo(b.at.y) : a.at.x.CompareTo(b.at.x));
        return JsonUtility.ToJson(this,true);
    }
}

public static class WorkshopFiles
{
    public static void WriteAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using (var writer = new StreamWriter(stream))
            { writer.Write(contents); writer.Flush(); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
internal sealed class WorkshopStamp
{
    [Serializable] internal sealed class Sample
    {
        public Vector2Int offset;
        public string tile;
        public Matrix4x4 matrix;
        public Color color;
    }

    public int version=1, layer;
    public string title;
    public List<Sample> cells=new();
    public static string Folder => Path.Combine(WorkshopWorkspace.Home,"Stamps");

    public void Save(WorkshopCatalog catalog)
    {
        Validate(catalog);
        var path=Path.Combine(Folder,Guid.NewGuid().ToString("N")+".json");
        WorkshopFiles.WriteAtomic(path,JsonUtility.ToJson(this,true));
    }

    public static WorkshopStamp Read(string path, WorkshopCatalog catalog)
    {
        if(new FileInfo(path).Length>4*1024*1024) throw new InvalidDataException("도장 파일이 너무 큽니다.");
        var stamp=JsonUtility.FromJson<WorkshopStamp>(File.ReadAllText(path));
        if(stamp==null) throw new InvalidDataException("도장 파일을 열 수 없습니다.");
        stamp.Validate(catalog);
        return stamp;
    }

    private void Validate(WorkshopCatalog catalog)
    {
        if(version!=1 || layer<0 || layer>=WorkshopCatalog.Layers.Length ||
            string.IsNullOrWhiteSpace(title) || title.Length>64 || cells==null || cells.Count==0 || cells.Count>1024)
            throw new InvalidDataException("도장 형식이나 크기를 확인하세요.");
        var positions=new HashSet<Vector2Int>();
        foreach(var c in cells)
        {
            if(c==null || c.offset.x<0 || c.offset.x>=32 || c.offset.y<0 || c.offset.y>=32 ||
                !positions.Add(c.offset) || catalog.Tile(c.tile)==null || !WorkshopRoom.ValidStyle(c.matrix,c.color))
                throw new InvalidDataException("도장에 사용할 수 없는 타일이 있습니다.");
        }
    }
}
#endif

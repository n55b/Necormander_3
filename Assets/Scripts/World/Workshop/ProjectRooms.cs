using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class ProjectRooms
{
    public const string DirectoryName = "WORLD/ROOMS";
    public const string CatalogPath = "Assets/Resources/WorkshopCatalog.asset";
    private const string Header = "room.json";
    private sealed class Choices
    {
        internal WorkshopRoom original;
        internal readonly List<WorkshopRoom> variants=new();
    }
    private static Dictionary<GameObject,Choices> byPrefab;
    private static WorkshopCatalog runtimeCatalog;
    private static string Document(string id) => Guid.TryParseExact(id,"N",out _) ? DirectoryName+"/"+id :
        throw new InvalidDataException("잘못된 방 ID: "+id);
    public static string[] Ids()
    {
        string root = Path.Combine(ProjectContentStore.Root,DirectoryName);
        return Directory.Exists(root) ? Directory.EnumerateDirectories(root).Where(HasFiles).Select(Path.GetFileName)
            .Where(id=>Guid.TryParseExact(id,"N",out _)).OrderBy(id=>id,StringComparer.Ordinal).ToArray() : Array.Empty<string>();
    }
    public static bool HasFiles(string folder)=>File.Exists(Path.Combine(folder,Header)) ||
        Directory.EnumerateFiles(folder,"*.json",SearchOption.AllDirectories).Any();
    public static Dictionary<string,string> Snapshot(string id) =>
        ProjectContentStore.WithDocument(Document(id),(folder,_)=>ProjectContentStore.Read(folder));
    internal static WorkshopRoom Description(string id)=>ProjectContentStore.WithDocument(Document(id),(folder,_)=>{
        var room=JsonUtility.FromJson<WorkshopRoom>(File.ReadAllText(Path.Combine(folder,Header)));
        if(room==null || room.id!=id)throw new InvalidDataException("방 설명과 ID가 다릅니다. "+id);
        return room;
    });
    public static void ValidateForBuild(string id, WorkshopCatalog catalog) => Decode(id,Snapshot(id),catalog);

    public static WorkshopRoom Template(WorkshopCatalog.Template template)
    {
        var room = WorkshopRoom.Create(template);
        room.id = template.id; room.title = template.title;
        return room;
    }

    private static Dictionary<string,string> Split(WorkshopRoom room)
    {
        var json = JObject.Parse(room.Serialize());
        var files = new Dictionary<string,string>(StringComparer.Ordinal);
        void Put(string path, JToken data) => files.Add(path,ProjectContentStore.Json(data));
        var meta = new JObject();
        foreach (string key in new[]{"version","id","title","template","baseRevision","seed"}) meta[key]=json[key];
        Put(Header,meta);
        for(int i=0;i<room.waves.Count;i++)
            Put("ENCOUNTER/WAVES/"+(i+1).ToString("D2")+".json",new JObject{["cluster"]=room.waves[i]});
        foreach (JObject exit in json["exits"])
            Put("EXITS/"+exit["direction"]["x"]+"_"+exit["direction"]["y"]+".json",exit);
        foreach (JObject piece in json["pieces"])
        {
            // Existing art uses hierarchy paths for IDs. Hex keeps those IDs safe and reversible on disk.
            string key = BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes((string)piece["id"])).Replace("-","");
            if(key.Length>180) throw new InvalidDataException("배치물 식별자가 너무 깁니다.");
            Put("OBJECTS/"+key+".json",piece);
        }
        var chunks = new SortedDictionary<string,JObject>(StringComparer.Ordinal);
        foreach (JObject cell in json["cells"])
        {
            int x=(int)cell["at"]["x"], y=(int)cell["at"]["y"], layer=(int)cell["layer"], style=(int)cell["style"];
            string path="TILES/"+WorkshopCatalog.Layers[layer]+"/"+(x>>4)+"_"+(y>>4)+".json";
            if(!chunks.TryGetValue(path,out var chunk)) chunks.Add(path,chunk=new JObject());
            var entry = new JObject {["tile"]=cell["tile"]};
            if(style>0) entry["appearance"]=json["styles"][style-1];
            chunk.Add(x+","+y,entry);
        }
        foreach(var chunk in chunks) Put(chunk.Key,chunk.Value);
        return files;
    }

    public static WorkshopRoom Decode(string id, Dictionary<string,string> files, WorkshopCatalog catalog)
    {
        if (!files.TryGetValue(Header,out string header)) throw new InvalidDataException("방 설명 파일을 찾을 수 없습니다. "+id);
        var doc=JsonUtility.FromJson<WorkshopRoom>(header);
        if (doc == null || doc.id != id) throw new InvalidDataException("방 경로와 ID가 다릅니다. "+id);
        doc.styles=new(); doc.cells=new(); doc.pieces=new(); doc.exits=new(); doc.waves=new();
        foreach (var pair in files.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            string path=pair.Key;
            if(path==Header) continue;
            // 옛 방 클리어 보상 선택. 지급에 영향이 없어 지웠고, 남은 파일은 다음 저장 때 사라진다.
            if(path=="ENCOUNTER/reward.json") continue;
            if(path=="ENCOUNTER/waves.json") doc.waves=JArray.Parse(pair.Value).Values<string>().ToList();
            else if(path.StartsWith("ENCOUNTER/WAVES/",StringComparison.Ordinal))
            {
                if(files.ContainsKey("ENCOUNTER/waves.json") || path!="ENCOUNTER/WAVES/"+(doc.waves.Count+1).ToString("D2")+".json")
                    throw new InvalidDataException("전투 웨이브의 순서를 확인하세요. "+path);
                doc.waves.Add((string)JObject.Parse(pair.Value)["cluster"]);
            }
            else if(path.StartsWith("EXITS/",StringComparison.Ordinal))
            {
                var exit=JsonUtility.FromJson<WorkshopRoom.Exit>(pair.Value);
                if(exit==null || path!="EXITS/"+exit.direction.x+"_"+exit.direction.y+".json")
                    throw new InvalidDataException("출구의 파일명과 방향이 다릅니다. "+path);
                doc.exits.Add(exit);
            }
            else if(path.StartsWith("OBJECTS/",StringComparison.Ordinal))
            {
                var piece=JsonUtility.FromJson<WorkshopRoom.Piece>(pair.Value);
                if(string.IsNullOrEmpty(piece?.id) || path!="OBJECTS/"+BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(piece.id)).Replace("-","")+".json")
                    throw new InvalidDataException("배치물의 파일명과 ID가 다릅니다. "+path);
                doc.pieces.Add(piece);
            }
            else if(path.StartsWith("TILES/",StringComparison.Ordinal))
            {
                var parts=path.Split('/');
                int layer=parts.Length==3 ? Array.IndexOf(WorkshopCatalog.Layers,parts[1]) : -1;
                foreach(var cell in JObject.Parse(pair.Value).Properties())
                {
                    string[] xy=cell.Name.Split(',');
                    if (xy.Length!=2 || !int.TryParse(xy[0],out int x) || !int.TryParse(xy[1],out int y) ||
                        layer<0 || parts[2]!=(x>>4)+"_"+(y>>4)+".json") throw new InvalidDataException("타일 좌표와 파일이 다릅니다. "+path);
                    var appearance = cell.Value["appearance"];
                    int style=0;
                    if (appearance!=null)
                    {
                        var s=JsonUtility.FromJson<WorkshopRoom.TileStyle>(appearance.ToString());
                        style=doc.StyleFor(s.matrix,s.color);
                    }
                    doc.cells.Add(new WorkshopRoom.Cell {layer=layer,at=new Vector2Int(x,y),tile=(string)cell.Value["tile"],style=style});
                }
            }
            else throw new InvalidDataException("지원하지 않는 방 데이터: "+path);
        }
        doc.Validate(catalog);
        return doc;
    }

    internal static Dictionary<string,string> Save(WorkshopRoom room, Dictionary<string,string> basis,
        WorkshopCatalog catalog, out WorkshopRoom combined, Dictionary<string,ProjectContentStore.Conflict> choices=null)
    {
        room.Validate(catalog);
        var files=ProjectContentStore.Save(Document(room.id),basis,Split(room),merged=>Decode(room.id,merged,catalog),choices);
        combined=Decode(room.id,files,catalog);
        Reset();
        return files;
    }
    // 에디터 검사(ProjectRoomBakeCheck)가 방 문서를 쓰는 입구. 충돌 선택이 필요한 저장은 위 Save를 쓴다.
    public static void Publish(WorkshopRoom room, WorkshopCatalog catalog) => Save(room,Snapshot(room.id),catalog,out _);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset() { byPrefab=null; runtimeCatalog=null; }

    internal static void Load()
    {
        if (byPrefab!=null) return;
        var next=new Dictionary<GameObject,Choices>();
        foreach(string id in Ids())
            ProjectContentStore.TryReadPublished(Document(id),()=>LoadRoom(id,next));
        byPrefab=next;
    }
    private static void LoadRoom(string id,Dictionary<GameObject,Choices> next)
    {
        var files=Snapshot(id);
        if(files.Count==1 && files.TryGetValue(Header,out var header) && (string)JObject.Parse(header)["template"]==id)
            return;
        if(runtimeCatalog==null) runtimeCatalog=Resources.Load<WorkshopCatalog>("WorkshopCatalog");
        var room=Decode(id,files,runtimeCatalog);
        var prefab=runtimeCatalog.FindRoom(room.template).room.gameObject;
        if(!next.TryGetValue(prefab,out var choices)) next.Add(prefab,choices=new());
        if(room.id==room.template) choices.original=room;
        else if(IsRegistered(room)) choices.variants.Add(room);
    }

    internal static void Apply(GameObject source, RoomInstance room, int seed, Vector2Int cell)
    {
        Load();
        if (!byPrefab.TryGetValue(source,out var choices)) return;
        var floor=GameManager.Instance!=null ? GameManager.Instance.projectFloor : null;
        if(floor!=null && floor.slots.Count>0)
        {
            var selectedSlot=floor.slots.Find(s=>s.scenario==floor.scenario && s.floor==floor.FloorNumber && s.seed==seed && s.cell==cell);
            if(selectedSlot!=null)
            {
                var selectedRoom=Decode(selectedSlot.document,Snapshot(selectedSlot.document),runtimeCatalog);
                if(runtimeCatalog.FindRoom(selectedRoom.template).room.gameObject==source)
                {
                    WorkshopGeometry.ApplyToGame(selectedRoom,room,runtimeCatalog);
                    return;
                }
            }
        }
        // An edit to a shipping template always wins over its original. New copies share its candidate slot.
        // Keep selection deterministic without advancing the combat or map generation RNG.
        int slot=(int)(unchecked((uint)(seed ^ cell.x*73856093 ^ cell.y*19349663))%(uint)(1+choices.variants.Count));
        var selected=slot==0 ? choices.original : choices.variants[slot-1];
        if(selected!=null) WorkshopGeometry.ApplyToGame(selected,room,runtimeCatalog);
    }
    internal static Vector2Int[] AvailableDoors(GameObject prefab,Vector2Int[] inherited)
    {
        Load();
        if(!byPrefab.TryGetValue(prefab,out var choices) || choices.original==null) return inherited;
        // Prefab anchors remain in every variant. An extra direction is usable for generation
        // only if every variant that might occupy this slot has an anchor there.
        var extra=choices.original.exits.Select(e=>e.direction).Where(d=>
            choices.variants.All(v=>v.exits.Any(e=>e.direction==d)));
        return inherited.Concat(extra).Distinct().ToArray();
    }
    public static bool IsRegistered(WorkshopRoom doc) =>
        File.Exists(Path.Combine(ProjectContentStore.Root,"WORLD","POOLS",doc.template,doc.id+".json"));
    public static void Register(WorkshopRoom doc)
    {
        var basis = ProjectContentStore.WithDocument("WORLD/POOLS/"+doc.template,(folder,_)=>ProjectContentStore.Read(folder));
        var updated=new Dictionary<string,string>(basis) { [doc.id+".json"]=ProjectContentStore.Json(new JObject{["room"]=doc.id}) };
        ProjectContentStore.Save("WORLD/POOLS/"+doc.template,basis,updated,_=>{});
        Reset();
    }
}

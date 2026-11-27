using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[Serializable]
internal sealed class WorkshopPackage
{
    internal const string Extension=".levelpack";
    private const int MaxBytes=64*1024*1024;
    private const string Format="Necromander.LevelWorkshop";
    public string format, title;
    public int version;
    public WorkshopFloor floor;
    public WorkshopVillage village;
    public List<WorkshopRoom> rooms;
    [Serializable] internal sealed class Revision { public string key, hash; }
    public List<Revision> parents;
    public List<Revision> templates;

    internal static string Hash(string value)
    {
        using var sha=SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");
    }

    internal static string Export(WorkshopCatalog catalog,string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length>64) throw new InvalidDataException("묶음 이름을 입력하세요.");
        var floor=WorkshopFloor.Load();
        var village=WorkshopVillage.Load();
        var package=new WorkshopPackage {
            format=Format,version=1,title=title.Trim(),
            floor=floor,village=village,rooms=new List<WorkshopRoom>(),
            parents=WorkshopWorkspace.Ancestors()
        };
        long total=0;
        var projectIds=ProjectContentStore.Authoring ? ProjectRooms.Ids() : Array.Empty<string>();
        var published=new HashSet<string>(projectIds,StringComparer.Ordinal);
        if (Directory.Exists(WorkshopFiles.Folder))
            foreach (var path in Directory.EnumerateFiles(WorkshopFiles.Folder,"*.json").OrderBy(p=>p,StringComparer.Ordinal))
            {
                if(published.Contains(Path.GetFileNameWithoutExtension(path)))continue;
                total+=new FileInfo(path).Length;
                if (total>MaxBytes || package.rooms.Count>=4096) throw new InvalidDataException("제작 묶음의 크기를 초과했습니다.");
                var room=WorkshopFiles.Read(path,catalog);
                if (!string.Equals(Path.GetFileNameWithoutExtension(path),room.id,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("파일명과 방 ID가 다릅니다. "+room.title);
                package.rooms.Add(room);
            }
        if(ProjectContentStore.Authoring)
            foreach(string id in projectIds.Where(id=>!ProjectRooms.IsRemoved(id)))   // 삭제한 방은 동료에게 보내지 않는다
                package.rooms.Add(ProjectRooms.Decode(id,ProjectRooms.Snapshot(id),catalog));
        package.templates=package.TemplateIds(catalog).Select(id=>new Revision {
            key=id,hash=catalog.FindRoom(id)?.revision
        }).ToList();
        package.Validate(catalog);
        string json=JsonUtility.ToJson(package);
        if (Encoding.UTF8.GetByteCount(json)>MaxBytes) throw new InvalidDataException("제작 묶음의 크기를 초과했습니다.");
        string label=new string(package.title.Take(40).Select(c=>Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        string output=Path.Combine(WorkshopWorkspace.Shared,"Workshop-"+label+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+Extension);
        WorkshopFiles.WriteAtomic(output,json);
        return output;
    }

    private IEnumerable<string> TemplateIds(WorkshopCatalog catalog) =>
        floor.TutorialOrder(catalog).Concat(rooms.Select(r=>r.template)).Distinct().OrderBy(id=>id,StringComparer.Ordinal);

    internal static WorkshopPackage Read(string path,WorkshopCatalog catalog)
    {
        if (new FileInfo(path).Length>MaxBytes) throw new InvalidDataException("제작 묶음의 크기를 초과했습니다.");
        var package=JsonUtility.FromJson<WorkshopPackage>(File.ReadAllText(path));
        if (package==null) throw new InvalidDataException("제작 묶음을 읽을 수 없습니다.");
        package.Validate(catalog);
        return package;
    }

    internal void Validate(WorkshopCatalog catalog)
    {
        if (format!=Format || version!=1 || string.IsNullOrWhiteSpace(title) || title.Length>128 ||
            floor==null || village==null || rooms==null || rooms.Count>4096 || parents==null || parents.Count>4098 ||
            templates==null || templates.Count>catalog.rooms.Length)
            throw new InvalidDataException("지원하는 제작 묶음 형식과 버전을 확인하세요.");
        if (parents.Any(r=>r==null || string.IsNullOrEmpty(r.key) || r.hash==null || r.hash.Length!=64) ||
            parents.Select(r=>r.key).Distinct().Count()!=parents.Count)
            throw new InvalidDataException("이전 개정 정보가 올바르지 않습니다.");
        ValidateContents(catalog,floor,village,rooms);
        var expected=TemplateIds(catalog).ToArray();
        if (templates.Count!=expected.Length || templates.Any(r=>r==null || catalog.FindRoom(r.key)?.revision!=r.hash) ||
            !expected.SequenceEqual(templates.Select(r=>r.key).OrderBy(id=>id,StringComparer.Ordinal)))
            throw new InvalidDataException("방 원본의 개정이 다릅니다. 같은 제작 빌드를 사용하세요.");
    }

    internal static void ValidateContents(WorkshopCatalog catalog,WorkshopFloor floor,WorkshopVillage village,IEnumerable<WorkshopRoom> rooms)
    {
        floor.Validate(); village.Validate();
        var unknown=village.actors.FirstOrDefault(a=>!catalog.villageActors.Contains(a.id));
        if (unknown!=null) throw new InvalidDataException("이 제작 빌드에 없는 마을 NPC · "+unknown.id);
        var byId=new Dictionary<string,WorkshopRoom>(StringComparer.OrdinalIgnoreCase);
        foreach (var room in rooms)
        {
            if (room==null || !byId.TryAdd(room.id ?? "",room)) throw new InvalidDataException("중복되거나 비어 있는 방 ID입니다.");
            try { room.Validate(catalog); }
            catch (Exception ex) { throw new InvalidDataException(room.title+" · "+ex.Message,ex); }
        }
        foreach (var slot in floor.slots)
            if (!byId.TryGetValue(slot.document,out var room) || room.template!=slot.template)
                throw new InvalidDataException($"연결한 방을 찾을 수 없습니다. 층 {slot.floor} · 위치 {slot.cell} · {slot.document}");
        var defaultOrder=new WorkshopFloor().TutorialOrder(catalog);
        var route=floor.TutorialOrder(catalog);
        if (route.Count!=defaultOrder.Count || route[0]!=defaultOrder[0] || route[route.Count-1]!=defaultOrder[defaultOrder.Count-1] ||
            !route.OrderBy(id=>id).SequenceEqual(defaultOrder.OrderBy(id=>id)))
            throw new InvalidDataException("튜토리얼의 방과 시작·종료 순서를 확인하세요.");
        var active=floor.scenario;
        try { floor.scenario=WorkshopFloor.Scenario.Tutorial; floor.Preview(catalog,out _); }
        finally { floor.scenario=active; }
    }
}

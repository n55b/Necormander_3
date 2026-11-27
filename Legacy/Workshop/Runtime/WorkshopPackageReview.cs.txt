using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

internal sealed class WorkshopPackageReview
{
    internal sealed class Part
    {
        internal string key, name, summary, localLabel, teamLabel, teamHash;
        internal bool canChoose, useLocal;
        internal readonly List<(string label,string local,string team)> differences=new();
        internal void Difference(string label,string ours,string theirs)
        {
            if (ours!=theirs) differences.Add((label,ours,theirs));
        }
    }

    internal readonly List<Part> parts=new();
    internal readonly WorkshopPackage package;
    private readonly WorkshopCatalog catalog;
    private readonly WorkshopFloor localFloor;
    private readonly WorkshopVillage localVillage;
    private readonly Dictionary<string,WorkshopRoom> localRooms=new();
    private readonly Dictionary<string,Dictionary<string,string>> roomBases=new();
    private readonly Dictionary<string,string> floorBasis,villageBasis;
    private readonly Dictionary<(WorkshopFloor.Scenario,int),Dictionary<string,string>> slotFloorBases=new();
    private readonly Dictionary<string,string> parents;
    private readonly string sourceWorkspace;
    private const string FloorKey="floor", VillageKey="village";

    internal WorkshopPackageReview(WorkshopPackage bundle,WorkshopCatalog catalog)
    {
        package=bundle;
        this.catalog=catalog;
        sourceWorkspace=WorkshopWorkspace.ActiveId;
        if(ProjectContentStore.Authoring)
        {
            floorBasis=ProjectFloorData.Snapshot(bundle.floor);
            villageBasis=ProjectVillageData.Snapshot();
            foreach(var slot in bundle.floor.slots)
            {
                var key=(slot.scenario,slot.floor);
                if(key==(bundle.floor.scenario,bundle.floor.FloorNumber) || slotFloorBases.ContainsKey(key))continue;
                slotFloorBases.Add(key,ProjectFloorData.Snapshot(new WorkshopFloor{scenario=slot.scenario,floor=Math.Max(1,slot.floor)}));
            }
        }
        parents=bundle.parents.ToDictionary(r=>r.key,r=>r.hash);
        // A damaged local file stays in its original workspace. A valid team copy can still be opened.
        localFloor=ReadLocal(WorkshopFloor.Load,out string floorError) ?? new WorkshopFloor();
        localVillage=ReadLocal(WorkshopVillage.Load,out string villageError) ?? new WorkshopVillage();
        if(floorBasis!=null)
        {
            localFloor=new WorkshopFloor{scenario=bundle.floor.scenario,floor=bundle.floor.floor,seed=bundle.floor.seed};
            ProjectFloorData.Read(localFloor,floorBasis);
        }
        var floorPart=Add(FloorKey,"층과 튜토리얼",floorBasis?.Count>0 ? JsonUtility.ToJson(localFloor) : floorError==null ? LocalJson("floor",localFloor) : null,JsonUtility.ToJson(bundle.floor),
            floorError ?? FloorLabel(localFloor),FloorLabel(bundle.floor),FloorChanges);
        if (floorPart.canChoose)
        {
            floorPart.Difference("무대",FloorLabel(localFloor),FloorLabel(bundle.floor));
            floorPart.Difference("방 수 / 웨이브",$"{localFloor.rooms} / {localFloor.waves}",$"{bundle.floor.rooms} / {bundle.floor.waves}");
            string Route(WorkshopFloor f)=>string.Join(" → ",f.TutorialOrder(catalog).Select(id=>catalog.FindRoom(id)?.title ?? id));
            floorPart.Difference("튜토리얼 순서",Route(localFloor),Route(bundle.floor));
            AddExample(floorPart,"방 연결",localFloor.slots,bundle.floor.slots,s=>(s.scenario,s.floor,s.seed,s.cell),
                s=>$"{s.cell} · {s.document.Substring(0,8)}");
        }
        var villagePart=Add(VillageKey,"마을",villageBasis?.Count>0 ? JsonUtility.ToJson(localVillage) : villageError==null ? LocalJson("village",localVillage) : null,JsonUtility.ToJson(bundle.village),
            villageError ?? VillageLabel(localVillage),VillageLabel(bundle.village),VillageChanges);
        if (villagePart.canChoose)
        {
            AddExample(villagePart,"NPC 배치와 인사말",localVillage.actors,bundle.village.actors,n=>n.id,
                n=>$"{n.id} {n.position} · {(n.hidden ? "숨김" : "표시")} · {n.greeting}");
            villagePart.Difference("조명",LightLabel(localVillage),LightLabel(bundle.village));
        }
        foreach (var incoming in bundle.rooms)
        {
            var path=WorkshopFiles.FileFor(incoming.id);
            string error=null;
            var local=File.Exists(path) ? ReadLocal(()=>WorkshopFiles.Read(path,catalog),out error) : null;
            if(ProjectContentStore.Authoring)
            {
                var snapshot=ProjectRooms.Snapshot(incoming.id);
                roomBases.Add(incoming.id,snapshot);
                if(snapshot.Count>0)local=ProjectRooms.Decode(incoming.id,snapshot,catalog);
            }
            if (local!=null) localRooms.Add(incoming.id,local);
            var part=Add(incoming.id,incoming.title,local?.Serialize(),incoming.Serialize(),error ?? local?.title,incoming.title,
                ()=>RoomChanges(local,incoming));
            if (part.canChoose) DescribeRoom(part,local,incoming);
        }
    }

    private static T ReadLocal<T>(Func<T> read,out string error) where T : class
    {
        error=null;
        try { return read(); }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException)
        {
            error="읽기 실패 · "+ex.Message;
            return null;
        }
    }

    private static string LocalJson(string stem,object doc) =>
        File.Exists(Path.Combine(WorkshopWorkspace.Current,stem+".json")) ? JsonUtility.ToJson(doc) : null;
    private static string FloorLabel(WorkshopFloor f) => $"층 {f.FloorNumber} · 시드 {f.seed} · 연결 {f.slots.Count}";
    private static string VillageLabel(WorkshopVillage v) => $"NPC {v.actors.Count} · 조명 "+(v.useLighting ? "직접 설정" : "기본");

    private Part Add(string key,string name,string local,string team,string localLabel,string teamLabel,Func<string> describeChanges)
    {
        string lhs=local==null ? null : WorkshopPackage.Hash(local), rhs=WorkshopPackage.Hash(team);
        parents.TryGetValue(key,out string parent);
        bool differs=lhs!=null && lhs!=rhs;
        string state=lhs==null ? "팀에서 추가" : lhs==rhs ? "같은 개정" : parent==lhs ? "팀에서 수정" :
            parent==rhs ? "내가 수정" : parent==null ? "다른 개정" : "양쪽에서 수정";
        string changes=differs ? describeChanges() : "";
        var part=new Part {key=key,name=name,teamHash=rhs,canChoose=differs,useLocal=differs && parent==rhs,
            summary=differs && changes.Length>0 ? state+" · "+changes : state,
            localLabel=localLabel ?? "저장본 없음", teamLabel=teamLabel};
        parts.Add(part);
        return part;
    }

    private static string LightLabel(WorkshopVillage v) => !v.useLighting ? "기본" :
        $"태양 {v.lighting.azimuth:0}° · 밝기 {v.lighting.intensity:0.##} · 안개 {v.lighting.mist:0.##} · 블룸 {v.lighting.bloom:0.##}";

    private static void AddExample<T,TKey>(Part part,string label,IEnumerable<T> local,IEnumerable<T> team,
        Func<T,TKey> key,Func<T,string> describe) where T : class
    {
        var a=local.ToDictionary(key); var b=team.ToDictionary(key);
        foreach (var k in a.Keys.Union(b.Keys))
        {
            a.TryGetValue(k,out var lhs); b.TryGetValue(k,out var rhs);
            if (lhs!=null && rhs!=null && JsonUtility.ToJson(lhs)==JsonUtility.ToJson(rhs)) continue;
            part.Difference(label,lhs==null ? "없음" : describe(lhs),rhs==null ? "없음" : describe(rhs));
            break;
        }
    }

    private void DescribeRoom(Part part,WorkshopRoom a,WorkshopRoom b)
    {
        part.Difference("이름",a.title,b.title);
        part.Difference("시드",a.seed.ToString(),b.seed.ToString());
        AddExample(part,"출구",a.exits,b.exits,e=>e.direction,e=>$"{e.direction} → {e.local.x:0.###}, {e.local.y:0.###}");
        AddExample(part,"타일",a.cells,b.cells,c=>(c.layer,c.at),c=>$"{c.at} · {catalog.Tile(c.tile)?.name ?? "빈칸"} · {c.style}");
        AddExample(part,"오브젝트",a.pieces,b.pieces,p=>p.id,p=>(p.removed ? "삭제" : "배치")+" · "+p.local);
        part.Difference("웨이브",string.Join(" → ",a.waves.Select(id=>catalog.FindCluster(id).asset.name)),
            string.Join(" → ",b.waves.Select(id=>catalog.FindCluster(id).asset.name)));
        string[] rewards={"층 설정","플레이어 스킬","메인 소환","아이템"};
        part.Difference("보상",rewards[a.reward],rewards[b.reward]);
    }

    private static int Changed<T,TKey>(IEnumerable<T> a,IEnumerable<T> b,Func<T,TKey> key)
    {
        var local=a.ToDictionary(key,x=>JsonUtility.ToJson(x));
        var team=b.ToDictionary(key,x=>JsonUtility.ToJson(x));
        return local.Keys.Union(team.Keys).Count(k=>!local.TryGetValue(k,out var lhs) || !team.TryGetValue(k,out var rhs) || lhs!=rhs);
    }
    private static string RoomChanges(WorkshopRoom local,WorkshopRoom team)
    {
        var changes=new List<string>();
        if (local.title!=team.title) changes.Add("이름");
        if (local.seed!=team.seed) changes.Add("시드");
        int tiles=Changed(local.cells,team.cells,c=>(c.layer,c.at));
        if (tiles>0) changes.Add($"타일 {tiles}칸");
        if (!local.styles.Select(s=>JsonUtility.ToJson(s)).SequenceEqual(team.styles.Select(s=>JsonUtility.ToJson(s)))) changes.Add("타일 모양");
        int pieces=Changed(local.pieces,team.pieces,p=>p.id);
        if (pieces>0) changes.Add($"오브젝트 {pieces}개");
        foreach (var direction in local.exits.Select(e=>e.direction).Union(team.exits.Select(e=>e.direction)))
        {
            var a=local.exits.Find(e=>e.direction==direction); var b=team.exits.Find(e=>e.direction==direction);
            if (a!=null && b!=null && a.local.Equals(b.local)) continue;
            changes.Add((direction==Vector2Int.up ? "북" : direction==Vector2Int.down ? "남" : direction==Vector2Int.left ? "서" : "동")+"문");
        }
        if (!local.waves.SequenceEqual(team.waves)) changes.Add("웨이브");
        if (local.reward!=team.reward) changes.Add("보상");
        return string.Join(" / ",changes);
    }

    private string FloorChanges()
    {
        var a=localFloor; var b=package.floor;
        var diff=new List<string>();
        if (a.scenario!=b.scenario || a.floor!=b.floor || a.seed!=b.seed) diff.Add("무대·층·시드");
        if (a.rooms!=b.rooms || a.waves!=b.waves) diff.Add("방 수·웨이브");
        if (!a.TutorialOrder(catalog).SequenceEqual(b.TutorialOrder(catalog))) diff.Add("튜토리얼 순서");
        int slots=Changed(a.slots,b.slots,s=>(s.scenario,s.floor,s.seed,s.cell));
        if (slots>0) diff.Add($"방 연결 {slots}곳");
        return string.Join(" / ",diff);
    }

    private string VillageChanges()
    {
        var a=localVillage; var b=package.village;
        int actors=Changed(a.actors,b.actors,npc=>npc.id);
        bool lighting=a.useLighting!=b.useLighting || a.useLighting && JsonUtility.ToJson(a.lighting)!=JsonUtility.ToJson(b.lighting);
        return string.Join(" / ",new[]{actors>0 ? $"NPC {actors}개" : null, lighting ? "조명" : null}.Where(s=>s!=null));
    }

    internal WorkshopWorkspace.Entry Import()
    {
        if (sourceWorkspace!=WorkshopWorkspace.ActiveId) throw new IOException("작업 공간이 바뀌었습니다. 다시 비교하세요.");
        var choices=parts.ToDictionary(p=>p.key);
        var floor=choices[FloorKey].useLocal ? localFloor : package.floor;
        var village=choices[VillageKey].useLocal ? localVillage : package.village;
        var rooms=package.rooms.ToDictionary(r=>r.id,r=>choices[r.id].useLocal ? localRooms[r.id] : r);
        // A local floor can refer to saved rooms absent from the sender's package.
        foreach (var slot in floor.slots)
            if (!rooms.ContainsKey(slot.document))
                rooms.Add(slot.document,ProjectContentStore.Authoring && ProjectRooms.Snapshot(slot.document).Count>0 ?
                    ProjectRooms.Decode(slot.document,ProjectRooms.Snapshot(slot.document),catalog) : WorkshopFiles.Read(WorkshopFiles.FileFor(slot.document),catalog));
        WorkshopPackage.ValidateContents(catalog,floor,village,rooms.Values);
        // The received versions are the common comparison point when sending edits back.
        return WorkshopWorkspace.Publish(package.title,floor,village,rooms.Values,
            parts.Select(p=>new WorkshopPackage.Revision {key=p.key,hash=p.teamHash}).ToList());
    }
    internal void Publish()
    {
        if(!ProjectContentStore.Authoring || sourceWorkspace!=WorkshopWorkspace.ActiveId || floorBasis==null)
            throw new IOException("기본 프로젝트 작업 공간에서 묶음을 다시 여세요.");
        var choices=parts.ToDictionary(p=>p.key);
        foreach(var room in package.rooms)
        {
            if(choices[room.id].useLocal)continue;
            ProjectRooms.Save(room,roomBases[room.id],catalog,out _);
        }
        if(!choices[VillageKey].useLocal) ProjectVillageData.Save(package.village,villageBasis);
        if(!choices[FloorKey].useLocal)
        {
            if(package.floor.scenario!=WorkshopFloor.Scenario.Village)ProjectFloorData.Save(package.floor,floorBasis);
            foreach(var pair in slotFloorBases)
            {
                var floor=new WorkshopFloor{scenario=pair.Key.Item1,floor=Math.Max(1,pair.Key.Item2)};
                ProjectFloorData.Read(floor,pair.Value);
                foreach(var slot in package.floor.slots.Where(s=>(s.scenario,s.floor)==pair.Key))
                {
                    floor.slots.RemoveAll(s=>s.seed==slot.seed && s.cell==slot.cell);
                    floor.slots.Add(slot);
                }
                ProjectFloorData.Save(floor,pair.Value);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static class ProjectFloorData
{
    internal static string Key(WorkshopFloor floor)=>"WORLD/FLOORS/"+floor.scenario+"/"+floor.FloorNumber;
    internal static Dictionary<string,string> Snapshot(WorkshopFloor floor)=>
        ProjectContentStore.WithDocument(Key(floor),(folder,_)=>ProjectContentStore.Read(folder));

    internal static void Read(WorkshopFloor floor, Dictionary<string,string> files)
    {
        floor.rooms=files.TryGetValue("layout.json",out var layout) ? (int)JObject.Parse(layout)["rooms"] : -1;
        floor.waves=files.TryGetValue("waves.json",out var waves) ? (int)JObject.Parse(waves)["waves"] : -1;
        if(floor.scenario==WorkshopFloor.Scenario.Tutorial)
            floor.tutorialOrder=files.TryGetValue("route.json",out var route) ? JArray.Parse(route).Values<string>().ToList() : new();
        floor.slots.RemoveAll(slot=>slot.floor==floor.FloorNumber && slot.scenario==floor.scenario);
        foreach(var pair in files.Where(p=>p.Key.StartsWith("SLOTS/",StringComparison.Ordinal)))
        {
            var slot=UnityEngine.JsonUtility.FromJson<WorkshopFloor.Slot>(pair.Value);
            if(slot==null || slot.scenario!=floor.scenario || slot.floor!=floor.FloorNumber || pair.Key!=SlotFile(slot))
                throw new InvalidDataException("층과 방의 연결 파일을 확인하세요. "+pair.Key);
            floor.slots.Add(slot);
        }
        floor.Validate();
    }
    private static string SlotFile(WorkshopFloor.Slot slot)=>"SLOTS/"+slot.seed+"_"+slot.cell.x+"_"+slot.cell.y+".json";
    internal static Dictionary<string,string> Save(WorkshopFloor floor,Dictionary<string,string> basis)
    {
        var next=new Dictionary<string,string>();
        if(floor.rooms>=0) next["layout.json"]=ProjectContentStore.Json(new JObject{["rooms"]=floor.rooms});
        if(floor.waves>=0) next["waves.json"]=ProjectContentStore.Json(new JObject{["waves"]=floor.waves});
        if(floor.scenario==WorkshopFloor.Scenario.Tutorial && floor.tutorialOrder.Count>0)
            next["route.json"]=ProjectContentStore.Json(new JArray(floor.tutorialOrder));
        foreach(var slot in floor.slots.Where(s=>s.floor==floor.FloorNumber && s.scenario==floor.scenario))
            next[SlotFile(slot)]=ProjectContentStore.Json(JObject.Parse(UnityEngine.JsonUtility.ToJson(slot)));
        var saved=ProjectContentStore.Save(Key(floor),basis,next,merged=>{
            var copy=new WorkshopFloor{scenario=floor.scenario,floor=floor.floor};
            Read(copy,merged);
            var catalog=UnityEngine.Resources.Load<WorkshopCatalog>("WorkshopCatalog");
            if(copy.tutorialOrder.Any(id=>catalog.FindRoom(id)==null)) throw new InvalidDataException("튜토리얼 방을 찾을 수 없습니다.");
            foreach(var slot in copy.slots)
                if(ProjectRooms.Decode(slot.document,ProjectRooms.Snapshot(slot.document),catalog).template!=slot.template)
                    throw new InvalidDataException("층에 연결된 방 원본을 확인하세요.");
        });
        Read(floor,saved);
        return saved;
    }

    internal static WorkshopFloor ForGame(int number,bool elite)
    {
        var floor=new WorkshopFloor{floor=Math.Max(1,number),scenario=number==0 ? WorkshopFloor.Scenario.Tutorial :
            elite ? WorkshopFloor.Scenario.Elite : WorkshopFloor.Scenario.Dungeon};
        if(!Directory.Exists(Path.Combine(ProjectContentStore.Root,Key(floor)))) return null;
        bool present=false;
        bool valid=ProjectContentStore.TryReadPublished(Key(floor),()=>{
            var snapshot=Snapshot(floor);
            present=snapshot.Count>0;if(present)Read(floor,snapshot);
        });
        return present && valid ? floor : null;
    }
}

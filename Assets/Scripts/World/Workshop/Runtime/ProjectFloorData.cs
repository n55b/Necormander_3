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
        floor.Validate();
    }
    internal static Dictionary<string,string> Save(WorkshopFloor floor,Dictionary<string,string> basis)
    {
        var next=new Dictionary<string,string>();
        if(floor.rooms>=0) next["layout.json"]=ProjectContentStore.Json(new JObject{["rooms"]=floor.rooms});
        if(floor.waves>=0) next["waves.json"]=ProjectContentStore.Json(new JObject{["waves"]=floor.waves});
        if(floor.scenario==WorkshopFloor.Scenario.Tutorial && floor.tutorialOrder.Count>0)
            next["route.json"]=ProjectContentStore.Json(new JArray(floor.tutorialOrder));
        var saved=ProjectContentStore.Save(Key(floor),basis,next,merged=>{
            var copy=new WorkshopFloor{scenario=floor.scenario,floor=floor.floor};
            Read(copy,merged);
            var catalog=UnityEngine.Resources.Load<WorkshopCatalog>("WorkshopCatalog");
            if(copy.tutorialOrder.Any(id=>catalog.FindRoom(id)==null)) throw new InvalidDataException("튜토리얼 방을 찾을 수 없습니다.");
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

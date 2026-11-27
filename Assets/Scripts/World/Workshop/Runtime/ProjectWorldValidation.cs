using System;
using System.IO;
using System.Linq;

public static class ProjectWorldValidation
{
    public static void ValidatePublished(WorkshopCatalog catalog)
    {
        string root=ProjectContentStore.Root;
        _=new ProjectPlayerTuning();
        var floors=Path.Combine(root,"WORLD","FLOORS");
        if(Directory.Exists(floors))
        foreach(string region in Directory.GetDirectories(floors))
        foreach(string location in Directory.GetDirectories(region))
        {
            if(!Enum.TryParse<WorkshopFloor.Scenario>(Path.GetFileName(region),out var scenario) ||
                !int.TryParse(Path.GetFileName(location),out int number))
                throw new InvalidDataException("층 경로를 확인하세요. "+location);
            var floor=new WorkshopFloor{scenario=scenario,floor=Math.Max(1,number)};
            if(floor.FloorNumber!=number)throw new InvalidDataException("층 번호와 유형을 확인하세요. "+location);
            var files=ProjectFloorData.Snapshot(floor);
            foreach(string key in files.Keys)
                if(key is not ("layout.json" or "waves.json" or "route.json"))
                    throw new InvalidDataException("알 수 없는 층 파일입니다. "+location+"/"+key);
            ProjectFloorData.Read(floor,files);
            if(floor.tutorialOrder.Any(id=>catalog.FindRoom(id)==null))
                throw new InvalidDataException("튜토리얼 방 참조를 확인하세요. "+location);
        }
        if(Directory.Exists(Path.Combine(root,ProjectVillageData.Document)))
        {
            var village=new WorkshopVillage();
            var files=ProjectVillageData.Snapshot();
            ProjectVillageData.Read(village,files);
            foreach(var key in files.Keys)
                if(key!="lighting.json" && !village.actors.Any(a=>catalog.villageActors.Contains(a.id) &&
                    (key=="GREETINGS/"+a.id+".json" || key=="NPC/"+a.id+".json")))
                    throw new InvalidDataException("마을 NPC 참조를 확인하세요. "+key);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

public static class ProjectWorldValidation
{
    public static void ValidatePublished(WorkshopCatalog catalog)
    {
        string root=ProjectContentStore.Root;
        string checkout=Path.GetDirectoryName(root);
        string gitPath=Path.Combine(checkout,".git");
        if(File.Exists(gitPath)||Directory.Exists(gitPath))
        {
            var conflicts=ProjectGitReview.Files();
            if(conflicts.Count!=0)throw new InvalidDataException("빌드 전 DATA 병합을 마쳐 주세요. "+conflicts[0].path);
        }
        _=new ProjectPlayerTuning();
        var pool=Path.Combine(root,"WORLD","POOLS");
        if(Directory.Exists(pool))
        foreach(string file in Directory.EnumerateFiles(pool,"*.json",SearchOption.AllDirectories))
        {
            string template=Directory.GetParent(file).Name;
            string id=Path.GetFileNameWithoutExtension(file);
            string selected=(string)JObject.Parse(File.ReadAllText(file))["room"];
            if(ProjectRooms.IsRemoved(id))throw new InvalidDataException("삭제한 방이 던전 후보에 남아 있습니다. "+file);
            var room=ProjectRooms.Decode(id,ProjectRooms.Snapshot(id),catalog);
            if(id!=selected || template!=room.template || catalog.FindRoom(template)==null ||
                Path.GetFullPath(file)!=Path.GetFullPath(Path.Combine(pool,template,id+".json")))
                throw new InvalidDataException("방 후보 연결을 확인하세요. "+file);
        }
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
                if(key is not ("layout.json" or "waves.json" or "route.json") && !key.StartsWith("SLOTS/",StringComparison.Ordinal))
                    throw new InvalidDataException("알 수 없는 층 파일입니다. "+location+"/"+key);
            ProjectFloorData.Read(floor,files);
            if(floor.tutorialOrder.Any(id=>catalog.FindRoom(id)==null))
                throw new InvalidDataException("튜토리얼 방 참조를 확인하세요. "+location);
            foreach(var slot in floor.slots)
                if(ProjectRooms.IsRemoved(slot.document))throw new InvalidDataException("삭제한 방이 층에 연결돼 있습니다. "+location);
                else if(ProjectRooms.Decode(slot.document,ProjectRooms.Snapshot(slot.document),catalog).template!=slot.template)
                    throw new InvalidDataException("층에 연결된 방 원본이 다릅니다. "+location);
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

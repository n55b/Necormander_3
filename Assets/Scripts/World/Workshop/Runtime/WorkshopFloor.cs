using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Overrides belong to the creator workspace. Each play session receives its own map data.
[Serializable]
internal sealed class WorkshopFloor
{
    internal enum Scenario { Dungeon, Elite, Tutorial, Village }
    public int version = 1;
    public Scenario scenario;
    public int floor = 1, seed = 907, rooms = -1, waves = -1;
    // Empty means inherit the current project sequence (including older floor.json files).
    public List<string> tutorialOrder = new();
    [NonSerialized] private Dictionary<string,string> projectBasis;
    internal void SelectStage(Scenario stage,int number)
    {
        if(scenario==stage && floor==number) return;
        scenario=stage; floor=number;
        if(!ProjectContentStore.Authoring) return;
        projectBasis=ProjectFloorData.Snapshot(this);
        ProjectFloorData.Read(this,projectBasis);
    }

    internal List<string> TutorialOrder(WorkshopCatalog catalog)
    {
        if (tutorialOrder is { Count: > 0 }) return new List<string>(tutorialOrder);
        return catalog.generation.tutorialRooms.Select(prefab =>
            catalog.rooms.FirstOrDefault(room => room.room != null && room.room.gameObject == prefab)?.id)
            .Where(id => id != null).ToList();
    }

    internal void ApplyTutorialOrder(List<string> order, WorkshopCatalog catalog)
    {
        var previous = TutorialOrder(catalog);
        if (order.Count != previous.Count || !order.OrderBy(id => id).SequenceEqual(previous.OrderBy(id => id)))
            throw new InvalidDataException("튜토리얼의 방 목록이 달라졌습니다.");
        var saved = tutorialOrder;
        tutorialOrder = new List<string>(order);
        // The new order must still make a playable floor; otherwise the old order stays.
        try { Preview(catalog, out _); }
        catch { tutorialOrder = saved; throw; }
    }
    private static string FilePath => Path.Combine(WorkshopWorkspace.Home, "floor.json");
    public int FloorNumber => scenario == Scenario.Tutorial ? 0 : floor;

    public static WorkshopFloor Load()
    {
        var doc = File.Exists(FilePath) ? JsonUtility.FromJson<WorkshopFloor>(File.ReadAllText(FilePath)) : new WorkshopFloor();
        if (doc == null) throw new InvalidDataException("층 설정을 읽을 수 없습니다.");
        doc.Validate();
        if(ProjectContentStore.Authoring)
        {
            doc.projectBasis=ProjectFloorData.Snapshot(doc);
            ProjectFloorData.Read(doc,doc.projectBasis);
        }
        return doc;
    }

    public void Save(bool publish=true)
    {
        Validate();
        if(publish && ProjectContentStore.Authoring && scenario!=Scenario.Village)
            projectBasis=ProjectFloorData.Save(this,projectBasis ?? new Dictionary<string,string>());
        WorkshopFiles.WriteAtomic(FilePath,JsonUtility.ToJson(this,true));
    }

    internal DungeonLayout Preview(WorkshopCatalog catalog,out EnemyMinionDataSO elite)
    {
        elite=null;
        if (scenario==Scenario.Village) return null;
        var random=new System.Random(seed);
        if(catalog.elites.Length>0) elite=catalog.elites[random.Next(catalog.elites.Length)];
        var settings=CreateSettings(catalog.generation,catalog);
        try
        {
            var planner=new DungeonLayoutPlanner(settings,catalog.roomPool);
            var mode=scenario==Scenario.Tutorial ? DungeonLayoutMode.Tutorial :
                scenario==Scenario.Elite ? DungeonLayoutMode.EliteArena :
                settings.GetBossForFloor(floor)!=null ? DungeonLayoutMode.Boss : DungeonLayoutMode.Normal;
            string error=null;
            for(int i=0;i<15;i++)
                if(planner.TryPlan(mode,FloorNumber,elite?.dedicatedRoomPrefab,random,"Workshop",out var plan,out error))
                    return plan;
            throw new InvalidDataException(error);
        }
        finally { UnityEngine.Object.Destroy(settings); }
    }

    internal void Validate()
    {
        tutorialOrder ??= new List<string>();
        if (version != 1 || !Enum.IsDefined(typeof(Scenario), scenario) || floor < 1 || floor > 99 ||
            (rooms != -1 && (rooms < 2 || rooms > 40)) || (waves != -1 && (waves < 1 || waves > 8)))
            throw new InvalidDataException("층 설정의 형식이나 범위를 확인하세요.");
    }

    public MapGenerationDataSO CreateSettings(MapGenerationDataSO source, WorkshopCatalog catalog)
    {
        Validate();
        List<GameObject> route = null;
        if (scenario == Scenario.Tutorial && tutorialOrder.Count > 0)
        {
            if (tutorialOrder.Count > 128) throw new InvalidDataException("튜토리얼 방 수를 확인하세요.");
            route = tutorialOrder.Select(id => catalog.FindRoom(id)?.room?.gameObject ??
                throw new InvalidDataException("튜토리얼 방 원본을 찾을 수 없습니다.")).ToList();
        }
        var settings = UnityEngine.Object.Instantiate(source);
        if (route != null) settings.tutorialRooms = route;
        var tuning = settings.GetTuningForFloor(FloorNumber);
        if (tuning == null)
        {
            tuning = new MapGenerationDataSO.FloorTuningEntry { floor = FloorNumber };
            settings.floorTuning.Add(tuning);
        }
        if (rooms >= 0) tuning.totalRoomCount = rooms;
        if (waves >= 0) tuning.wavesCount = waves;
        if (scenario == Scenario.Elite) tuning.skipEliteEncounter = false;
        return settings;
    }
}

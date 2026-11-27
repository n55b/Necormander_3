using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class WorkshopTrial
{
    internal static WorkshopCatalog Library;
    internal static WorkshopRoom Draft;
    // 시험에 쓰는 방 문서. 방 편집의 Draft와 분리해 시험장이 편집 중인 방을 덮지 않는다.
    internal static WorkshopRoom Stage { get; private set; }
    internal const string ArenaId = "a7e2a7e2a7e2a7e2a7e2a7e2a7e2a7e2";
    internal static Vector2 View;
    internal static int Zoom = 1;
    internal static Vector3? Cursor;
    internal static bool Active { get; private set; }
    internal static string SaveFolder { get; private set; }
    private static UnityEngine.Random.State previousRandom;
    private static LightingPreview previousLook;
    internal static WorkshopVillage Village { get; private set; }
    internal static WorkshopFloor FloorTest { get; private set; }
    internal static bool RoomTest => Active && FloorTest == null;
    internal static int Seed => FloorTest?.seed ?? Stage.seed;
    private static Dictionary<Vector2Int,WorkshopRoom> floorEdits;
    internal static WorkshopRoom RoomAt(Vector2Int cell)
    {
        if (!Active) return null;
        if (RoomTest) return cell==Vector2Int.zero ? Stage : null;
        return floorEdits!=null && floorEdits.TryGetValue(cell,out var doc) ? doc : null;
    }
    internal static bool ReturnToFloor;
    internal static DungeonLayout PlannedFloor { get; private set; }
    internal static EnemyMinionDataSO PlannedElite { get; private set; }
    private static bool previousTutorial;
    internal static readonly Stack<string> Undo = new(), Redo = new();
    // 아이템 화면의 시험 지급(문서 id, 개수). 콘텐츠 시험이 채우고 WorkshopReturn이 플레이어 준비 뒤 주머니에 넣는다. 방·층 시험은 비운다.
    internal static readonly List<(string id,int count)> Loadout = new();
    public const string SceneName = "LevelWorkshop";
    public const string ScenePath = "Assets/Scenes/LevelWorkshop.unity";

    public static bool Available =>
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(ScenePath)!=null;
#else
        Application.CanStreamedLevelBeLoaded(SceneName);
#endif

    public static bool Open()
    {
        return Application.isPlaying && SceneManager.GetActiveScene().path != ScenePath &&
            !UnityNote.SceneLoader.IsLoading && Available && UnityNote.SceneLoader.Load(SceneName);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        SceneManager.sceneLoaded -= Loaded;
        Village=null; floorEdits=null; PlannedFloor=null; PlannedElite=null; Active=false; FloorTest=null; ReturnToFloor=false; Library=null; Draft=null; Stage=null; Zoom=1; Cursor=null;
        Undo.Clear(); Redo.Clear(); Loadout.Clear();
    }
    internal static bool Play(Vector3? cursor)
    {
        Stage=Draft; Loadout.Clear();
        return Begin(cursor,null);
    }
    // 전투 없는 빈 방(첫 inPool Spawn 원본) 가운데에 플레이어, 오른쪽 1.5~3 유닛 걸을 수 있는 칸에 허수아비. 문서는 저장하지 않는다.
    internal static bool PlayArena()
    {
        var template=Library.rooms.First(r=>r.type==RoomType.Spawn && r.inPool);
        var dummy=Library.Dummy ?? throw new InvalidDataException("허수아비가 카탈로그에 없습니다. Tools > 제작실 > 원본 카탈로그 다시 굽기를 실행하세요.");
        var root=template.room.transform;
        var ground=WorkshopCatalog.Map(root,0);
        var wall=WorkshopCatalog.Map(root,1); var blocked=WorkshopCatalog.Map(root,3);
        bool Walkable(Vector3Int c)=>ground.HasTile(c) && wall?.HasTile(c)!=true && blocked?.HasTile(c)!=true;   // WorkshopPreview.Walkable과 같은 규칙
        Vector3 Local(Vector3Int c)=>root.InverseTransformPoint(ground.GetCellCenterWorld(c));
        var cells=new List<Vector3>();
        foreach (var c in ground.cellBounds.allPositionsWithin) if (Walkable(c)) cells.Add(Local(c));
        var middle=root.InverseTransformPoint(ground.transform.TransformPoint(ground.localBounds.center));
        var start=cells.OrderBy(p=>Vector2.Distance(p,middle)).First();                 // 가운데에서 가장 가까운 바닥
        var spot=cells.Where(p=>p.x>start.x && Vector2.Distance(p,start) is >= 1.5f and <= 3f)
            .OrderBy(p=>Vector2.Distance(p,start)).Select(p=>(Vector3?)p).FirstOrDefault() ?? start+Vector3.right*1.5f;
        var arena=new WorkshopRoom{id=ArenaId,title="시험장",template=template.id,baseRevision=template.revision};
        arena.pieces.Add(new WorkshopRoom.Piece{id="dummy",catalog=dummy.id,local=spot});
        Stage=arena;
        return Begin(start,null);
    }
    internal static bool PlayFloor(WorkshopFloor settings){ Loadout.Clear(); return Begin(null,settings); }
    private static bool Begin(Vector3? cursor, WorkshopFloor floor)
    {
        if (Active || UnityNote.SceneLoader.IsLoading) return false;
        if (floor == null) Stage.Validate(Library);
        if (floor!=null)
        {
            PlannedFloor=floor.Preview(Library,out var elite); PlannedElite=elite;
            floorEdits=floor.ReadEdits(PlannedFloor,Library);
        }
        Village=floor?.scenario==WorkshopFloor.Scenario.Village ? WorkshopVillage.Load() : null;
        FloorTest=floor; ReturnToFloor=floor!=null;
        previousTutorial=TutorialFlow.IsRunning;
        TutorialFlow.IsRunning=floor?.scenario==WorkshopFloor.Scenario.Tutorial;
        Cursor=cursor;
        previousLook=LightingPreview.Active; previousRandom=UnityEngine.Random.state;
        SaveFolder=Path.Combine(Application.temporaryCachePath,"WorkshopTrial",Guid.NewGuid().ToString("N"));
        if (ProjectContentStore.Authoring) DraftStore.Preview(true);   // 미저장 초안으로 시험한다. End가 저장값으로 되돌린다
        Active=true;
        SceneManager.sceneLoaded+=Loaded;
        UnityEngine.Random.InitState(Seed);
        if (UnityNote.SceneLoader.Load(floor?.scenario==WorkshopFloor.Scenario.Village ? "VillageScene" : "BattleScene")) return true;
        End();
        return false;
    }
    private static void Loaded(Scene scene, LoadSceneMode mode)
    {
        if (mode!=LoadSceneMode.Single) return;
        if (scene.name!=SceneName && scene.name!="StartScene")
        {
            if (Village!=null && scene.name!="VillageScene") LightingPreview.Active=previousLook;
            var hud=new GameObject("Workshop test controls");
            hud.AddComponent<WorkshopReturn>();
        }
        else End();
    }
    private static void End()
    {
        SceneManager.sceneLoaded-=Loaded;
        UnityEngine.Random.state=previousRandom; LightingPreview.Active=previousLook;
        TutorialFlow.IsRunning=previousTutorial;
        Village=null; floorEdits=null; PlannedFloor=null; PlannedElite=null; Active=false; FloorTest=null;
        DraftStore.Preview(false);
    }

    internal static DungeonLayout Layout()
    {
        var source=Library.FindRoom(Stage.template);
        var neighbors=Library.rooms.First(r=>r.type==RoomType.Spawn && r.inPool);
        var directions=source.room.GetComponentsInChildren<RoomAnchor>(true).Select(a=>a.direction)
            .Concat(Stage.exits.Select(e=>e.direction)).Distinct().Where(RoomConnectivity.IsCardinal).ToList();
        var rooms=new List<DungeonLayout.Room> { new(Vector2Int.zero,source.type,source.room.gameObject,directions) };
        var links=new List<DungeonLayout.Door>();
        foreach (var direction in directions)
        {
            if (!neighbors.room.GetComponentsInChildren<RoomAnchor>(true).Any(a=>a.direction==-direction)) continue;
            links.Add(new DungeonLayout.Door(0,rooms.Count));
            rooms.Add(new DungeonLayout.Room(direction,RoomType.Spawn,neighbors.room.gameObject,new[]{-direction}));
        }
        return new DungeonLayout(DungeonLayoutMode.Normal,rooms,links);
    }
    internal static void PositionPlayer(MapGenerator generator, PlayerController player)
    {
        var room=generator.AllRooms[0];
        var anchor=room.anchors.FirstOrDefault(a=>a.isUsed);
        Vector3 pos=room.transform.position+(Vector3)room.centerOffset;
        if (Cursor.HasValue) pos=room.transform.TransformPoint(Cursor.Value);
        else if (anchor!=null) pos=anchor.transform.position-(Vector3)(Vector2)anchor.direction*2;
        if (NavMesh.SamplePosition(pos,out var hit,4,NavMesh.AllAreas)) pos=hit.position;
        player.transform.position=pos;
        generator.SetCurrentRoom(room);
        if (room.roomType is RoomType.Normal or RoomType.Elite or RoomType.Boss or RoomType.Augment)
            room.StartCombatEvent();
        else room.ForceEnter();
    }
}

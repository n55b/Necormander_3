#if UNITY_EDITOR || PROJECT_CONTENT_QA
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public sealed partial class ProjectRoomVerification
{
    private IEnumerator CheckPassages(WorkshopCatalog catalog)
    {
        const string document = "3c48b07887b14e128c2435c855dd20a0";
        var draft = ProjectRooms.Decode(document, ProjectRooms.Snapshot(document), catalog);
        if (!ProjectRooms.IsRegistered(draft)) throw new InvalidOperationException("Missing normal dungeon registration.");
        var template = catalog.FindRoom(draft.template);
        var probe = draft.cells.First(c => c.layer == 1);
        var expected = catalog.Tile(probe.tile);
        var previews = new System.Collections.Generic.Dictionary<Vector2Int, DoorSample>();
        yield return ReadWorkshopDoors(draft, template, previews);
        const int seed = 916;
        void Configure(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "BattleScene") return;
            TutorialFlow.IsRunning = false;
            GameManager.Instance.currentFloor = GameManager.Instance.debugStartFloor = 1;
            GameManager.Instance.debugEliteArena = false;
            GameManager.Instance.testMode_DisableAutoBattle = true;
            MapGenerator.Instance.SetNextLayoutSeed(seed);
        }
        SceneManager.sceneLoaded += Configure;
        try
        {
            if (!UnityNote.SceneLoader.Load("BattleScene")) throw new InvalidOperationException("Enter BattleScene");
            yield return null;
            while (UnityNote.SceneLoader.IsLoading || !GameManager.Instance.IsPlayerReady) yield return null;
        }
        finally { SceneManager.sceneLoaded -= Configure; }
        var map = MapGenerator.Instance;
        if (WorkshopTrial.Active || map.LastLayoutSeed != seed || !map.IsMapGenerationCompleted)
            throw new InvalidOperationException("Expected ordinary completed dungeon.");
        var painted = map.AllRooms.FirstOrDefault(r => r.name == template.room.name+"(Clone)" &&
            r.wallTilemap.GetTile((Vector3Int)probe.at) == expected && map.GetConnectedRooms(r).Count >= 2);
        Debug.Log("[Passages] seed="+seed+" rooms="+map.AllRooms.Count+" registered junction="+(painted != null));
        if (!painted) throw new InvalidOperationException("No ordinary dungeon selected the registered junction.");
        var walls = GameTiles(map,"Wall");
        var ground = GameTiles(map,"Ground");
        var blocked = GameTiles(map,"Unsteppable");
        var openings = new System.Collections.Generic.HashSet<Vector3Int>(
            painted.anchors.Where(a => a.isUsed).SelectMany(a => a.PassageCells(walls, false)));
        foreach (var cell in draft.cells.Where(c => c.layer == 1))
        {
            var local = (Vector3Int)cell.at;
            var at = walls.WorldToCell(painted.wallTilemap.GetCellCenterWorld(local));
            if (openings.Contains(at))
            {
                if(walls.HasTile(at)) throw new InvalidOperationException("Door did not cut authored terrain at "+local);
                continue;
            }
            var edge = walls.GetSprite(at);
            walls.RefreshTile(at);
            if (walls.GetTile(at) != catalog.Tile(cell.tile) || edge == null || edge != walls.GetSprite(at))
                throw new InvalidOperationException($"Terrain at {local} / {at}: expected {catalog.Tile(cell.tile)}, found {walls.GetTile(at)}, edge {edge?.name} then {walls.GetSprite(at)?.name}");
            bool touchesCut = openings.Any(c => Mathf.Abs(c.x-at.x)<=1 && Mathf.Abs(c.y-at.y)<=1);
            if(!touchesCut && edge != painted.wallTilemap.GetSprite(local))
                throw new InvalidOperationException("Uncut art differs from the workshop at "+local);
        }
        foreach (var sample in previews)
        {
            var door = painted.anchors.First(a=>a.isUsed && a.direction==sample.Key);
            sample.Value.Compare(door, walls, ground, GameTiles(map,"Shadow"), blocked);
        }
        int count=0;
        foreach(var room in map.AllRooms)
        foreach(var door in room.anchors.Where(a=>a.isUsed))
        {
            var at=walls.WorldToCell(door.transform.position);
            var outward=(Vector3Int)door.direction;
            var side=new Vector3Int(-outward.y,outward.x);
            int depth=Mathf.RoundToInt(2.5f/Vector3.Distance(walls.GetCellCenterWorld(at),walls.GetCellCenterWorld(at+outward)));
            if(room==painted) Debug.Log("[Passages] painted doorway "+door.direction+" local "+room.wallTilemap.WorldToCell(door.transform.position));
            for(int d=0;d<=depth;d++)
            for(int s=-1;s<=1;s++)
            {
                var cell=at-d*outward+s*side;
                if(!ground.HasTile(cell) || walls.HasTile(cell) || blocked.HasTile(cell))
                    throw new InvalidOperationException("Doorway "+room.name+" "+door.direction+" depth="+d+" offset="+s+" ground="+ground.HasTile(cell)+" wall="+walls.HasTile(cell)+" blocked="+blocked.HasTile(cell));
            }
            count++;
        }
        // Measure open-door navigation independently of intentional combat locks.
        foreach (var room in map.AllRooms) { room.isCleared=true; room.SetDoorsOpen(true); }
        var keyboard=InputSystem.AddDevice<Keyboard>();
        try
        {
            var player=GameManager.Instance.PLAYERCONTROLLER;
            var input=player.GetComponent<PlayerInput>();
            UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(keyboard,input.user);
            var activeDoor=painted.anchors.First(a=>a.isUsed).GetComponent<DoorController>();
            activeDoor.SendMessage("OnTriggerEnter2D",player.GetComponent<Collider2D>());
            if (!player.IsInputBlocked) throw new InvalidOperationException("Door did not start travel.");
            Vector3 beforeInterrupt=player.transform.position;
            Fader.FullScreenFader.FadeIn();
            yield return null;
            if(player.IsInputBlocked || Vector2.Distance(beforeInterrupt,player.transform.position)>.2f)
                throw new InvalidOperationException("Interrupted door left player locked or warped.");
            foreach(var neighbor in map.GetConnectedRooms(painted))
            {
                if(!RoomConnectivity.TryGetFacingAnchors(painted,neighbor,false,out var from,out var to)) throw new InvalidOperationException("Facing doors");
                Vector2 start=walls.GetCellCenterWorld(walls.WorldToCell(from.transform.position))-(Vector3)(Vector2)from.direction*2.5f;
                player.GetComponent<Rigidbody2D>().position=start;
                player.transform.position=(Vector3)start;
                Physics2D.SyncTransforms();
                yield return new WaitForFixedUpdate();
                yield return WalkThrough(keyboard,player,from,to);
                yield return WalkThrough(keyboard,player,to,from);
            }
            yield return WalkPaintedCorridor(map,catalog,keyboard,player);
        }
        finally { InputSystem.RemoveDevice(keyboard); }
        Debug.Log("[Passages] PASS "+count+" doorway mouths; "+map.GetConnectedRooms(painted).Count+" keyboard round trips");
    }

    private static IEnumerator WalkPaintedCorridor(MapGenerator map,WorkshopCatalog catalog,Keyboard keyboard,PlayerController player)
    {
        var root = new GameObject("Corridor regression", typeof(Grid), typeof(CorridorPainter));
        var grid=root.GetComponent<Grid>();
        var worldWall=GameTiles(map,"Wall");
        grid.cellSize=worldWall.layoutGrid.cellSize;
        grid.cellGap=worldWall.layoutGrid.cellGap;
        root.transform.localScale=worldWall.transform.lossyScale;
        root.transform.position=worldWall.GetComponent<TilemapRenderer>().bounds.max + new Vector3(24,24);
        root.transform.position=new Vector3(root.transform.position.x,root.transform.position.y,0);
        Tilemap Layer(string name)
        {
            var node=new GameObject(name,typeof(Tilemap));
            node.transform.SetParent(root.transform,false);
            return node.GetComponent<Tilemap>();
        }
        try
        {
            var floor=Layer("Ground"); var wall=Layer("Wall"); var shadow=Layer("Shadow"); var blocked=Layer("Unsteppable");
            wall.gameObject.layer=worldWall.gameObject.layer;
            wall.gameObject.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Static;
            var composite=wall.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType=CompositeCollider2D.GeometryType.Polygons;
            composite.generationType=CompositeCollider2D.GenerationType.Manual;
            var collision=wall.gameObject.AddComponent<TilemapCollider2D>();
            collision.compositeOperation=Collider2D.CompositeOperation.Merge;
            var settings=GameManager.Instance.CurrentStageMapData;
            var shore=catalog.tiles.First(t=>t.Title=="물가").tile;
            var cliff=catalog.tiles.First(t=>t.Title=="절벽").tile;
            var masonry=catalog.tiles.First(t=>t.Title=="벽").tile;
            RoomAnchor End(Vector3Int at,Vector2Int direction,TileBase brush)
            {
                var node=new GameObject("Entrance",typeof(RoomAnchor));
                node.transform.SetParent(root.transform,false);
                node.transform.position=wall.GetCellCenterWorld(at);
                var anchor=node.GetComponent<RoomAnchor>(); anchor.direction=direction;
                var side=new Vector3Int(-direction.y,direction.x);
                for(int d=0;d<=7;d++)
                for(int t=-3;t<=3;t++)
                {
                    var cell=at-d*(Vector3Int)direction+t*side;
                    floor.SetTile(cell,settings.floorTile);
                    if(d==1) wall.SetTile(cell,brush);
                }
                // A blocked-only cell used to be counted as existing floor during corridor painting.
                floor.SetTile(at,null); blocked.SetTile(at,settings.wallTile);
                return anchor;
            }
            var turn=new Vector3Int(8,0);
            var end=new Vector3Int(8,7);
            var left=End(Vector3Int.zero,Vector2Int.right,shore);
            var right=End(end,Vector2Int.down,cliff);
            wall.SetTile(new Vector3Int(3,2),masonry);
            var path=new System.Collections.Generic.List<Vector2Int>();
            for(int x=0;x<=turn.x;x++) path.Add(new Vector2Int(x,0));
            for(int y=1;y<=end.y;y++) path.Add(new Vector2Int(end.x,y));
            var painter=root.GetComponent<CorridorPainter>();
            painter.Init(floor,wall,shadow,blocked,settings.floorTile,settings.wallTile,settings.shadowTile);
            painter.RegisterCorridorWithAnchors(path,left,right,0);
            painter.FinalizePainting();
            foreach(var cell in painter.GetTotalGroundTiles())
                if(!floor.HasTile((Vector3Int)cell) || wall.HasTile((Vector3Int)cell) || blocked.HasTile((Vector3Int)cell))
                    throw new InvalidOperationException("Physical corridor has an unsupported or obstructed cell "+cell);
            foreach(var edgeCell in new[]{new Vector3Int(-1,2),new Vector3Int(10,8),new Vector3Int(3,2)})
            {
                var before=wall.GetSprite(edgeCell); wall.RefreshTile(edgeCell);
                if(before==null || wall.GetSprite(edgeCell)!=before) throw new InvalidOperationException("Stale corridor bank at "+edgeCell);
            }
            collision.ProcessTilemapChanges();
            composite.GenerateGeometry();
            var start=left.ArrivalPoint(wall); var corner=wall.GetCellCenterWorld(turn); var finish=right.ArrivalPoint(wall);
            player.GetComponent<Rigidbody2D>().position=start; player.transform.position=start;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            IEnumerator Walk(Vector2 target)
            {
                var body=player.GetComponent<Rigidbody2D>();
                float until=Time.realtimeSinceStartup+6;
                while(Vector2.Distance(body.position,target)>.14f)
                {
                    // Steer back to the centerline if braking at the preceding bend left a lateral offset.
                    Vector2 delta=target-body.position-body.linearVelocity*.06f;
                    var keys=new KeyboardState();
                    if(Mathf.Abs(delta.x)>.075f) keys.Set(delta.x>0 ? Key.D : Key.A,true);
                    if(Mathf.Abs(delta.y)>.075f) keys.Set(delta.y>0 ? Key.W : Key.S,true);
                    InputSystem.QueueStateEvent(keyboard,keys);
                    if(Time.realtimeSinceStartup>until)
                        throw new InvalidOperationException("Physical corridor stalled at "+body.position+" target="+target+" input="+player.MoveInput);
                    yield return new WaitForFixedUpdate();
                }
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                yield return new WaitForFixedUpdate();
            }
            yield return Walk(corner); yield return Walk(finish);
            yield return Walk(corner); yield return Walk(start);
            Debug.Log("[Passages] PASS physical corridor, two room entrances, bend and real keyboard round trip");
        }
        finally { Destroy(root); }
    }

    private static Tilemap GameTiles(MapGenerator map,string name)
    {
        var field=typeof(MapGenerator).GetField("global"+name+"Tilemap",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        return (Tilemap)field.GetValue(map);
    }

    private static IEnumerator WalkThrough(Keyboard keyboard,PlayerController player,RoomAnchor from,RoomAnchor to)
    {
        var dir=from.direction;
        var key=dir.x>0?Key.D:dir.x<0?Key.A:dir.y>0?Key.W:Key.S;
        Vector2 start=player.transform.position;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));
        var until=Time.realtimeSinceStartup+6;
        float travel=0;
        while (Vector2.Distance(player.transform.position,to.transform.position)>5)
        {
            travel=Mathf.Max(travel,Vector2.Dot((Vector2)player.transform.position-start,dir));
            if(Time.realtimeSinceStartup>until) throw new InvalidOperationException("Door traversal stalled "+from.name+" dir="+dir+" input="+player.MoveInput+" position="+player.transform.position+" travel="+travel);
            yield return null;
        }
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        yield return null;
        while(player.IsInputBlocked && Time.realtimeSinceStartup<until) yield return null;
        if(travel<.1f || player.IsInputBlocked || MapGenerator.Instance.CurrentRoom!=to.GetComponentInParent<RoomInstance>())
            throw new InvalidOperationException("Door did not complete real input traversal "+travel);
    }
}
#endif

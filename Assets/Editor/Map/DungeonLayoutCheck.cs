using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class DungeonLayoutCheck
{
    private const string Running = "DungeonLayoutCheck.Running";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IEnumerator checks;
    private static double deadline;

    static DungeonLayoutCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Running, false)) return;
            checks = Verify();
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += Tick;
        };
    }

    public static void Run()
    {
        Require(Application.isBatchMode && PlayerSettings.companyName == "NecromanderVerification",
            "Use the isolated verification runner.");
        EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity");
        SessionState.SetBool(Running, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        int exit;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Dungeon verification timed out.");
            if (checks.MoveNext()) return;
            Debug.Log("[DungeonLayoutCheck] PASS");
            exit = 0;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            exit = 1;
        }
        EditorApplication.update -= Tick;
        SessionState.EraseBool(Running);
        EditorApplication.Exit(exit);
    }

    private static IEnumerator Verify()
    {
        Application.runInBackground = true;
        var settings = AssetDatabase.LoadAssetAtPath<MapGenerationDataSO>("Assets/SOData/Map/MapGenerationData.asset");
        int bossFloor = settings.floorBosses.First(entry => entry != null && entry.boss != null).floor;
        while (UnityNote.SceneLoader.IsLoading) yield return null;

        RoomInstance oldRoom = null;
        string firstNormal = null;
        var cases = new[]
        {
            (mode: DungeonLayoutMode.Tutorial, floor: 0, seed: 907),
            (mode: DungeonLayoutMode.Normal, floor: 1, seed: 907),
            (mode: DungeonLayoutMode.Normal, floor: 1, seed: 907),
            (mode: DungeonLayoutMode.EliteArena, floor: 2, seed: 613),
            (mode: DungeonLayoutMode.Boss, floor: bossFloor, seed: 2718),
        };
        foreach (var scenario in cases)
        {
            TutorialFlow.IsRunning = scenario.mode == DungeonLayoutMode.Tutorial;
            void Configure(Scene scene, LoadSceneMode mode)
            {
                if (scene.name != "BattleScene") return;
                SceneManager.sceneLoaded -= Configure;
                var manager = Object.FindFirstObjectByType<GameManager>();
                manager.currentFloor = scenario.floor;
                manager.debugEliteArena = scenario.mode == DungeonLayoutMode.EliteArena;
                manager.testMode_DisableAutoBattle = true;
                Object.FindFirstObjectByType<MapGenerator>().SetNextLayoutSeed(scenario.seed);
            }
            // The seed enters through MapGenerator. Vary the unrelated global random stream each load.
            for (int i = 0; i < 20; i++) _ = UnityEngine.Random.value;
            SceneManager.sceneLoaded += Configure;
            Require(UnityNote.SceneLoader.Load("BattleScene"), "BattleScene can reload for each case");
            yield return null;
            while (UnityNote.SceneLoader.IsLoading && string.IsNullOrEmpty(GameManager.Instance?.InitializationError))
                yield return null;
            Require(GameManager.Instance != null && GameManager.Instance.IsPlayerReady &&
                GameManager.Instance.currentFloor == scenario.floor, $"{scenario.mode} scene ready: {GameManager.Instance?.InitializationError}");
            Require(oldRoom == null, "previous floor objects have been unloaded");

            var map = MapGenerator.Instance;
            Require(map.LastLayoutSeed == scenario.seed && map.CurrentGridLayout?.Mode == scenario.mode,
                "generator honors explicit seed and mode");
            VerifyScene(map);
            if (scenario.mode == DungeonLayoutMode.Normal)
            {
                var shop = map.AllRooms.FirstOrDefault(r => r.roomType == RoomType.Shop || r.roomType == RoomType.EnhanceShop);
                if (shop != null)
                {
                    Require(!shop.AllowsSprint, "unvisited shop has no sprint");
                    shop.FinishShopVisit();
                    Require(shop.AllowsSprint && !shop.CanSprintAt(shop.transform.position + Vector3.right * 10000),
                        "completed shop sprint ends outside its floor");
                }
                var combat = map.AllRooms.First(r => r.roomType == RoomType.Normal);
                Require(!combat.AllowsSprint, "unfinished combat room has no sprint");
            }
            oldRoom = map.AllRooms[0];
            int roomCount = map.AllRooms.Count;
            int doorCount = map.CurrentGridLayout.Doors.Count;

            switch (scenario.mode)
            {
                case DungeonLayoutMode.Tutorial:
                    Require(map.AllRooms.Count == settings.tutorialRooms.Count, "tutorial has its authored rooms");
                    var portal = map.AllRooms.Last().GetComponentInChildren<FloorProceedPortal>(true);
                    Require(portal != null && portal.Interact(GameManager.Instance.PLAYERCONTROLLER.gameObject),
                        "tutorial final portal accepts an interaction");
                    yield return null;
                    while (UnityNote.SceneLoader.IsLoading) yield return null;
                    Require(TutorialFlow.Completed && !TutorialFlow.IsRunning &&
                        SceneManager.GetActiveScene().name == "VillageScene" && GameManager.Instance.IsPlayerReady,
                        "tutorial exit loads the village and records completion");
                    Debug.Log("[DungeonLayoutCheck] Tutorial exit loaded VillageScene.");
                    break;
                case DungeonLayoutMode.Normal:
                    string signature = LayoutSignature(map);
                    if (firstNormal == null)
                    {
                        firstNormal = signature;
                        VerifyDoorTravel(map);
                        var shop = VerifyShop(map);
                        while (shop.MoveNext()) yield return shop.Current;
                    }
                    else
                    {
                        Require(firstNormal == signature, "same seed reproduces room assets, coordinates, doors and elite");
                        var rebuild = VerifyRebuild(map, scenario.seed);
                        while (rebuild.MoveNext()) yield return rebuild.Current;
                        oldRoom = map.AllRooms[0];
                    }
                    break;
                case DungeonLayoutMode.EliteArena:
                    Require(map.AllRooms.Count == 2 && map.AllRooms.Count(room => room.roomType == RoomType.Elite) == 1 &&
                        map.FloorElite != null, "elite arena contains a configured encounter");
                    break;
                case DungeonLayoutMode.Boss:
                    var boss = map.AllRooms.Single(room => room.roomType == RoomType.Boss);
                    Require(boss.GetComponent<BossRoomEvent>()?.PrewarmBossData == settings.GetBossForFloor(bossFloor),
                        "boss arena uses the authored boss for the floor");
                    var finish=VerifyBossFinish(boss);
                    while(finish.MoveNext()) yield return finish.Current;
                    break;
            }
            Debug.Log($"[DungeonLayoutCheck] {scenario.mode} seed={scenario.seed}: {roomCount} rooms / {doorCount} linked doors");
        }
    }

    private static IEnumerator VerifyBossFinish(RoomInstance room)
    {
        var encounter=room.GetComponent<BossRoomEvent>();
        var player=GameManager.Instance.PLAYERCONTROLLER;
        player.GetComponent<Rigidbody2D>().position=room.transform.position+(Vector3)room.centerOffset;
        room.ForceEnter();
        encounter.OnPlayerEnter(room);
        var field=typeof(BossRoomEvent).GetField("_activeBoss",Private);
        GameObject boss=null;
        float deadline=Time.realtimeSinceStartup+9;
        while(boss==null && Time.realtimeSinceStartup<deadline) {boss=field.GetValue(encounter) as GameObject;yield return null;}
        Require(boss!=null,"authored boss spawned in its room");
        var health=boss.GetComponentInChildren<CharacterHealth>();
        Require(health!=null && health.MaxHP>0,"live boss has a damage receiver");
        var camera=PixelArtUpscaleRig.RenderCamera;
        for(int i=0;i<12;i++) yield return null;
        PresentationRegressionCheck.SaveFrame(camera.targetTexture,"BossBeforeFinish");
        var info=new DamageInfo(health.MaxHP*8,DamageType.Fixed,player.gameObject,category:DamageCategory.BasicAttack,bypassGuard:true);
        info.feedbackComboFinish=true;
        bool confirmed=false;
        void Defeated() => confirmed=true;
        encounter.OnBossCombatClear.AddListener(Defeated);
        try
        {
            deadline=Time.realtimeSinceStartup+30;
            float nextStrike=0;
            int strikes=0; bool captured=false;
            while(!confirmed && Time.realtimeSinceStartup<deadline)
            {
                if(health!=null && !health.IsDead && !health.Invincible && Time.time>=nextStrike)
                {
                    info.amount=health.MaxHP*8;
                    health.TakeDamage(info);
                    nextStrike=Time.time+.45f;
                    strikes++;
                }
                yield return null;
                if(!captured && strikes>0 && health!=null && health.IsDead)
                { PresentationRegressionCheck.SaveFrame(camera.targetTexture,"BossFinisher"); captured=true; }
            }
            Debug.Log($"[DungeonLayoutCheck] Boss encounter took {strikes} finishing strikes across its phases.");
            Require(confirmed && room.isCleared,"boss finisher reaches encounter clear and reward");
        }
        finally {if(encounter!=null) encounter.OnBossCombatClear.RemoveListener(Defeated);}
    }

    private static void VerifyScene(MapGenerator map)
    {
        var layout = map.CurrentGridLayout;
        Require(layout.Rooms.Count == map.AllRooms.Count && NavMesh.CalculateTriangulation().indices.Length > 0,
            "all planned rooms exist with baked navigation");
        int stampedLayers = 0;
        for (int i = 0; i < layout.Rooms.Count; i++)
        {
            var expected = layout.Rooms[i];
            var room = map.AllRooms[i];
            Require(room.gridPosition == expected.Cell && room.roomType == expected.Type &&
                room.name == expected.Prefab.name + "(Clone)", $"room applied at {expected.Cell}");
            if (room.groundTilemap == null) continue;
            foreach (var cell in room.groundTilemap.cellBounds.allPositionsWithin)
            {
                var tile = room.groundTilemap.GetTile(cell);
                if (tile == null) continue;
                Require(map.GroundTileAt(room.groundTilemap.GetCellCenterWorld(cell)) == tile, $"ground copied at {expected.Cell}");
                stampedLayers++;
                break;
            }
        }
        Require(stampedLayers > 0, "generated floor contains at least one stamped ground layer");
        Require(map.AllRooms.Sum(room => map.GetConnectedRooms(room).Count) == 2 * layout.Doors.Count,
            "minimap graph matches the planned number of doors");
        foreach (var pair in layout.Doors)
        {
            var from = map.AllRooms[pair.From];
            var to = map.AllRooms[pair.To];
            Require(map.GetConnectedRooms(from).Contains(to) && map.GetConnectedRooms(to).Contains(from),
                "runtime graph has both directions");
            Require(RoomConnectivity.TryGetFacingAnchors(from, to, false, out var a, out var b), "anchors in live rooms face");
            var source = a.GetComponent<DoorController>();
            var destination = b.GetComponent<DoorController>();
            Require(source != null && destination != null && GetDestination(source) == destination &&
                GetDestination(destination) == source && a.GetComponents<BoxCollider2D>().Any(box => box.isTrigger),
                "paired teleporters are installed");
        }
    }

    private static DoorController GetDestination(DoorController door)
        => (DoorController)typeof(DoorController).GetField("_destinationDoor", Private).GetValue(door);

    private static string LayoutSignature(MapGenerator map)
    {
        var rooms = map.CurrentGridLayout.Rooms.Select(room =>
            $"{room.Cell}:{room.Type}:{room.Prefab.name}");
        var doors = map.CurrentGridLayout.Doors.Select(door => $"{door.From}-{door.To}");
        return string.Join("|", rooms.Concat(doors)) + "|" + map.FloorElite?.name;
    }

    private static void VerifyDoorTravel(MapGenerator map)
    {
        var start = map.AllRooms[0];
        var neighbor = map.GetConnectedRooms(start).First();
        Require(RoomConnectivity.TryGetFacingAnchors(start, neighbor, false, out var anchor, out _), "spawn has a neighbor");
        var door = anchor.GetComponent<DoorController>();
        var moved = (bool)typeof(DoorController).GetMethod("DoTeleport", Private)
            .Invoke(door, new object[] { GameManager.Instance.PLAYERCONTROLLER });
        var hud = GetHudMap();
        Require(moved && map.CurrentRoom == neighbor && hud != null &&
            hud.Cast<RectTransform>().Any(child => child.gameObject.activeSelf &&
                child.name == $"RoomUI_{neighbor.name}" && child.anchoredPosition == Vector2.zero),
            "door movement updates the room and minimap focus");
    }

    private static IEnumerator VerifyShop(MapGenerator map)
    {
        var shop = map.AllRooms.First(room => room.roomType == RoomType.Shop);
        var npc = shop.GetComponentInChildren<ShopNPC>();
        Require(shop.GetComponent<ShopRoomEvent>() != null && npc != null && npc.Stock.Count > 0 &&
            npc.Interact(GameManager.Instance.PLAYERCONTROLLER.gameObject) && ShopPopupUI.IsOpen,
            "generated shop initializes stock and opens its popup");
        var popup = ShopPopupUI.Ensure();
        float opened = Time.realtimeSinceStartup + .25f;
        while (Time.realtimeSinceStartup < opened) yield return null;
        Canvas.ForceUpdateCanvases();
        var canvas = popup.GetComponent<Canvas>();
        var display = PixelViewport.CalculateDisplayRect(Screen.width, Screen.height);
        Require(canvas != null && !canvas.pixelPerfect && canvas.worldCamera == PixelArtUpscaleRig.RenderCamera &&
            canvas.worldCamera.targetTexture.width == display.width &&
            canvas.worldCamera.targetTexture.height == display.height,
            "dynamically opened shop renders at the viewport resolution");
        var labels = popup.GetComponentsInChildren<TextMeshProUGUI>().Where(label => !string.IsNullOrWhiteSpace(label.text)).ToArray();
        Require(labels.Length > 3, "shop renders generated item names, prices and buttons");
        foreach (var label in labels) PresentationRegressionCheck.VerifyPixelLabel(label);
        var descriptions = popup.GetComponentsInChildren<ScrollRect>();
        Require(descriptions.Length > 0 && descriptions.All(scroll => scroll.content != null &&
            scroll.viewport.GetComponent<RectMask2D>() != null), "shop descriptions stay in their masked card area");
        PresentationRegressionCheck.SaveFrame(canvas.worldCamera.targetTexture, "ShopTypography");
        var longDescription = descriptions[0];
        longDescription.content.GetComponent<TextMeshProUGUI>().text = string.Join("\n", Enumerable.Repeat("상품 설명", 15));
        Canvas.ForceUpdateCanvases();
        yield return null;
        longDescription.verticalNormalizedPosition = 1f;
        longDescription.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down * 4f });
        Require(longDescription.verticalNormalizedPosition < .99f, "long shop descriptions respond to the mouse wheel");
        popup.Close();
        Require(!ShopPopupUI.IsOpen, "shop closes after interaction");
        Debug.Log("[DungeonLayoutCheck] Shop stock and popup passed.");
    }

    private static IEnumerator VerifyRebuild(MapGenerator map, int seed)
    {
        string signature = LayoutSignature(map);
        var previousRooms = map.AllRooms.ToArray();
        var previousMarkers = GetHudMap().Cast<RectTransform>()
            .Where(child => child.name.StartsWith("RoomUI_", StringComparison.Ordinal)).ToArray();
        var ground = (Tilemap)typeof(MapGenerator).GetField("globalGroundTilemap", Private).GetValue(map);
        var spareCell = ground.cellBounds.max + new Vector3Int(16, 16, 0);
        spareCell.z = 0;
        var sample = ground.GetUsedTilesCount();
        var tiles = new TileBase[sample];
        ground.GetUsedTilesNonAlloc(tiles);
        ground.SetTile(spareCell, tiles.First(tile => tile != null));
        map.SetNextLayoutSeed(seed);
        map.StartCoroutine(map.GenerateMapCoroutine());
        while (!map.IsMapGenerationCompleted) yield return null;
        yield return null;

        Require(previousRooms.All(room => room == null) && previousMarkers.All(marker => marker == null) &&
            !ground.HasTile(spareCell), "same-scene rebuild removes previous rooms, minimap markers and tiles");
        Require(LayoutSignature(map) == signature, "same-scene rebuild reproduces the requested layout");
        VerifyScene(map);
        Debug.Log("[DungeonLayoutCheck] Same-scene cleanup and regeneration passed.");
    }

    private static RectTransform GetHudMap()
        => (RectTransform)typeof(UIBasedMiniMap).GetField("hudMapContainer", Private).GetValue(UIBasedMiniMap.Instance);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[DungeonLayoutCheck] " + message);
    }
}

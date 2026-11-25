using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Tilemaps;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class WorkshopPlayModeCheck
{
    private const string Request = "Verification.WorkshopPlayMode";
    private const string StartPath = "Assets/Scenes/StartScene.unity";
    private const string WorkScene = "Assets/Scenes/OptionScene.unity";
    private static readonly Stack<IEnumerator> checks = new();
    private static double deadline;
    private static Sprite expectedTerrain;
    private static WorkshopTerrainTile playedBrush;
    private static readonly Dictionary<Vector3Int, (TileBase tile, Sprite sprite, TileBase floor)> paintedBanks = new();
    private static Vector3Int bankOrigin;

    private static bool BanksMatch(Tilemap walls, Tilemap ground, Vector3Int origin)
    {
        foreach (var pair in paintedBanks)
        {
            var at = origin + pair.Key;
            var expected = pair.Value;
            if (walls.GetTile(at) != expected.tile || walls.GetSprite(at) != expected.sprite ||
                walls.GetColliderType(at) != Tile.ColliderType.Grid || ground.GetTile(at) != expected.floor)
                return false;
        }
        return paintedBanks.Count > 0;
    }

    static WorkshopPlayModeCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Request + ".Stopping", false))
            {
                SessionState.EraseBool(Request + ".Stopping");
                bool restored = SceneManager.GetActiveScene().path == WorkScene &&
                    AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == StartPath;
                Debug.Log(restored ? "[WorkshopPlayModeCheck] PASS" : "[WorkshopPlayModeCheck] Edit scene restore failed.");
                EditorApplication.Exit(restored ? 0 : 1);
                return;
            }
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Request, false)) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            checks.Clear();
            checks.Push(Verify());
            EditorApplication.update += Tick;
        };
    }

    public static void Run()
    {
        if (!Application.isBatchMode || Application.companyName != "NecromanderVerification")
            throw new InvalidOperationException("Run through the isolated WorkshopPlayMode verification suite.");
        EditorSceneManager.OpenScene(WorkScene);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartPath);
        SessionState.SetBool(Request, true);
        WorkshopBuild.OpenInPlayMode();
    }

    private static Button Button(string caption) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
        .Single(b => b.gameObject.activeInHierarchy && b.name == caption);

    private static bool Ready(string scene) => SceneManager.GetActiveScene().name == scene && !UnityNote.SceneLoader.IsLoading;

    private static IEnumerator Verify()
    {
        while (!Ready(WorkshopTrial.SceneName) || Object.FindFirstObjectByType<RoomWorkshopWindow>() == null) yield return null;
        if (AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) != StartPath || WorkshopBuild.CanOpenInPlayMode())
            throw new InvalidOperationException("Temporary start scene or duplicate workshop entry.");
        yield return Frames();
        var desk = GameObject.Find("Room actions").GetComponent<RectTransform>();
        Require(GameObject.Find("Saved rooms") && GameObject.Find("Content tools"), "room creation, library and game content have separate panels");
        PresentationRegressionCheck.SaveFrame(PixelArtUpscaleRig.RenderCamera.targetTexture,"WorkshopUx/desk");
        if (UiResolutionCheck.Enabled)
            yield return UiResolutionCheck.Verify("Workshop desk",desk,Button("새 방 만들기"));
        var search=GameObject.Find("방 이름 검색").GetComponent<TMP_InputField>();
        search.text="__missing_workshop_room__";
        Button("검색").onClick.Invoke();
        Require(Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t=>t.text=="검색한 이름의 방이 없습니다."), "empty search explains how to recover");
        Button("검색 해제").onClick.Invoke();
        Button("UI·보상 번역").onClick.Invoke();
        Button("UI Text Table · ko").onClick.Invoke();
        Button("새 번역").onClick.Invoke();
        var input = GameObject.Find("새 번역 본문").GetComponent<TMP_InputField>();
        input.text = "서리 정령이 깨어납니다.";
        input.selectionStringAnchorPosition = 0;
        input.selectionStringFocusPosition = 2;
        Button("민트색").onClick.Invoke();
        Button("물결").onClick.Invoke();
        Require(input.text == "<color=#AFDFC9><wave>서리</wave></color> 정령이 깨어납니다.", "decorations wrap only the selected Korean word");
        input.selectionStringFocusPosition = input.selectionStringAnchorPosition;
        Button("타이핑").onClick.Invoke();
        Button("다시 재생").onClick.Invoke();
        float startPreview = Time.unscaledTime;
        for (int frame = 0; frame < 24; frame++)
        {
            while (Time.unscaledTime - startPreview < frame / 16f) yield return null;
            yield return null;
            PresentationRegressionCheck.SaveFrame(PixelArtUpscaleRig.RenderCamera.targetTexture,
                $"WorkshopUx/TextPlayback/frame-{frame:D2}");
        }
        var sample = GameObject.Find("문구 미리보기").GetComponentInChildren<TMPTextEffectPlayer>().GetComponent<TMP_Text>();
        Require(sample.GetParsedText() == "서리 정령이 깨어납니다." && input.text.StartsWith("<type>"), "preview renders the raw draft without modifying it");
        if (UiResolutionCheck.Enabled)
        {
            var sizes = UiResolutionCheck.Verify("Workshop",GameObject.Find("문구 미리보기").GetComponent<RectTransform>(),Button("다시 재생"));
            while (sizes.MoveNext()) yield return sizes.Current;
        }
        PresentationRegressionCheck.SaveFrame(PixelArtUpscaleRig.RenderCamera.targetTexture, "WorkshopUx/text-preview");
        Button("내 작업").onClick.Invoke();
        Button("새 방 만들기").onClick.Invoke();
        yield return VerifyMapTools();
        Button("저장").onClick.Invoke();
        Button("플레이").onClick.Invoke();
        while (!Ready("BattleScene") || GameManager.Instance == null || !GameManager.Instance.IsPlayerReady) yield return null;
        if (!WorkshopBuild.CanOpenInPlayMode()) throw new InvalidOperationException("Workshop unavailable from a running battle.");
        while (MapGenerator.Instance==null || !MapGenerator.Instance.IsMapGenerationCompleted) yield return null;
        var worldMap=new SerializedObject(MapGenerator.Instance).FindProperty("globalGroundTilemap").objectReferenceValue as Tilemap;
        bool present=false;
        if(worldMap!=null && worldMap.ContainsTile(playedBrush))
            foreach(var at in worldMap.cellBounds.allPositionsWithin)
                if(worldMap.GetTile(at)==playedBrush && worldMap.GetSprite(at)==expectedTerrain){present=true;break;}
        Require(present,"saved terrain appears on the actual dungeon tilemap with its authored edge");
        Debug.Log("[Workshop map] PASS saved brush in BattleScene global terrain");
        var walls = (Tilemap)new SerializedObject(MapGenerator.Instance).FindProperty("globalWallTilemap").objectReferenceValue;
        bool foundBanks = false;
        foreach (var at in walls.cellBounds.allPositionsWithin)
            if (walls.GetTile(at) == paintedBanks[Vector3Int.zero].tile && BanksMatch(walls, worldMap, at))
            { foundBanks = true; break; }
        Require(foundBanks, "BattleScene wall, cliff and shore retain their layout, edge sprites, collision and underlying floor");
        Debug.Log("[Workshop map] PASS edge sprites and collision on all " + paintedBanks.Count + " painted obstacle cells in BattleScene");
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var live = new SerializedObject(SunyuniePanel.PlayerControlsTarget(player, false).GetComponent<PlayerController>());
        var defaults = new SerializedObject(SunyuniePanel.PlayerControlsTarget(player, true).GetComponent<PlayerController>());
        float authored = defaults.FindProperty("clearedRoomSprint").floatValue;
        live.FindProperty("clearedRoomSprint").floatValue = authored + .25f;
        live.ApplyModifiedProperties();
        defaults.Update();
        if (live.FindProperty("clearedRoomSprint").floatValue != authored + .25f || defaults.FindProperty("clearedRoomSprint").floatValue != authored)
            throw new InvalidOperationException("Live player tuning did not stay on the instance.");
        var lighting = LightingDebugPanel.Instance;
        lighting.SetOpen(true);
        var angle = Object.FindObjectsByType<Slider>(FindObjectsSortMode.None).Single(s => s.name == "태양 방향");
        angle.value = (angle.value + 45) % 360;
        if (LightingPreview.Active == null || LightingPreview.Active.azimuth != angle.value)
            throw new InvalidOperationException("Live sun direction failed.");
        lighting.SetOpen(false);
        WorkshopBuild.OpenInPlayMode();
        while (!Ready(WorkshopTrial.SceneName)) yield return null;
        Button("저장"); // The room editor, including its draft, survived the trial.
        Button("게임 설정").onClick.Invoke();
        Button("던전").onClick.Invoke();
        int Seed() => int.Parse(GameObject.Find("배치 시드/값").GetComponent<TMP_Text>().text);
        int seed = Seed();
        GameObject.Find("배치 시드/+").GetComponent<Button>().onClick.Invoke();
        if (Seed() != seed + 1) throw new InvalidOperationException("Seed stepper failed.");
        Button("설정 저장").onClick.Invoke();
        string file = Path.Combine(Path.GetDirectoryName(WorkshopFiles.Folder), "floor.json");
        if (JsonUtility.FromJson<SavedSeed>(File.ReadAllText(file)).seed != seed + 1)
            throw new InvalidOperationException("Workshop settings not saved.");
        Button("메뉴").onClick.Invoke();
        while (!Ready("StartScene")) yield return null;
        Button("레벨 제작실").onClick.Invoke();
        while (!Ready(WorkshopTrial.SceneName)) yield return null;
        Button("새 방 만들기");
    }

    private static IEnumerator Frames(int count = 2)
    {
        int end = Time.frameCount + count;
        while (Time.frameCount < end) yield return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[Workshop map] " + message);
    }

    private static IEnumerator VerifyMapTools()
    {
        yield return Frames();
        var previousSettings=InputSystem.settings;
        var settings=Object.Instantiate(previousSettings);
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings=settings;
        var mouse = InputSystem.AddDevice<Mouse>();
        var keyboard=InputSystem.AddDevice<Keyboard>();
        try
        {
            Camera Board() => GameObject.Find("Workshop camera").GetComponent<Camera>();
            Tilemap Ground() => Board().transform.parent.GetComponentsInChildren<Tilemap>().First(t => t.name=="Ground");
            Vector2 Display(Vector3 world)
            {
                var map = (RectTransform)GameObject.Find("Map").transform;
                Vector2 uv = Board().WorldToViewportPoint(world);
                var point = map.TransformPoint(Rect.NormalizedToPoint(map.rect, uv));
                var logical = PixelViewport.CanvasToGamePoint(map.GetComponentInParent<Canvas>(), point);
                return PixelViewport.GameToScreenPoint(logical, PixelViewport.CalculateDisplayRect(Screen.width,Screen.height));
            }
            IEnumerator Send(MouseState state)
            {
                InputSystem.QueueStateEvent(mouse,state);
                yield return Frames();
            }
            IEnumerator ClickMap(Vector3 world)
            {
                var state=new MouseState{position=Display(world)};
                yield return Send(state);
                yield return Send(state.WithButton(MouseButton.Left));
                yield return Send(state.WithButton(MouseButton.Left,false));
            }
            void Use(string name) => Button(name).onClick.Invoke();
            var catalog = Resources.Load<WorkshopCatalog>("WorkshopCatalog");
            var brushes = catalog.tiles.Where(e=>e.tile is WorkshopTerrainTile).ToArray();
            Require(brushes.Length>=5 && brushes.All(e=>((WorkshopTerrainTile)e.tile).PaletteSprite!=null), "imported terrain palettes");
            VerifyBrushMasks(brushes);
            var ground=Ground();
            var center=ground.WorldToCell(Board().ViewportToWorldPoint(new Vector3(.47f,.52f,30)));
            center.z=0;
            var adjacent=center+Vector3Int.right;
            Use("자동 타일");
            void ChooseBrush(string title)
            {
                var current = brushes.Select(e => e.Title).First(n => GameObject.Find(n) != null);
                Use(current);
                Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == title &&
                    b.GetComponent<RectTransform>().rect.height == 43).onClick.Invoke();
            }
            ChooseBrush("흙길");
            yield return ClickMap(ground.GetCellCenterWorld(center));
            Require(ground.GetTile(center) is WorkshopTerrainTile, "mouse paints a terrain cell");
            var isolated=ground.GetSprite(center);
            yield return ClickMap(ground.GetCellCenterWorld(adjacent));
            var connected=ground.GetSprite(center);
            Require(connected!=isolated && connected!=null, "neighbor updates after painting");
            Use("지우개");
            yield return ClickMap(ground.GetCellCenterWorld(adjacent));
            Require(ground.GetSprite(center)==isolated, "neighbor updates after erasing");
            Use("실행 취소");
            yield return Frames();
            ground=Ground();
            Require(ground.GetSprite(center)==connected, "undo restores connectivity");
            Use("다시 실행"); yield return Frames();
            Require(Ground().GetSprite(center)==isolated, "redo restores edge");
            ground=Ground();
            Use("선택");
            var world=ground.GetCellCenterWorld(center);
            Vector2 screen=Display(world);
            yield return Frames();
            var baseline=ReadCell(Board(),world,1);
            for (int zoom=2;zoom<=8;zoom++)
            {
                yield return Send(new MouseState{position=screen,scroll=new Vector2(0,120)});
                yield return Send(new MouseState{position=screen});
                float cellWidth=Vector3.Distance(ground.CellToWorld(Vector3Int.zero),ground.CellToWorld(Vector3Int.right));
                Require(Mathf.Abs(Board().orthographicSize-272f*cellWidth/(32*zoom))<.001f,"integer zoom "+zoom);
                Require((Display(world)-screen).magnitude<4f,"cursor anchor at zoom "+zoom);
                var actual=ReadCell(Board(),world,zoom);
                Require(actual.SequenceEqual(baseline),"same 16×16 source pixels at zoom "+zoom);
            }
            for (int zoom=7;zoom>=1;zoom--)
            {
                yield return Send(new MouseState{position=screen,scroll=new Vector2(0,-120)});
                yield return Send(new MouseState{position=screen});
                Require(ReadCell(Board(),world,zoom).SequenceEqual(baseline),"zoom-out matches source at "+zoom);
            }
            var drag=new MouseState{position=screen};
            yield return Send(drag.WithButton(MouseButton.Middle));
            var pan=new MouseState{position=screen+Vector2.right*24};
            yield return Send(pan.WithButton(MouseButton.Middle)); yield return Send(pan.WithButton(MouseButton.Middle,false));
            Require(Vector2.Distance(Display(world),pan.position)<2 && ReadCell(Board(),world,1).SequenceEqual(baseline),
                "middle-button pan tracks pointer without changing tile art");
            Use("원위치");
            Tilemap Walls() => Board().transform.parent.GetComponentsInChildren<Tilemap>().First(t => t.name=="Wall");
            var footprint = new[] {Vector3Int.zero, Vector3Int.right, Vector3Int.right*2, Vector3Int.up,
                Vector3Int.right+Vector3Int.up, Vector3Int.up*2};
            var titles = new[] {"벽","절벽","물가"};
            bankOrigin = center + new Vector3Int(-5,1,0);
            paintedBanks.Clear();
            Use("자동 타일");
            for (int i=0;i<titles.Length;i++)
            {
                ChooseBrush(titles[i]);
                var brush = (WorkshopTerrainTile)brushes.Single(e=>e.Title==titles[i]).tile;
                foreach (var delta in footprint)
                {
                    var offset=delta + Vector3Int.right*(i*3);
                    var at=bankOrigin+offset;
                    var floor=Ground().GetTile(at);
                    yield return ClickMap(Ground().GetCellCenterWorld(at));
                    Require(Walls().GetTile(at)==brush && Ground().GetTile(at)==floor,"painting "+titles[i]+" preserves the floor");
                    paintedBanks.Add(offset,(brush,null,floor));
                }
            }
            foreach(var offset in paintedBanks.Keys.ToArray())
            {
                var entry=paintedBanks[offset];
                paintedBanks[offset]=(entry.tile,Walls().GetSprite(bankOrigin+offset),entry.floor);
            }
            for(int i=0;i<titles.Length;i++)
            {
                var baseCell=Vector3Int.right*(3*i);
                Require(paintedBanks[baseCell].sprite.name.EndsWith("-19") &&
                    paintedBanks[baseCell+Vector3Int.right+Vector3Int.up].sprite.name.EndsWith("-76"),
                    titles[i]+" connects its own sides and separates different materials");
            }
            Use("지우개");
            var removed=bankOrigin+Vector3Int.right*6+Vector3Int.up+Vector3Int.right;
            yield return ClickMap(Ground().GetCellCenterWorld(removed));
            Require(!Walls().HasTile(removed) && Walls().GetSprite(bankOrigin+Vector3Int.right*6).name.EndsWith("-3"),
                "removing shore exposes an inner corner");
            Use("실행 취소"); yield return Frames();
            Require(BanksMatch(Walls(),Ground(),bankOrigin),"undo restores all boundary sprites");
            Use("선택");
            yield return Send(new MouseState{position=Vector2.zero});
            PresentationRegressionCheck.SaveFrame(Board().targetTexture,"WorkshopUx/terrain-banks");
            var roomsFolder=Path.Combine(ProjectContentStore.Root,ProjectRooms.DirectoryName);
            var earlier=Directory.Exists(roomsFolder) ? Directory.GetDirectories(roomsFolder) : Array.Empty<string>();
            string title="흙길과 울타리";
            GameObject.Find("방 이름").GetComponent<TMP_InputField>().onEndEdit.Invoke(title);
            Use("저장");
            string id = Directory.GetDirectories(roomsFolder).Except(earlier)
                .Where(path=>File.Exists(Path.Combine(path,"room.json")) && File.ReadAllText(Path.Combine(path,"room.json")).Contains(title))
                .OrderByDescending(Directory.GetLastWriteTimeUtc).Select(Path.GetFileName).First();
            var nameInput=GameObject.Find("방 이름").GetComponent<TMP_InputField>();
            nameInput.ActivateInputField(); yield return Frames();
            Require(nameInput.isFocused,"room name has keyboard focus");
            string renamed=title+" 2";
            nameInput.text=renamed;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftCtrl,Key.LeftShift,Key.S));
            yield return Frames();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return Frames();
            Require(File.ReadAllText(Path.Combine(roomsFolder,id,"room.json")).Contains(renamed),
                "save all commits the focused room name to DATA on the first keystroke");
            Use("내 작업");
            GameObject.Find("방 이름 검색").GetComponent<TMP_InputField>().text=renamed;
            Use("검색"); Use("프로젝트 편집 "+id); yield return Frames();
            Require(GameObject.Find("방 이름").GetComponent<TMP_InputField>().text==renamed,"search opens the named room");
            PresentationRegressionCheck.SaveFrame(PixelArtUpscaleRig.RenderCamera.targetTexture,"WorkshopUx/room-tools");
            Require(Ground().GetSprite(center)==isolated,"DATA reopen preserves terrain connectivity");
            Require(BanksMatch(Walls(),Ground(),bankOrigin),"DATA reopen preserves obstacle layers and their boundaries");
            expectedTerrain=isolated; playedBrush=(WorkshopTerrainTile)Ground().GetTile(center);
            Use("전체 보기"); yield return Frames();
            var groundBounds=Ground().GetComponent<TilemapRenderer>().bounds;
            var lo=Board().WorldToViewportPoint(groundBounds.min);
            var hi=Board().WorldToViewportPoint(groundBounds.max);
            Require(lo.x>=0 && lo.y>=0 && hi.x<=1 && hi.y<=1,"overview contains the room");
            Use("원위치"); Use("소품");
            Use(catalog.pieces.First(piece=>!piece.enemy).prefab.name);
            var filter=GameObject.Find("재료 검색어").GetComponent<TMP_InputField>();
            filter.SetTextWithoutNotify("Tree_New_1"); Use("검색");
            Use("Tree_New_1");
            yield return ClickMap(world);
            Use("선택"); yield return Frames();
            Transform Art() => Board().transform.parent.GetChild(0);
            var tree=Art().GetChild(Art().childCount-1);
            Require(tree.name=="Tree_New_1","palette search places the chosen tree");
            var picture=tree.GetComponent<SpriteRenderer>();
            var crown=picture.bounds.center + Vector3.up*(picture.bounds.extents.y*.80f);
            var empty=picture.bounds.max+Vector3.one*.22f; empty.z=0;
            yield return ClickMap(empty);
            var original=tree.position;
            var state=new MouseState{position=Display(crown)};
            yield return Send(state);
            yield return Send(state.WithButton(MouseButton.Left));
            var end=new MouseState{position=Display(crown+Vector3.right*.5f)};
            yield return Send(end.WithButton(MouseButton.Left)); yield return Send(end.WithButton(MouseButton.Left,false));
            Require(Mathf.Abs(tree.position.x-original.x-.5f)<.04f,"dragging the crown moves the selected tree");
            var outlines=Board().transform.parent.GetComponentsInChildren<SpriteRenderer>()
                .Where(r=>r.name=="Selection outline").ToArray();
            Require(outlines.Length==4,"one outline for the selected tree");
            var union=outlines[0].bounds;
            foreach(var edge in outlines) union.Encapsulate(edge.bounds);
            var bounds=picture.bounds;
            Require(Vector2.Distance(union.center,bounds.center)<.02f && union.min.x<bounds.min.x &&
                union.min.y<bounds.min.y && union.max.x>bounds.max.x && union.max.y>bounds.max.y &&
                union.size.x-bounds.size.x<.2f,"visible outline hugs all four sprite edges after drag");
            PresentationRegressionCheck.SaveFrame(PixelArtUpscaleRig.RenderCamera.targetTexture,"WorkshopUx/selected-tree");
            // A changed draw batch used to sample the tile atlas for trees, barricades and traps.
            var probe=picture.bounds.center;
            var canopyPixels=ReadCell(Board(),probe,1);
            var maps=Art().GetComponentsInChildren<TilemapRenderer>().Where(r=>r.enabled).ToArray();
            try
            {
                foreach(var map in maps) map.enabled=false;
                yield return Frames();
                var bare=ReadCell(Board(),probe,1);
                Require(canopyPixels.SequenceEqual(bare),"opaque canopy uses the same sprite atlas when tile chunks leave the batch");
            }
            finally { foreach(var map in maps) map.enabled=true; }
            Use("비교"); yield return Frames();
            var comparison=Object.FindObjectsByType<RawImage>(FindObjectsSortMode.None)
                .Where(image=>image.name is "편집 기준 방 render" or "편집 중인 방 render").ToArray();
            Require(comparison.Length==2 && comparison.All(image=>image.texture.width==310 && image.texture.height==176),
                "both comparison panes render at their own native resolution");
            Use("편집으로"); yield return Frames();
            Require(Board().targetTexture.width==478 && Board().targetTexture.height==272,"editor resolution restored");
            // Two identical props at one point must both be individually reachable.
            Vector3 stackAt=tree.position;
            Use("소품"); yield return ClickMap(stackAt); Use("선택"); yield return Frames();
            var top=Art().GetChild(Art().childCount-1);
            var other=Art().GetChild(Art().childCount-2);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftAlt)); yield return Frames();
            var stackPoint=Display(stackAt + Vector3.up*.5f);
            var press=new MouseState{position=stackPoint}.WithButton(MouseButton.Left);
            yield return Send(press);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return Frames();
            var move=new MouseState{position=stackPoint+Vector2.right*16};
            yield return Send(move.WithButton(MouseButton.Left)); yield return Send(move.WithButton(MouseButton.Left,false));
            Require(Vector2.Distance(top.position,stackAt)<.04f && Vector2.Distance(other.position,stackAt)>.2f,
                "Alt picks the other overlapping prop");
            int count=Art().childCount;
            Use("선택 삭제"); yield return Frames();
            Require(Art().childCount==count-1,"delete removes the selected prop");
            Use("실행 취소"); yield return Frames();
            Require(Art().childCount==count,"undo restores removed selection");
            Debug.Log("[Workshop map] PASS material search, overview, canopy pick and drag, atlas binding, comparison, Alt overlap and undo");
            Debug.Log("[Workshop map] PASS native pixels at zoom 1–8, pointer anchoring, paint, erase, undo, redo, DATA reopen");
        }
        finally { InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse); InputSystem.settings=previousSettings; Object.DestroyImmediate(settings); }
    }

    private static void VerifyBrushMasks(WorkshopCatalog.TileEntry[] brushes)
    {
        var root=new GameObject("Terrain rule check",typeof(Grid));
        var node=new GameObject("Tiles",typeof(Tilemap));
        node.transform.SetParent(root.transform,false);
        try
        {
            var map=node.GetComponent<Tilemap>();
            var offsets=new[]{Vector3Int.up,Vector3Int.right,Vector3Int.down,Vector3Int.left,
                new Vector3Int(1,1),new Vector3Int(1,-1),new Vector3Int(-1,-1),new Vector3Int(-1,1)};
            foreach (var brush in brushes)
            {
                var shapes=new HashSet<Sprite>();
                map.SetTile(Vector3Int.zero,brush.tile);
                for(int mask=0;mask<256;mask++)
                {
                    for(int i=0;i<offsets.Length;i++)
                        map.SetTile(offsets[i],(mask & (1<<i))!=0 ? brush.tile : null);
                    var sprite=map.GetSprite(Vector3Int.zero);
                    Require(sprite!=null,"rule resolves to visible art");
                    shapes.Add(sprite);
                }
                Require(shapes.Count==47,"47 edge and corner shapes for "+brush.Title);
                map.ClearAllTiles();
            }
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static Color32[] ReadCell(Camera camera, Vector3 center, int zoom)
    {
        var frame=camera.targetTexture;
        Vector2 mid=camera.WorldToScreenPoint(center);
        var image=new Texture2D(16*zoom,16*zoom,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;
        try
        {
            RenderTexture.active=frame;
            image.ReadPixels(new Rect(Mathf.Round(mid.x)-8*zoom, Mathf.Round(mid.y)-8*zoom,16*zoom,16*zoom),0,0);
            var all=image.GetPixels32();
            var samples=new Color32[256];
            for(int y=0;y<16;y++) for(int x=0;x<16;x++)
                samples[y*16+x]=all[(y*zoom+zoom/2)*image.width+x*zoom+zoom/2];
            return samples;
        }
        finally {RenderTexture.active=previous; Object.DestroyImmediate(image);}
    }

    [Serializable] private class SavedSeed { public int seed = 0; }

    private static void Tick()
    {
        int exit = 0;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Workshop Play Mode check timed out.");
            while (checks.Count > 0)
            {
                var current = checks.Peek();
                if (!current.MoveNext()) { checks.Pop(); continue; }
                if (current.Current is IEnumerator nested) { checks.Push(nested); continue; }
                return;
            }
        }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        EditorApplication.update -= Tick;
        SessionState.EraseBool(Request);
        if (exit != 0) EditorApplication.Exit(exit);
        else
        {
            SessionState.SetBool(Request + ".Stopping", true);
            EditorApplication.ExitPlaymode();
        }
    }
}

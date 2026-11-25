#if LEVEL_WORKSHOP_QA
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed partial class WorkshopPlayerCheck : MonoBehaviour
{
    private string output, step;
    private Mouse mouse;
    private float deadline;
    private bool finished;
    private readonly List<string> passed = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartCheck()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-workshopReport");
        if (index < 0 || index + 1 >= args.Length) return;
        var go = new GameObject("Workshop player check");
        DontDestroyOnLoad(go);
        go.AddComponent<WorkshopPlayerCheck>().output = args[index + 1];
    }

    private IEnumerator Start()
    {
        Directory.CreateDirectory(output);
        var profiles=Path.Combine(Application.temporaryCachePath,"WorkshopSharing",Guid.NewGuid().ToString("N"));
        WorkshopWorkspace.UseTestHome(Path.Combine(profiles,"A"));
        Application.logMessageReceived+=CheckException;
        Debug.unityLogger.filterLogType=LogType.Log;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        mouse = InputSystem.AddDevice<Mouse>();
        QualitySettings.vSyncCount=0;
        var requested=new Vector2Int(Screen.width,Screen.height);
        // Resize once so the hidden verification window has a live swap chain before sampling.
        var warm=requested.x==640 ? new Vector2Int(960,540) : new Vector2Int(640,360);
        Screen.SetResolution(warm.x,warm.y,FullScreenMode.Windowed);
        for(int i=0;i<30;i++) yield return null;
        Screen.SetResolution(requested.x,requested.y,FullScreenMode.Windowed);
        for(int i=0;i<30;i++) yield return null;
        Require(Screen.width==requested.x && Screen.height==requested.y,$"window {Screen.width} x {Screen.height}");
        string liveSave=Path.Combine(Application.persistentDataPath,"save_data.json");
        string SaveContents()=>File.Exists(liveSave) ? File.ReadAllText(liveSave) : null;
        string initialSave=SaveContents(), initialSkill=RightClickUnlockState.SelectedName;
        bool initialTutorial=TutorialFlow.Completed;
        Step("title");
        yield return Wait(()=>!UnityNote.SceneLoader.IsLoading);
        string[] args=Environment.GetCommandLineArgs();
        if (args.Contains("-workshopSoak"))
        {
            yield return CheckRepeatedTrials();
            yield break;
        }
        int receiver=Array.IndexOf(args,"-workshopRecipient");
        if (receiver>=0 && receiver+2<args.Length)
        {
            WorkshopWorkspace.UseTestHome(args[receiver+2]);
            yield return CheckFreshRecipient(args[receiver+1],args.Contains("-workshopReopen"));
            yield break;
        }
        yield return Capture("title");
        yield return Click("레벨 제작실");
        yield return Click("Legacy");
        yield return Capture("legacy");
        Require(WorkshopTrial.Library.rooms.Length == 32, "32 base rooms");
        yield return Click("내 작업");
        yield return Click("새 방 만들기");
        yield return Wait(() => WorkshopTrial.Draft != null && GameObject.Find("Map") != null);
        var draft = WorkshopTrial.Draft;
        Require(WorkshopTrial.Library.FindRoom(draft.template).title == "Room_Normal 3", "created room from reference template");
        yield return Capture("workbench");
        var camera = GameObject.Find("Workshop camera").GetComponent<Camera>();
        var ground = camera.transform.parent.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>().First(m => m.name == "Ground");
        Vector2 point = default;
        bool found = false;
        foreach (var cell in ground.cellBounds.allPositionsWithin)
        {
            if (!ground.HasTile(cell)) continue;
            var here=ground.GetCellCenterWorld(cell);
            if(camera.transform.parent.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true)
                .Any(m=>(m.name=="Wall" || m.name=="Unsteppable") && m.HasTile(m.WorldToCell(here)))) continue;
            var view = camera.WorldToViewportPoint(ground.GetCellCenterWorld(cell));
            if (view.x < .3f || view.y < .3f || view.x > .7f || view.y > .7f) continue;
            point = new Vector2(7 + view.x * WorkshopPreview.Width, 360 - 54 - (1 - view.y) * WorkshopPreview.Height);
            found = true; break;
        }
        Require(found, "visible ground cell");
        yield return CheckGroupTools(point);
        yield return Click("지우개");
        yield return Press(point);
        Require(WorkshopTrial.Draft.cells.Count == 1, "erased one cell");
        yield return Click("실행 취소");
        Require(WorkshopTrial.Draft.cells.Count == 0, "undo stroke");
        yield return Click("다시 실행");
        Require(WorkshopTrial.Draft.cells.Count == 1, "redo stroke");
        yield return Click("실행 취소");
        yield return Click("적");
        yield return Press(point);
        Require(WorkshopTrial.Draft.pieces.Count == 1, "placed encounter enemy");
        yield return Click("출구");
        var anchor=WorkshopTrial.Library.DefaultRoom.room.GetComponentsInChildren<RoomAnchor>().First(a=>a.direction==Vector2Int.right);
        var source=WorkshopTrial.Library.DefaultRoom.room.transform;
        var door=source.InverseTransformPoint(anchor.transform.position)+new Vector3(0,.125f);
        var board=GameObject.Find("Workshop camera").GetComponent<Camera>();
        var art=board.transform.parent.GetChild(0);
        var doorView=board.WorldToViewportPoint(art.TransformPoint(door));
        if(doorView.x>0 && doorView.x<1 && doorView.y>0 && doorView.y<1)
        {
            yield return Press(new Vector2(7+doorView.x*WorkshopPreview.Width,34+doorView.y*WorkshopPreview.Height));
            Require(WorkshopTrial.Draft.exits.Count==1,"moved east door");
        }
        yield return Click("전투 설정");
        yield return Click("웨이브 추가");
        yield return Click("웨이브 추가");
        yield return Click("다음 군집");
        string second=WorkshopTrial.Draft.waves[1];
        yield return Click("앞으로");
        Require(WorkshopTrial.Draft.waves[0]==second && WorkshopTrial.Draft.waves.Count==2,"reordered authored clusters");
        yield return Click("아이템");
        yield return Capture("encounter");
        yield return Click("닫기");
        yield return Click("저장");
        var file = WorkshopFiles.FileFor(draft.id);
        Require(File.Exists(file) && WorkshopFiles.Read(file,WorkshopTrial.Library).pieces.Count == 1, "saved document round trip");
        yield return Click("공유본");
        Require(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(WorkshopFiles.Folder),"Shared"), draft.id+"-*.json").Length > 0, "shared copy");
        yield return Click("공유");
        yield return Click("가져오기 "+draft.id);
        Require(WorkshopTrial.Draft.id!=draft.id && WorkshopTrial.Draft.pieces.Count==1,"imported team file as independent draft");
        yield return Click("내 작업");
        yield return Click("편집 "+draft.id);
        Require(WorkshopTrial.Draft.id==draft.id && WorkshopTrial.Draft.pieces.Count==1,"reopened saved room from library");
        yield return Click("적");
        yield return Press(point+new Vector2(6,0));
        yield return Click("실행 취소");
        yield return Click("다시 실행");
        yield return Click("실행 취소");
        yield return Click("플레이");
        Step("battle ready");
        yield return Wait(() => SceneManager.GetActiveScene().name=="BattleScene" && GameManager.Instance!=null && GameManager.Instance.IsPlayerReady && !UnityNote.SceneLoader.IsLoading);
        var generator = FindFirstObjectByType<MapGenerator>();
        Require(WorkshopTrial.Active && generator.AllRooms[0].name.Contains("Room_Normal 3"), "trial uses edited room in BattleScene");
        Require(generator.AllRooms[0].GetComponentInChildren<NormalRoomEvent>()!=null, "normal encounter attached");
        yield return Capture("battle");
        var room=generator.AllRooms[0];
        var encounter=room.transform.Find("Workshop encounter");
        Require(encounter!=null,"placed enemy group exists in trial");
        Step("fixed enemy activation");
        yield return Wait(()=>encounter.gameObject.activeInHierarchy);
        var enemy=encounter.GetComponentInChildren<CharacterHealth>();
        Require(enemy!=null && enemy.MaxHP>0,"placed enemy initialized");
        enemy.TakeDamage(new DamageInfo(enemy.MaxHP*20+999,DamageType.Physical,GameManager.Instance.PLAYERCONTROLLER.gameObject));
        Step("cluster waves");
        var combat=room.GetComponent<NormalRoomEvent>();
        Require(combat.AuthoredWaves.Count==2 && combat.AuthoredWaves[0]==WorkshopTrial.Library.FindCluster(second).asset,
            "saved wave order in BattleScene");
        Require(room.HasAuthoredReward && room.normalRewardType==RoomInstance.NormalRewardType.Item,"item reward retained after floor distribution");
        var seen=new HashSet<int>();
        while(!room.isCleared)
        {
            foreach(var active in combat.ActiveEnemiesForCheck)
            {
                var hp=active!=null ? active.GetComponentInChildren<CharacterHealth>() : null;
                if(hp==null || hp==enemy || hp.IsDead || hp.CurHP<=0) continue;
                seen.Add(combat.CurrentWaveForCheck);
                hp.TakeDamage(new DamageInfo(hp.MaxHP*20+999,DamageType.Physical,GameManager.Instance.PLAYERCONTROLLER.gameObject));
            }
            yield return null;
        }
        Require(seen.SetEquals(new[]{1,2}),"two successive live enemy waves before room clear");
        Require(room.anchors.Any(a=>a.isUsed && a.GetComponent<DoorController>()!=null),"cleared room has linked exit");
        yield return CheckMapToggle();
        var input = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerInput>();
        if(input!=null && input.user.valid) UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(mouse,input.user);
        yield return Click("편집으로  F6");
        Step("return");
        yield return Wait(() => !WorkshopTrial.Active && GameObject.Find("Workshop camera")!=null && !UnityNote.SceneLoader.IsLoading);
        Require(WorkshopTrial.Draft.id == draft.id && WorkshopTrial.Redo.Count > 0, "restored document and undo history");
        yield return Click("게임 설정");
        yield return Click("튜토리얼");
        yield return Click("방 순서 편집");
        yield return Click("기본 순서");
        yield return Click("순서 적용");
        var library=WorkshopTrial.Library;
        var trialFloor=WorkshopFloor.Load();
        var sequence=trialFloor.TutorialOrder(library);
        int swapped=-1;
        for (int i=1;i<sequence.Count-2;i++)
        {
            var candidate=new List<string>(sequence);
            (candidate[i],candidate[i+1])=(candidate[i+1],candidate[i]);
            try { trialFloor.ApplyTutorialOrder(candidate,library); swapped=i; break; }
            catch (InvalidDataException) { }
        }
        Require(swapped>0,"found connected alternative tutorial route");
        var initialRoute=WorkshopFloor.Load().Preview(library,out _);
        var linkedCell=initialRoute.Rooms[swapped].Cell;
        yield return Click("방 "+linkedCell.x+","+linkedCell.y);
        yield return Click("층에 연결");
        string movedDocument=WorkshopTrial.Draft.id;
        yield return Click("게임 설정");
        yield return Click("방 순서 편집");
        yield return Click((swapped+1)+"   "+library.FindRoom(sequence[swapped]).title);
        yield return Click("뒤로 이동");
        yield return Capture("tutorial-order");
        yield return Click("순서 적용");
        var savedFloor=WorkshopFloor.Load();
        var movedRoute=savedFloor.Preview(library,out _);
        var movedCell=movedRoute.Rooms[swapped+1].Cell;
        Require(movedRoute.Rooms[swapped+1].Prefab==initialRoute.Rooms[swapped].Prefab &&
            savedFloor.At(movedCell,sequence[swapped])?.document==movedDocument,"reordering preserved linked draft at new cell");
        yield return Capture("floor");
        yield return Click("방 0,0");
        yield return Click("지우개");
        yield return Click("층에 연결");
        string attached=WorkshopTrial.Draft.id;
        yield return Click("게임 설정");
        yield return Click("무대 플레이");
        Step("tutorial");
        yield return Wait(()=>GameManager.Instance!=null && GameManager.Instance.IsPlayerReady && !UnityNote.SceneLoader.IsLoading);
        Require(TutorialFlow.IsRunning && GameManager.Instance.mapGenerator.AllRooms.Count==WorkshopTrial.Library.generation.tutorialRooms.Count,"full tutorial path");
        Require(WorkshopTrial.RoomAt(Vector2Int.zero)?.id==attached,"attached room loaded into floor slot");
        Require(WorkshopTrial.RoomAt(movedCell)?.id==movedDocument &&
            GameManager.Instance.mapGenerator.AllRooms[swapped+1].name.Contains(library.FindRoom(sequence[swapped]).room.name),
            "live tutorial follows edited sequence");
        var trialInput=GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerInput>();
        if(trialInput!=null && trialInput.user.valid) UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(mouse,trialInput.user);
        yield return Click("편집으로  F6");
        yield return Wait(()=>!WorkshopTrial.Active && GameObject.Find("무대 플레이")!=null);
        Require(!TutorialFlow.IsRunning,"returned to game settings");
        yield return CheckVillage();
        yield return CheckSharing(profiles,draft.id);
        yield return Click("메뉴");
        yield return Wait(() => SceneManager.GetActiveScene().name == "StartScene" && !UnityNote.SceneLoader.IsLoading);
        Require(SaveContents()==initialSave && RightClickUnlockState.SelectedName==initialSkill &&
            TutorialFlow.Completed==initialTutorial,"live run save, selected skill and tutorial flag unchanged");
        Finish(true);
    }
    private IEnumerator CheckMapToggle()
    {
        Step("map toggle after battle");
        yield return Wait(() => !UIPopUpManager.Instance.IsOnBattle);
        var map = FindFirstObjectByType<MapUIManager>();
        var keys = InputSystem.AddDevice<Keyboard>();
        var player = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerInput>();
        UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(keys, player.user);
        IEnumerator Tab()
        {
            InputSystem.QueueStateEvent(keys, new KeyboardState(Key.Tab));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keys, new KeyboardState());
            yield return null; yield return null;
        }
        try
        {
            map.enabled = false;
            yield return Tab();
            Require(!map.IsMapOpen, "disabled map ignores Tab");
            map.enabled = true;
            yield return null; yield return null;
            yield return Tab();
            Require(map.IsMapOpen, "Tab opens the re-enabled map");
            yield return Tab();
            Require(!map.IsMapOpen, "second Tab closes the map");
        }
        finally { InputSystem.RemoveDevice(keys); }
    }

    private IEnumerator CheckVillage()
    {
        yield return Click("마을");
        yield return Click("무대 플레이");
        yield return Wait(()=>SceneManager.GetActiveScene().name=="VillageScene" &&
            GameManager.Instance!=null && GameManager.Instance.IsPlayerReady && !UnityNote.SceneLoader.IsLoading);
        void PairMouse()
        {
            var input=GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerInput>();
            if (input!=null && input.user.valid) UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(mouse,input.user);
        }
        PairMouse();
        yield return Click("마을 편집");
        yield return Click("조명 조정");
        yield return Click("장면 기본값 복원");
        LightingDebugPanel.Instance.SetOpen(false);
        yield return Click("다음 NPC");
        if (GameObject.Find("표시 꺼짐")!=null) yield return Click("표시 꺼짐");
        var npc=FindObjectsByType<NPCBase>(FindObjectsSortMode.None).Single(n=>n.WorkshopId=="village-guide");
        var before=npc.WorkshopActor.position;
        yield return Click("지도에 놓기");
        // Alternate the point when a previous verification run saved the same NPC.
        var target=new Vector2(350,135);
        var display=PixelViewport.GameToScreenPoint(target,PixelArtUpscaleRig.Output.DisplayRect);
        if (PixelViewport.TryPointerToWorld(display,out var expected) && Vector2.Distance(expected,before)<1)
            target.x+=40;
        yield return Press(target);
        var moved=npc.WorkshopActor.position;
        Require(Vector2.Distance(moved,before)>.02f,"village actor moved on map");
        const string lines="쫑긋! 마을에 온 것을 환영합니다.\n다시 만나요.";
        GameObject.Find("NPC 인사말").GetComponent<TMPro.TMP_InputField>().text=lines;
        var editorText=GameObject.Find("NPC 인사말").GetComponent<TMPro.TMP_InputField>().textComponent;
        editorText.ForceMeshUpdate();
        Require(editorText.font.HasCharacter('긋',false,true) && editorText.font.atlasTexture.filterMode==FilterMode.Point,
            "free-form Korean uses native bitmap glyphs");
        yield return Click("대사 미리보기");
        Require(DialogueUI.Instance.IsPlaying,"authored greeting opens game dialogue");
        var speech=GameObject.Find("BodyText").GetComponent<TMPro.TMP_Text>();
        yield return new WaitForSecondsRealtime(.9f);
        var nameRect=GameObject.Find("NameBox").GetComponent<RectTransform>();
        Vector3[] nameCorners=new Vector3[4], bodyCorners=new Vector3[4];
        nameRect.GetWorldCorners(nameCorners); speech.rectTransform.GetWorldCorners(bodyCorners);
        Require(nameCorners[0].y>=bodyCorners[1].y,"speaker badge clears dialogue text");
        Require(speech.font.HasCharacter('쫑') && speech.textInfo.characterInfo[0].character=='쫑',
            "dialogue renders a Korean glyph outside the shipped atlas");
        yield return Capture("village-dialogue");
        DialogueUI.Instance.StopDialogue();
        yield return Click("마을 편집");
        yield return Click("조명 조정");
        var slider=FindObjectsByType<Slider>(FindObjectsSortMode.None).Single(x=>x.name=="빛 강도");
        var rail=(RectTransform)slider.transform;
        var logical=PixelViewport.CanvasToGamePoint(slider.GetComponentInParent<Canvas>(),
            rail.TransformPoint(new Vector2(rail.rect.xMin+rail.rect.width*.63f,rail.rect.center.y)));
        yield return Press(logical);
        Require(LightingPreview.Active!=null,"village lighting edited live");
        string look=JsonUtility.ToJson(LightingPreview.Active);
        yield return Click("마을 저장");
        yield return Capture("village");
        var saved=WorkshopVillage.Load();
        Require(saved.useLighting && JsonUtility.ToJson(saved.lighting)==look &&
            saved.actors.Any(a=>a.id=="village-guide" && a.greeting==lines && Vector2.Distance(a.position,moved)<.001f),
            "village position, dialogue and lighting persisted");
        LightingDebugPanel.Instance.SetOpen(false);
        yield return Click("편집으로  F6");
        yield return Wait(()=>!WorkshopTrial.Active && GameObject.Find("무대 플레이")!=null);
        yield return Click("무대 플레이");
        yield return Wait(()=>SceneManager.GetActiveScene().name=="VillageScene" && GameManager.Instance!=null &&
            GameManager.Instance.IsPlayerReady && !UnityNote.SceneLoader.IsLoading);
        npc=FindObjectsByType<NPCBase>(FindObjectsSortMode.None).Single(n=>n.WorkshopId=="village-guide");
        Require(Vector3.Distance(npc.WorkshopActor.position,moved)<.001f && npc.WorkshopGreeting==lines &&
            JsonUtility.ToJson(LightingPreview.Active)==look,"fresh village trial restored all authored values");
        Require(npc.InteractWithGreeting(GameManager.Instance.PLAYERCONTROLLER.gameObject) && DialogueUI.Instance.IsPlaying,
            "edited NPC greets through interaction");
        DialogueUI.Instance.StopDialogue();
        PairMouse();
        yield return Click("편집으로  F6");
        yield return Wait(()=>!WorkshopTrial.Active && GameObject.Find("무대 플레이")!=null);
    }
    private IEnumerator CheckGroupTools(Vector2 point)
    {
        yield return Click("도장");
        yield return Drag(point,point+Vector2.right*8);
        yield return Click("보관함");
        var earlier=Directory.GetFiles(WorkshopStamp.Folder,"*.json");
        yield return Click("새 도장 저장");
        string stored=Directory.GetFiles(WorkshopStamp.Folder,"*.json").Except(earlier).Single();
        var stamp=WorkshopStamp.Read(stored,WorkshopTrial.Library);
        yield return Click("닫기");
        yield return Click("다시 고르기");
        yield return Click("보관함");
        yield return Click(stamp.title+" · "+stamp.cells.Count+"칸");
        yield return Press(point+Vector2.up*16);
        Require(WorkshopTrial.Draft.cells.Count==stamp.cells.Count,"reused a saved stamp from the shelf");
        Require(WorkshopTrial.Draft.cells.Count>=1 && WorkshopTrial.Draft.cells.All(c=>c.style>0),"captured and placed tile stamp");
        yield return Click("실행 취소");
        Require(WorkshopTrial.Draft.cells.Count==0,"one undo removes whole stamp");
        yield return Click("소품");
        yield return Press(point);
        yield return Press(point+Vector2.right*32);
        yield return Click("선택");
        yield return Press(point);
        var keys=InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keys,new KeyboardState(Key.LeftShift)); yield return null; yield return null;
        yield return Press(point+Vector2.right*32);
        InputSystem.QueueStateEvent(keys,new KeyboardState()); yield return null; yield return null;
        InputSystem.RemoveDevice(keys);
        var before=WorkshopTrial.Draft.pieces.Select(p=>p.local).ToArray();
        yield return Drag(point,point+Vector2.up*12);
        var pieces=WorkshopTrial.Draft.pieces;
        Require(pieces.Count==2 && (pieces[0].local-before[0]).sqrMagnitude>.01f &&
            Vector3.Distance(pieces[0].local-before[0],pieces[1].local-before[1])<.002f,"group drag preserves relative offsets");
        yield return Click("선택 삭제");
        Require(pieces.All(p=>p.removed),"group removal");
        yield return Click("실행 취소");
        Require(WorkshopTrial.Draft.pieces.All(p=>!p.removed),"one undo restores group");
        for(int i=0;i<3;i++) yield return Click("실행 취소");
        Require(WorkshopTrial.Draft.pieces.Count==0,"group edits restored original room");
    }
    private IEnumerator Drag(Vector2 from,Vector2 to)
    {
        var fromState=new MouseState{position=PixelViewport.GameToScreenPoint(from,PixelArtUpscaleRig.Output.DisplayRect)};
        var toState=new MouseState{position=PixelViewport.GameToScreenPoint(to,PixelArtUpscaleRig.Output.DisplayRect)};
        InputSystem.QueueStateEvent(mouse,fromState); yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse,fromState.WithButton(MouseButton.Left)); yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse,toState.WithButton(MouseButton.Left)); yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse,toState.WithButton(MouseButton.Left,false)); yield return null; yield return null;
    }
    private void OnDestroy() => Application.logMessageReceived-=CheckException;
    private void CheckException(string message,string trace,LogType type)
    {
        bool missingGreetingGlyph = type==LogType.Warning && message.Contains("character with Unicode") &&
            (message.Contains("[BodyText]") || message.Contains("[Pixel Small (workshop)]"));
        if ((type!=LogType.Exception && !missingGreetingGlyph) || finished) return;
        step=step+" / "+message;
        Finish(false);
    }
    private void Step(string name)
    {
        step=name; deadline=Time.realtimeSinceStartup+40;
        Debug.Log("[WorkshopCheck] "+name);
    }
    private void Update()
    {
        if (!finished && deadline>0 && Time.realtimeSinceStartup>deadline) Finish(false);
    }
    private void Require(bool ok,string name)
    {
        if(!ok) {step=name; Finish(false); throw new InvalidOperationException(name);}
        passed.Add(name);
    }
    private void Finish(bool ok)
    {
        if(finished) return;
        finished=true;
        File.Delete(Path.Combine(output,ok?"failed.txt":"passed.txt"));
        File.WriteAllLines(Path.Combine(output,ok?"passed.txt":"failed.txt"),passed.Concat(new[]{step}));
        Debug.Log("[WorkshopCheck] "+(ok?"PASS ":"FAIL ")+step);
        Application.Quit(ok?0:1);
    }
    private static IEnumerator Wait(Func<bool> ready)
    {
        while(!ready()) yield return null;
        yield return null;
    }
    private IEnumerator Click(string name, Action releasing = null)
    {
        Step(name);
        Button button=null;
        yield return Wait(()=>!UnityNote.SceneLoader.IsLoading && (button=FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.name==name && b.isActiveAndEnabled))!=null);
        var rect=(RectTransform)button.transform;
        var logical=PixelViewport.CanvasToGamePoint(button.GetComponentInParent<Canvas>(),rect.TransformPoint(rect.rect.center));
        yield return Press(logical, releasing);
    }
    private IEnumerator Press(Vector2 logical, Action releasing = null)
    {
        var state=new MouseState{position=PixelViewport.GameToScreenPoint(logical,PixelArtUpscaleRig.Output.DisplayRect)};
        InputSystem.QueueStateEvent(mouse,state); yield return null; yield return null;
        var es=UnityEngine.EventSystems.EventSystem.current;
        if(es!=null && es.currentInputModule is UnityEngine.InputSystem.UI.InputSystemUIInputModule mod)
        {
            var delivered=mod.point.action.ReadValue<Vector2>();
            var hits=new List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es){position=delivered},hits);
            Debug.Log($"[WorkshopCheck] {step} expected={logical} device={mouse.enabled} delivered={delivered} hit={string.Join(",",hits.Select(h=>h.gameObject.name))}");
        }
        InputSystem.QueueStateEvent(mouse,state.WithButton(MouseButton.Left)); yield return null; yield return null;
        releasing?.Invoke();
        InputSystem.QueueStateEvent(mouse,state.WithButton(MouseButton.Left,false)); yield return null; yield return null;
    }
    private IEnumerator Capture(string name)
    {
        for (int i=0;i<12;i++) yield return null;
        yield return new WaitForEndOfFrame();
        var screen=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(output,name+"-window.png"),screen.EncodeToPNG());
        Destroy(screen);
        var frame=PixelArtUpscaleRig.Output.Frame;
        var prior=RenderTexture.active; RenderTexture.active=frame;
        var pic=new Texture2D(frame.width,frame.height,TextureFormat.RGB24,false);
        pic.ReadPixels(new Rect(0,0,frame.width,frame.height),0,0); pic.Apply();
        RenderTexture.active=prior;
        File.WriteAllBytes(Path.Combine(output,name+".png"),pic.EncodeToPNG());
        bool drawn=pic.GetPixels32().Distinct().Take(16).Count()>=16;
        Destroy(pic); Require(drawn,name+" rendered in 640 x 360");
    }
}
#endif

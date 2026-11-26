#if UNITY_EDITOR || PROJECT_CONTENT_QA
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed partial class ProjectRoomVerification : MonoBehaviour
{
    // clusters: 카탈로그 군집 id 두 개. 팀 검사 스크립트가 같은 방의 웨이브 파일을 고칠 때도 읽는다.
    [Serializable] private class Fixture { public string template; public Vector2Int cell,grid; public float sprint; public int waves; public string[] clusters; }
    private string phase, report;
    private float deadline;
    private bool finished;
    private void OnEnable()=>Application.logMessageReceived+=StopOnError;
    private void OnDisable()=>Application.logMessageReceived-=StopOnError;
    private void StopOnError(string message,string stack,LogType type)
    {
        if(type==LogType.Exception) { Debug.LogError(message+"\n"+stack); Finish(false); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        var args=Environment.GetCommandLineArgs();
        int i=Array.IndexOf(args,"-projectRoomCheck");
        if(i<0 || i+2>=args.Length || Application.companyName!="NecromanderVerification") return;
        var holder=new GameObject("Project room verification"); DontDestroyOnLoad(holder);
        var probe=holder.AddComponent<ProjectRoomVerification>();
        probe.phase=args[i+1]; probe.report=args[i+2];
        probe.deadline=Time.realtimeSinceStartup+100;
    }
    private void Update() { if(!finished && Time.realtimeSinceStartup>deadline) Finish(false); }
    private void Finish(bool ok)
    {
        if(finished) return;
        finished=true;
        Debug.Log("[ProjectRoomCheck] "+phase+(ok ? " PASS" : " FAIL"));
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(ok ? 0 : 1);
#else
        Application.Quit(ok ? 0 : 1);
#endif
    }
    private static Button Button(string name) => FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.name==name);
    private static void Click(string name) => (Button(name) ?? throw new InvalidOperationException(name)).onClick.Invoke();
    private IEnumerator Start()
    {
        Application.runInBackground=true;
        Debug.unityLogger.filterLogType=LogType.Log;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        while(UnityNote.SceneLoader.IsLoading) yield return null;
        var catalog=Resources.Load<WorkshopCatalog>("WorkshopCatalog");
        if (phase == "passages") { yield return CheckPassages(catalog); Finish(true); yield break; }
        var fixture=File.Exists(report) ? JsonUtility.FromJson<Fixture>(File.ReadAllText(report)) : new Fixture();
        if(phase=="seed" || phase=="verify" || phase=="worldverify")
        {
            void Seed(Scene scene,LoadSceneMode mode)
            {
                if(scene.name!="BattleScene") return;
                TutorialFlow.IsRunning=false;
                GameManager.Instance.currentFloor=1;
                GameManager.Instance.debugStartFloor=1;
                MapGenerator.Instance.SetNextLayoutSeed(907);
            }
            SceneManager.sceneLoaded+=Seed;
            try
            {
                if(!UnityNote.SceneLoader.Load("BattleScene")) throw new InvalidOperationException("BattleScene");
                yield return null;
                while(UnityNote.SceneLoader.IsLoading || GameManager.Instance==null || !GameManager.Instance.IsPlayerReady) yield return null;
            }
            finally {SceneManager.sceneLoaded-=Seed;}
            var map=MapGenerator.Instance;
            if(WorkshopTrial.Active || map.LastLayoutSeed!=907) throw new InvalidOperationException("Expected ordinary seeded dungeon.");
            var planned=map.CurrentGridLayout.Rooms.First(r=>r.Type==RoomType.Normal &&
                (phase=="seed" || r.Prefab==catalog.FindRoom(fixture.template).room.gameObject));
            var template=catalog.rooms.First(t=>t.room.gameObject==planned.Prefab);
            var room=map.AllRooms.First(r=>r.gridPosition==planned.Cell);
            var ground=room.groundTilemap;
            if(phase=="seed")
            {
                var original=WorkshopCatalog.Map(template.room.transform,0);
                var cell=Vector3Int.zero;
                float closest=float.PositiveInfinity;
                foreach(var candidate in original.cellBounds.allPositionsWithin)
                {
                    float distance=(original.GetCellCenterLocal(candidate)-original.localBounds.center).sqrMagnitude;
                    if(distance>=closest || !original.HasTile(candidate) || map.GroundTileAt(ground.GetCellCenterWorld(candidate))==null) continue;
                    closest=distance; cell=candidate;
                }
                if(float.IsInfinity(closest)) throw new InvalidOperationException("No playable ground cell.");
                if(catalog.clusters.Length<2) throw new InvalidOperationException("Team check needs two enemy clusters.");
                fixture.template=template.id; fixture.cell=(Vector2Int)cell;fixture.grid=planned.Cell;
                fixture.clusters=catalog.clusters.Take(2).Select(c=>c.id).ToArray();
                File.WriteAllText(report,JsonUtility.ToJson(fixture,true));
            }
            else if(phase=="worldverify")
            {
                // 게시한 복사본만 두 번째 군집 한 웨이브를 가진다. 원본 방 문서는 웨이브가 두 개다.
                if(!HasWaves(room,catalog,fixture.clusters[1]))
                    throw new InvalidOperationException("Published variant did not spawn in the ordinary dungeon.");
                if(!UnityNote.SceneLoader.Load("VillageScene"))throw new InvalidOperationException("VillageScene");
                yield return null;
                while(UnityNote.SceneLoader.IsLoading)yield return null;
                if(!FindObjectsByType<NPCBase>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(n=>n.WorkshopGreeting=="반갑습니다."))
                    throw new InvalidOperationException("Published greeting did not reach the ordinary village.");
                Finish(true);yield break;
            }
            else
            {
                if(map.GroundTileAt(ground.GetCellCenterWorld((Vector3Int)fixture.cell))!=null || !HasWaves(room,catalog,fixture.clusters[0]))
                    throw new InvalidOperationException("Merged ground and authored wave did not reach the generated dungeon.");
                if(Mathf.Abs(GameManager.Instance.PLAYERCONTROLLER.WorkshopTuningDefaults()[0]-fixture.sprint)>.0001f ||
                    GameManager.Instance.CurrentStageMapData.GetWavesForFloor(1)!=fixture.waves)
                    throw new InvalidOperationException("Player movement or floor wave count did not reach the live game.");
                if(!Resources.FindObjectsOfTypeAll<ItemSO>().Any(item=>item.name=="ClothArmor" && item.maxStack==17))
                    throw new InvalidOperationException("Saved item property did not reach the game registry.");
                if(!Resources.FindObjectsOfTypeAll<GrowthRegistrySO>().Any(r=>r.items.Any(i=>i!=null && i.itemName=="QA 가죽옷" &&
                    i.maxStack==23 && r.ResolveSavedItem(i.name)==i && r.startingItem==i)))
                    throw new InvalidOperationException("New item did not reach rewards and save lookup.");
                if(catalog.dialogueTable.Get("qa_shop")?.Single().text!="상인이 돌아왔다.")
                    throw new InvalidOperationException("Saved dialogue did not reach the gameplay dialogue table.");
                var lang=UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales.GetLocale("en");
                var liveTranslation=UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.GetTable("UI Text Table",lang);
                if(liveTranslation.GetEntry("Btn_StartGame").Value!="QA Start" || liveTranslation.GetEntry("qa_workshop_title")?.Value!="Editor")
                    throw new InvalidOperationException("Published translation did not reach the active StringTable.");
                Debug.Log("[ProjectRoomCheck] global ground empty at "+fixture.cell+", authored wave "+fixture.clusters[0]+" in "+template.title);
                Finish(true); yield break;
            }
        }
        if(!WorkshopTrial.Open()) throw new InvalidOperationException("Enter workshop");
        yield return null;
        if(phase=="creator")
        {
            while(UnityNote.SceneLoader.IsLoading || Button("프로젝트 연결")==null)yield return null;
            Click("프로젝트 연결");
            string checkout=Environment.GetCommandLineArgs().Last(arg=>arg.StartsWith("-checkout=")).Substring(10);
            GameObject.Find("프로젝트 경로").GetComponent<TMPro.TMP_InputField>().text=checkout;
            Click(Button("연결")!=null ? "연결" : "현재 문서 저장 후 연결");
            if(!ProjectContentStore.Authoring || Path.GetFullPath(ProjectContentStore.Root)!=Path.GetFullPath(Path.Combine(checkout,"DATA")))
                throw new IOException("Creator did not connect to the selected checkout.");
        }
        while(UnityNote.SceneLoader.IsLoading || Button("게임 방 편집")==null) yield return null;
        if(phase=="world")
        {
            uint hash=unchecked((uint)(907 ^ fixture.grid.x*73856093 ^ fixture.grid.y*19349663));
            int slots=1;
            do
            {
                Click("Legacy");
                Button FindTemplate()=>FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.name.EndsWith("복제 "+fixture.template));
                for(int n=0;FindTemplate()==null && n<20;n++)Click("다음");
                (FindTemplate()??throw new InvalidOperationException("Missing source template")).onClick.Invoke();
                Click("전투 설정");Click("웨이브 추가");Click("다음 군집");Click("닫기");Click("던전 등록");
                if(!ProjectRooms.IsRegistered(WorkshopTrial.Draft))throw new InvalidOperationException("Pool link was not saved.");
                slots++;
            }while(hash%slots==0 && slots<16);
            Click("비교");
            if(GameObject.Find("편집 중인 방 render")==null)throw new InvalidOperationException("Room comparison failed.");
            Click("편집으로");Click("게임 설정");Click("마을");Click("무대 플레이");yield return null;
            while(UnityNote.SceneLoader.IsLoading || Button("마을 편집")==null)yield return null;
            Click("마을 편집");GameObject.Find("NPC 인사말").GetComponent<TMPro.TMP_InputField>().text="반갑습니다.";Click("마을 저장");
            Finish(true);yield break;
        }
        if(phase=="resolve")
        {
            Click("공유"); Click("Git 충돌 확인"); Click("현재 쪽 사용"); Click("해결 저장 · 스테이징");
            var resolved=ProjectRooms.Decode(fixture.template,ProjectRooms.Snapshot(fixture.template),catalog);
            if(resolved.waves.Count!=2 || resolved.waves[1]!=fixture.clusters[1] || !resolved.cells.Any(c=>c.at==fixture.cell))
                throw new InvalidOperationException("Conflict choice changed unrelated terrain.");
            Finish(true); yield break;
        }
        Click("게임 방 편집");
        string row="게임 원본 편집 "+fixture.template;
        for(int page=0; Button(row)==null && page<10;page++) Click("다음");
        Click(row);
        yield return null;
        if(phase=="terrain")
        {
            // 원위치(1배)는 방 경계 중심의 30×17칸만 보인다. 큰 방에서는 지울 칸이 화면 밖이라 방 전체를 띄운다.
            Click("전체 보기");
            yield return null;
            Click("지우개");
            yield return null;
            Canvas.ForceUpdateCanvases();
            var camera=GameObject.Find("Workshop camera").GetComponent<Camera>();
            var ground=WorkshopCatalog.Map(camera.transform.parent.GetChild(0),0);
            var point=camera.WorldToViewportPoint(ground.GetCellCenterWorld((Vector3Int)fixture.cell));
            var logical=new Vector2(7+point.x*WorkshopPreview.Width,34+point.y*WorkshopPreview.Height);
            var mouse=InputSystem.AddDevice<Mouse>();
            var keys=InputSystem.AddDevice<Keyboard>();
            try
            {
                var motion=new MouseState{position=PixelViewport.GameToScreenPoint(logical,PixelArtUpscaleRig.Output.DisplayRect)};
                InputSystem.QueueStateEvent(mouse,motion); yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse,motion.WithButton(MouseButton.Left)); yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse,motion); yield return null; yield return null;
                if(!WorkshopTrial.Draft.cells.Any(c=>c.at==fixture.cell && string.IsNullOrEmpty(c.tile)))
                    throw new InvalidOperationException("Terrain brush missed "+fixture.cell+" logical="+logical+" input="+mouse.position.ReadValue()+
                        " display="+PixelArtUpscaleRig.Output.DisplayRect+" cells="+string.Join(",",WorkshopTrial.Draft.cells.Select(c=>c.at)));
                InputSystem.QueueStateEvent(keys,new KeyboardState(Key.LeftCtrl,Key.S)); yield return null; yield return null;
                InputSystem.QueueStateEvent(keys,new KeyboardState()); yield return null;
            }
            finally {InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keys);}
        }
        else
        {
            // waves: 같은 방의 두 번째 담당자가 전투 웨이브만 추가한다. creator: 첫 웨이브의 군집을 바꾼다.
            if(phase=="waves"||phase=="creator"){Click("전투 설정"); Click(phase=="creator" ? "다음 군집" : "웨이브 추가"); Click("닫기");}
            Click("저장");
        }
        var doc=ProjectRooms.Decode(fixture.template,ProjectRooms.Snapshot(fixture.template),catalog);
        if(phase=="terrain" && !doc.cells.Any(c=>c.at==fixture.cell) ||
            phase=="waves" && !doc.waves.SequenceEqual(new[]{fixture.clusters[0]}) ||
            phase=="creator" && !doc.waves.SequenceEqual(new[]{fixture.clusters[1],fixture.clusters[1]}))
            throw new InvalidOperationException("Project save did not match the edit.");
        if(phase=="seed")
        {
            CheckRecovery();
            CheckCellMerge(catalog,fixture);
            Click("내 작업"); Click("플레이어 수치");
            GameObject.Find("클리어 방 질주 배율/+").GetComponent<Button>().onClick.Invoke();
            Click("프로젝트에 저장");
            fixture.sprint=new ProjectPlayerTuning().Value(ProjectPlayerTuning.Sprint,-1);
            Click("게임 설정"); Click("던전");
            GameObject.Find("일반 전투 웨이브/+").GetComponent<Button>().onClick.Invoke();
            Click("설정 저장");
            fixture.waves=ProjectFloorData.ForGame(1,false)?.waves ?? -1;
            if(fixture.sprint<1 || fixture.waves<1) throw new InvalidOperationException("Tuning values were not published.");
            Click("내 작업");Click("콘텐츠 수치");
            var search=GameObject.Find("데이터 검색").GetComponent<TMPro.TMP_InputField>();
            search.onEndEdit.Invoke("ClothArmor");
            Click("BALANCE/ITEMS   ClothArmor");
            search=GameObject.Find("데이터 검색").GetComponent<TMPro.TMP_InputField>();
            search.onEndEdit.Invoke("maxStack");
            var field=FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name.StartsWith("maxStack   ",StringComparison.Ordinal));
            field.onClick.Invoke();
            GameObject.Find("설정값").GetComponent<TMPro.TMP_InputField>().text="17";
            Click("프로젝트에 저장");
            Click("프리셋");GameObject.Find("프리셋 이름").GetComponent<TMPro.TMP_InputField>().text="QA 방어구";
            Click("현재 값으로 만들기");Click("QA 방어구");
            Click("복제해 만들기");GameObject.Find("새 콘텐츠 이름").GetComponent<TMPro.TMP_InputField>().text="QA 가죽옷";Click("만들기");
            GameObject.Find("데이터 검색").GetComponent<TMPro.TMP_InputField>().onEndEdit.Invoke("maxStack");
            FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name.StartsWith("maxStack   ")).onClick.Invoke();
            GameObject.Find("설정값").GetComponent<TMPro.TMP_InputField>().text="23";
            Click("내 작업");Click("모두 저장");
            Click("콘텐츠 수치");Click("에셋 목록");
            GameObject.Find("데이터 검색").GetComponent<TMPro.TMP_InputField>().onEndEdit.Invoke("Growth Reward Registry");
            Click("BALANCE/REGISTRY   Growth Reward Registry");
            GameObject.Find("데이터 검색").GetComponent<TMPro.TMP_InputField>().onEndEdit.Invoke("startingItem");
            FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name.StartsWith("startingItem   ")).onClick.Invoke();
            for(int n=0;Button("QA 가죽옷")==null && n<12;n++)Click("다음");
            Click("QA 가죽옷");Click("프로젝트에 저장");
            Click("내 작업");Click("대사 편집");
            GameObject.Find("새 대화 ID").GetComponent<TMPro.TMP_InputField>().text="qa_shop";
            Click("새 대화");Click("줄 추가");
            GameObject.Find("대사 본문").GetComponent<TMPro.TMP_InputField>().text="상인이 돌아왔다.";
            Click("프로젝트에 저장");
            Click("내 작업");Click("UI·보상 번역");Click("UI Text Table · en");
            GameObject.Find("번역 검색").GetComponent<TMPro.TMP_InputField>().onEndEdit.Invoke("Btn_StartGame");
            FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name.StartsWith("Btn_StartGame   ")).onClick.Invoke();
            GameObject.Find("번역 본문").GetComponent<TMPro.TMP_InputField>().text="QA Start";Click("프로젝트에 저장");
            Click("새 번역");
            GameObject.Find("새 번역 키").GetComponent<TMPro.TMP_InputField>().text="qa_workshop_title";
            GameObject.Find("새 번역 본문").GetComponent<TMPro.TMP_InputField>().text="Editor";Click("프로젝트에 저장");
            yield return CheckPalette();
            File.WriteAllText(report,JsonUtility.ToJson(fixture,true));
        }
        Finish(true);
    }
    private static bool HasWaves(RoomInstance room,WorkshopCatalog catalog,string cluster)
    {
        var waves=room.GetComponent<NormalRoomEvent>()?.AuthoredWaves;
        return waves!=null && waves.Count==1 && waves[0]==catalog.FindCluster(cluster)?.asset;
    }
    private static void CheckCellMerge(WorkshopCatalog catalog,Fixture fixture)
    {
        var doc=WorkshopRoom.Create(catalog.FindRoom(fixture.template));
        var basis=ProjectRooms.Save(doc,new(),catalog,out _);
        var a=ProjectRooms.Decode(doc.id,basis,catalog);var b=ProjectRooms.Decode(doc.id,basis,catalog);
        var other=new Vector2Int(fixture.cell.x^1,fixture.cell.y);
        var tiles=catalog.tiles.Where(t=>t.tile!=null).Take(2).ToArray();
        a.Paint(0,fixture.cell,tiles[0].id);b.Paint(0,other,tiles[1].id);
        ProjectRooms.Save(a,basis,catalog,out _);
        var both=ProjectRooms.Save(b,basis,catalog,out var combined);
        if(combined.cells.Count!=2)throw new InvalidOperationException("Separate cells in one chunk did not merge.");
        a=ProjectRooms.Decode(doc.id,both,catalog);b=ProjectRooms.Decode(doc.id,both,catalog);
        a.Paint(0,fixture.cell,tiles[1].id);b.Paint(0,fixture.cell,"");
        ProjectRooms.Save(a,both,catalog,out _);
        var choices=new System.Collections.Generic.Dictionary<string,ProjectContentStore.Conflict>();
        try{ProjectRooms.Save(b,both,catalog,out _);}
        catch(ProjectContentStore.Conflict conflict){conflict.useMine=true;choices.Add(conflict.field,conflict);}
        if(choices.Count!=1)throw new InvalidOperationException("Same-cell changes must require a choice.");
        var final=ProjectRooms.Save(b,both,catalog,out var reconciled,choices);
        if(reconciled.cells.Count!=2 || reconciled.cells.Single(c=>c.at==other).tile!=tiles[1].id ||
            !string.IsNullOrEmpty(reconciled.cells.Single(c=>c.at==fixture.cell).tile))throw new InvalidOperationException("Unrelated cell changed.");
        string key=ProjectRooms.DirectoryName+"/"+doc.id;
        var stamps=final.Keys.ToDictionary(k=>k,k=>File.GetLastWriteTimeUtc(Path.Combine(ProjectContentStore.Root,key,k)));
        ProjectRooms.Save(reconciled,final,catalog,out _);
        if(stamps.Any(s=>s.Value!=File.GetLastWriteTimeUtc(Path.Combine(ProjectContentStore.Root,key,s.Key))))
            throw new InvalidOperationException("Saving unchanged room wrote to disk.");
        ProjectContentStore.Save(key,final,new(),files=>{if(files.Count!=0)throw new IOException("Unexpected fixture content.");});
        Debug.Log("[ProjectRoomCheck] Disjoint cells merged, same-cell conflict resolved, no-op save wrote zero files.");
    }
#if UNITY_EDITOR
    private const string CrashDocument="QA/SAVE-RECOVERY";
    public static void InterruptSave()
    {
        if(!Application.isBatchMode || Application.companyName!="NecromanderVerification" ||
            !Environment.GetCommandLineArgs().Contains("-projectContentCrashCheck"))
            throw new InvalidOperationException("Only run through the isolated project content test.");
        var empty=new System.Collections.Generic.Dictionary<string,string>();
        var next=new System.Collections.Generic.Dictionary<string,string>{{"a.json","{\"value\":2}"},{"b.json","{\"value\":3}"}};
        ProjectContentStore.AfterReplacementForCheck=path=>{
            File.WriteAllText(Path.Combine(ProjectDataConnection.Checkout,"Library","project-content-crash.marker"),path);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        };
        ProjectContentStore.Save(CrashDocument,empty,next,_=>{});
        throw new InvalidOperationException("The interrupted writer was expected to terminate.");
    }
    private static IEnumerator CheckPalette()
    {
        var entry=WorkshopArtSource.Entries().Single(e=>e.source=="PALETTE/necrohanderPalette.aseprite");
        string path=WorkshopArtSource.Source(entry);
        var original=WorkshopArtSource.Read(path,entry);
        int offset=WorkshopArtSource.PaletteOffset(original,out int count);
        if(offset<0||count!=144)throw new InvalidDataException("Unexpected palette encoding.");
        try
        {
            Click("내 작업");Click("아트 원본");Click("기준 팔레트");
            while(GameObject.Find("현재 원본 preview")==null)yield return null;
            GameObject.Find("색상 코드").GetComponent<TMPro.TMP_InputField>().text="123456";
            Click("색 비교");
            while(GameObject.Find("변경 미리보기 preview")==null)yield return null;
            var preview=(Texture2D)GameObject.Find("변경 미리보기 preview").GetComponent<RawImage>().texture;
            if(!((Color32)preview.GetPixel(0,preview.height-1)).Equals(new Color32(0x12,0x34,0x56,255)))
                throw new InvalidOperationException("First preview swatch did not update.");
            Click("ARTIST 원본 저장");
            var saved=WorkshopArtSource.Read(path,entry);
            var expected=(byte[])original.Clone();
            expected[offset]=0x12;expected[offset+1]=0x34;expected[offset+2]=0x56;
            if(!saved.SequenceEqual(expected))throw new InvalidOperationException("Palette edit touched unrelated Aseprite bytes.");
            Debug.Log("[ProjectRoomCheck] Aseprite preview and palette save verified.");
        }
        finally{File.WriteAllBytes(path,original);}
    }
    private static void CheckRecovery()
    {
        ProjectContentStore.WithDocument(CrashDocument,(root,journal)=>{
            string a=Path.Combine(root,"a.json"),b=Path.Combine(root,"b.json");
            if((int)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(a))["value"]!=2 ||
                (int)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(b))["value"]!=3 || File.Exists(journal))
                throw new InvalidOperationException("Killed writer did not recover both files.");
            File.Delete(a);File.Delete(b);return true;
        });
        Debug.Log("[ProjectRoomCheck] Recovered both files after terminating the previous Unity writer.");
        // The first replacement reached disk before the previous writer stopped.
        string key="QA/"+Guid.NewGuid().ToString("N");
        string folder=Path.Combine(ProjectContentStore.Root,key);
        string a=Path.Combine(folder,"a.json"), b=Path.Combine(folder,"b.json");
        const string old="{\"value\":1}", next="{\"value\":2}";
        ProjectContentStore.WithDocument(key,(_,journal)=>{
            var writes=new Newtonsoft.Json.Linq.JArray();
            foreach(var file in new[]{"a.json","b.json"})
                writes.Add(new Newtonsoft.Json.Linq.JObject{["file"]=file,["before"]=old,["after"]=next});
            ProjectContentStore.ReplaceFile(a,next); ProjectContentStore.ReplaceFile(b,old);
            ProjectContentStore.ReplaceFile(journal,ProjectContentStore.Json(writes));
            return true;
        });
        ProjectContentStore.WithDocument(key,(_,journal)=>{
            if(File.ReadAllText(a)!=next || File.ReadAllText(b)!=next || File.Exists(journal))
                throw new InvalidOperationException("Partial project save did not recover.");
            File.Delete(a); File.Delete(b);
            return true;
        });
        Debug.Log("[ProjectRoomCheck] Both files recovered after an interrupted replacement.");
    }
#else
    private static IEnumerator CheckPalette(){yield break;}
    private static void CheckRecovery() {}
#endif
}
#endif

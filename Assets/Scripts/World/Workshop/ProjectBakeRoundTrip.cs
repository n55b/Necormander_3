#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 굽기 왕복 검사(-Suite BakeRoundTrip·ProjectContent)의 플레이 모드 단계. 편집 모드의 굽기는 Assets/Editor/Map/ProjectBakeRoundTripCheck.cs.
// 인자 -bakeRoundTrip <단계> <폴더>, 진입은 ProjectContentBuild.StartRoomCheck(StartScene 플레이).
//  author: 시드 907 던전에서 원본 방을 고르고 제작실 화면을 눌러 상태이상·아이템·방을 고쳐 저장한다(fixture.json).
//  before: 같은 시드 던전·적용된 SO 값·레지스트리 목록·새 방 내용을 기록하고(before.json) 아이템을 넣은 세이브를 남긴다(save_data.json).
//  after: 구운 뒤 같은 기록(after.json)을 비교해 항목별 차이(differences.txt)를 남기고, 굽기 전 세이브를 불러와 아이템을 확인한다.
// 새 방은 굽기 전에는 원본과 한 자리를 나눠 쓰고(ProjectRooms.Apply) 구운 뒤에는 따로 뽑히는 후보라 같은 시드 배치가 달라진다.
// 그래서 던전 기록 동안 before는 WORLD/POOLS 연결을, after는 구운 새 방 프리팹을 후보에서 잠시 뺀다. 새 방 내용과 등록은 따로 비교한다.
// 런타임 어셈블리에 두는 이유는 WorkshopItemCheck와 같다(DraftStore·ProjectContentVariants 같은 internal 멤버).
public sealed class ProjectBakeRoundTrip : MonoBehaviour
{
    private const string Tag="[BakeRoundTrip] ";
    private string phase, folder;
    private float deadline;
    private bool finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        var args=Environment.GetCommandLineArgs();
        int i=Array.IndexOf(args,"-bakeRoundTrip");
        if(i<0 || i+2>=args.Length || Application.companyName!="NecromanderVerification") return;
        var holder=new GameObject("Bake round trip"); DontDestroyOnLoad(holder);
        var probe=holder.AddComponent<ProjectBakeRoundTrip>();
        probe.phase=args[i+1]; probe.folder=args[i+2];
        probe.deadline=Time.realtimeSinceStartup+300;
    }
    private void OnEnable()=>Application.logMessageReceived+=StopOnError;
    private void OnDisable()=>Application.logMessageReceived-=StopOnError;
    private void StopOnError(string message,string stack,LogType type)
    {
        if(type==LogType.Exception){ Debug.LogError(message+"\n"+stack); Finish(false); }
    }
    private void Update(){ if(!finished && Time.realtimeSinceStartup>deadline){ Debug.LogError(Tag+phase+" timed out"); Finish(false); } }
    private void Finish(bool ok)
    {
        if(finished) return;
        finished=true;
        RoomWorkshopWindow.QuitApproved=true;
        Debug.Log(Tag+phase+(ok ? " PASS" : " FAIL"));
        EditorApplication.Exit(ok ? 0 : 1);
    }
    private string FilePath(string name)=>Path.Combine(folder,name);
    private JObject Read(string name)=>JObject.Parse(File.ReadAllText(FilePath(name)));
    private void Write(string name,JToken json){ Directory.CreateDirectory(folder); File.WriteAllText(FilePath(name),json.ToString()); }

    private IEnumerator Start()
    {
        Application.runInBackground=true;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        while(UnityNote.SceneLoader.IsLoading) yield return null;
        yield return phase switch { "author"=>Author(), "before"=>Record(true), "after"=>Record(false), _=>throw new ArgumentException(phase) };
        Finish(true);
    }

    // ── author: 제작실 화면으로 편집하고 저장한다 ──
    private IEnumerator Author()
    {
        var catalog=Resources.Load<WorkshopCatalog>("WorkshopCatalog");
        yield return Battle();
        // 덮어쓰기·새 방이 없고 다른 방 프리팹이 바탕으로 쓰지 않는 일반 전투 방(ProjectRoomBake가 굽는 조건). 원본 방은 이 시드 던전에 나오는 방.
        var headers=ProjectRooms.Ids().ToDictionary(id=>id,id=>ProjectRooms.Description(id).template);
        string Asset(WorkshopCatalog.Template t)=>AssetDatabase.GUIDToAssetPath(t.id);
        bool Free(WorkshopCatalog.Template t)=>t.inPool && t.type==RoomType.Normal && t.room.GetComponent<NormalRoomEvent>()!=null &&
            !headers.Any(h=>h.Value==t.id && h.Key!=t.id) && (!headers.ContainsKey(t.id) || ProjectRooms.Snapshot(t.id).Keys.All(k=>k=="room.json")) &&
            !catalog.rooms.Any(o=>o!=t && AssetDatabase.GetDependencies(Asset(o)).Contains(Asset(t)));
        var placed=MapGenerator.Instance.CurrentGridLayout.Rooms.Select(r=>r.Prefab).Distinct().ToArray();
        var original=catalog.rooms.FirstOrDefault(t=>placed.Contains(t.room.gameObject) && Free(t));
        Require(original!=null,"a free normal room in the seed 907 dungeon");
        var basis=catalog.rooms.Where(t=>t!=original && Free(t)).OrderBy(t=>t.id,StringComparer.Ordinal).FirstOrDefault();
        Require(basis!=null,"a second free normal room for the new room");
        var fixture=new JObject{["original"]=original.id,["basis"]=basis.id};
        Debug.Log(Tag+"original room "+original.title+", new room on "+basis.title);

        Require(WorkshopTrial.Open(),"enter the workshop");
        yield return null;
        while(UnityNote.SceneLoader.IsLoading || WorkshopStatusCheck.Find("게임 콘텐츠")==null) yield return null;
        yield return Frames();
        // 상태이상: 중독 1회 피해 +1
        Click("게임 콘텐츠"); yield return Frames();
        Click("상태이상"); yield return Frames();
        Click("상태이상/poison"); yield return Frames();
        float poison=StatusRules.POISON_TICK_DAMAGE;
        GameObject.Find("poisonTickDamage/+").GetComponent<Button>().onClick.Invoke(); yield return Frames();
        Click("프로젝트에 저장"); yield return Frames();
        Require(Mathf.Abs(StatusRules.POISON_TICK_DAMAGE-(poison+1))<.0001f,"status screen saved the poison value");

        // 아이템(원본): 최대 보유 +1, 효과 추가(능력치 보정)와 값, 순서 변경(위), 아이콘, 상점 진열 끔
        Click("탭/아이템"); yield return Frames();
        var shop=AssetDatabase.LoadAssetAtPath<ShopRegistrySO>("Assets/SOData/Registry/Shop Registry.asset");
        var feather=Resources.FindObjectsOfTypeAll<ItemSO>().First(i=>i.name=="WindFeather" && AssetDatabase.Contains(i));
        var shield=Resources.FindObjectsOfTypeAll<ItemSO>().First(i=>i.name=="PortableShield" && AssetDatabase.Contains(i));
        string featherId=Guid(feather), shieldId=Guid(shield);
        yield return SelectItem(featherId);
        int stack=feather.MaxStack, effects=feather.effects.Count;
        Require(effects>0 && shop.itemPool.Contains(feather),"WindFeather has an effect and is in the shop");
        GameObject.Find("maxStack/+").GetComponent<Button>().onClick.Invoke(); yield return Frames();
        Click("효과 추가"); yield return Frames();
        Click("효과 종류/능력치 보정"); yield return Frames();
        var draft=DraftStore.All.Single(d=>d.entry.source==feather);
        string effect=draft.files.Where(p=>p.Key.StartsWith("LISTS/effects/",StringComparison.Ordinal) && p.Value.Contains("\"type\""))
            .Select(p=>Path.GetFileNameWithoutExtension(p.Key)).Single();
        Child<Button>("effects/@"+effect+"/value","+").onClick.Invoke(); yield return Frames();
        Click("효과/"+effect+"/위"); yield return Frames();
        Require(feather.effects[effects-1].id==effect,"moving up puts the new effect before the last original one");
        var icon=feather.icon;
        Child<Button>("icon","아이콘").onClick.Invoke(); yield return Frames();
        var pick=Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b=>b.gameObject.activeInHierarchy && b.name.StartsWith("아이콘/",StringComparison.Ordinal) &&
            b.name!="아이콘/"+icon?.name).OrderBy(b=>b.name,StringComparer.Ordinal).FirstOrDefault();
        Require(pick!=null,"icon picker offers another sprite");
        pick.onClick.Invoke(); yield return Frames();
        Require(feather.icon!=icon && feather.icon!=null,"icon choice edits the draft");
        Child<Button>("상점 진열","값").onClick.Invoke(); yield return Frames();
        Click("프로젝트에 저장"); yield return Frames();
        Require(!draft.Dirty && !shop.itemPool.Contains(feather) && feather.MaxStack==stack+1 && feather.effects.Count==effects+1,"item screen saved WindFeather");
        // 새 아이템: 고친 WindFeather를 복제(만들 때 등장 켬), 최대 보유 +1 저장
        Click("새 아이템"); yield return Frames();
        GameObject.Find("새 아이템 이름").GetComponent<TMP_InputField>().text="QA 왕복 깃털";
        Click("시작/복제"); yield return Frames();
        if(GameObject.Find("만들 때 등장").GetComponentInChildren<TMP_Text>().text!="켜짐"){ Click("만들 때 등장"); yield return Frames(); }
        Click("만들기"); yield return Frames();
        var made=ProjectContentVariants.Entries.Single(e=>e.title=="QA 왕복 깃털");
        var madeItem=(ItemSO)made.source;
        int madeStack=madeItem.MaxStack;
        GameObject.Find("maxStack/+").GetComponent<Button>().onClick.Invoke(); yield return Frames();
        Click("프로젝트에 저장"); yield return Frames();
        Require(madeItem.MaxStack==madeStack+1 && shop.itemPool.Contains(madeItem),"new item saved and offered in the shop");
        // 삭제 표시: PortableShield
        Click("아이템 필터/전체"); yield return Frames();
        yield return SelectItem(shieldId);
        Click("아이템 삭제"); yield return Frames();
        Click("프로젝트에 저장"); yield return Frames();
        Click("아이템 필터/전체"); yield return Frames();
        Require(ProjectStructure.IsRemoved(ProjectParameters.Snapshot(ProjectFolder(shield))),"PortableShield marked removed");
        Require(!DraftStore.Unsaved.Any(),"content screens leave nothing unsaved");
        fixture["feather"]=featherId; fixture["shield"]=shieldId; fixture["item"]=made.id; fixture["effect"]=effect;

        // 원본 방: 지형 한 칸 지우기, 소품 하나, 웨이브 하나
        Click("처음 화면"); yield return Frames();
        Click("레벨"); yield return Frames();
        Click("게임 방 편집"); yield return Frames();
        string row="게임 원본 편집 "+original.id;
        for(int n=0;WorkshopStatusCheck.Find(row)?.gameObject.activeInHierarchy!=true && n<10;n++){ Click("다음"); yield return Frames(); }
        Click(row); yield return Frames();
        var ground=Ground(original);
        yield return MapClick("지우개",ground[0]);
        yield return MapClick("소품",ground[1]);
        Require(WorkshopTrial.Draft.cells.Any(c=>c.at==ground[0] && string.IsNullOrEmpty(c.tile)) && WorkshopTrial.Draft.pieces.Count==1,"erase and prop reach the room draft");
        Click("전투 설정"); yield return Frames();
        Click("웨이브 추가"); yield return Frames();
        Click("닫기"); yield return Frames();
        Click("저장"); yield return Frames();
        var saved=ProjectRooms.Decode(original.id,ProjectRooms.Snapshot(original.id),catalog);
        Require(saved.cells.Any(c=>c.at==ground[0]) && saved.pieces.Count==1 && saved.waves.Count==1,"original room saved to DATA");
        // 새 방: 두 번째 원본을 복제해 한 칸 지우고 웨이브를 넣어 던전에 등록
        Click("처음 화면"); yield return Frames();
        Click("레벨"); yield return Frames();
        Click("Legacy"); yield return Frames();
        Button Copy()=>Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.gameObject.activeInHierarchy && b.name.EndsWith("복제 "+basis.id,StringComparison.Ordinal));
        for(int n=0;Copy()==null && n<20;n++){ Click("다음"); yield return Frames(); }
        Require(Copy()!=null,"Legacy lists the second room");
        Copy().onClick.Invoke(); yield return Frames();
        var ground2=Ground(basis);
        yield return MapClick("지우개",ground2[0]);
        Click("전투 설정"); yield return Frames();
        Click("웨이브 추가"); yield return Frames();
        Click("닫기"); yield return Frames();
        Click("던전 등록"); yield return Frames();
        var room=WorkshopTrial.Draft;
        Require(room.id!=basis.id && ProjectRooms.IsRegistered(room) && ProjectRooms.Snapshot(room.id).Keys.Any(k=>k.StartsWith("ENCOUNTER/",StringComparison.Ordinal)),"new room saved and registered");
        fixture["room"]=room.id;
        Write("fixture.json",fixture);
        Debug.Log(Tag+"authored "+fixture.ToString(Newtonsoft.Json.Formatting.None));
    }
    private static string ProjectFolder(ScriptableObject so)=>ProjectParameterBindings.Group(AssetDatabase.GetAssetPath(so))+"/"+Guid(so);
    // 원본 프리팹의 바닥 칸 중 경계 중심에서 가까운 두 칸(서로 두 칸 이상 떨어짐). 전체 보기에서 누를 수 있다.
    private static Vector2Int[] Ground(WorkshopCatalog.Template t)
    {
        var map=WorkshopCatalog.Map(t.room.transform,0);
        var center=map.localBounds.center;
        var cells=new List<Vector2Int>();
        foreach(var c in map.cellBounds.allPositionsWithin) if(map.HasTile(c)) cells.Add((Vector2Int)c);
        cells=cells.OrderBy(c=>(map.GetCellCenterLocal((Vector3Int)c)-center).sqrMagnitude).ThenBy(c=>c.x).ThenBy(c=>c.y).ToList();
        var second=cells.First(c=>Mathf.Abs(c.x-cells[0].x)+Mathf.Abs(c.y-cells[0].y)>=2);
        return new[]{cells[0],second};
    }
    // 방 편집 지도: 전체 보기에서 도구를 고르고 그 칸을 가상 마우스로 한 번 누른다(ProjectRoomVerification terrain 단계와 같은 좌표 변환).
    private static IEnumerator MapClick(string tool,Vector2Int cell)
    {
        Click("전체 보기"); yield return Frames();
        Click(tool); yield return Frames();
        Canvas.ForceUpdateCanvases();
        var camera=GameObject.Find("Workshop camera").GetComponent<Camera>();
        var ground=WorkshopCatalog.Map(camera.transform.parent.GetChild(0),0);
        var point=camera.WorldToViewportPoint(ground.GetCellCenterWorld((Vector3Int)cell));
        var logical=new Vector2(7+point.x*WorkshopPreview.Width,34+point.y*WorkshopPreview.Height);
        var mouse=InputSystem.AddDevice<Mouse>();
        try
        {
            var motion=new MouseState{position=PixelViewport.GameToScreenPoint(logical,PixelArtUpscaleRig.Output.DisplayRect)};
            InputSystem.QueueStateEvent(mouse,motion); yield return Frames();
            InputSystem.QueueStateEvent(mouse,motion.WithButton(MouseButton.Left)); yield return Frames();
            InputSystem.QueueStateEvent(mouse,motion); yield return Frames();
        }
        finally { InputSystem.RemoveDevice(mouse); }
    }
    private static IEnumerator SelectItem(string id)
    {
        for(int n=0;n<10 && WorkshopStatusCheck.Find("아이템/"+id)?.gameObject.activeInHierarchy!=true;n++){ Click("아이템 다음 쪽"); yield return Frames(); }
        Click("아이템/"+id); yield return Frames();
    }

    // ── before·after: 게임 상태 기록 ──
    private IEnumerator Record(bool before)
    {
        var fixture=Read("fixture.json");
        var catalog=Resources.Load<WorkshopCatalog>("WorkshopCatalog");
        var bake=before ? null : Read("bake.json");
        SaveSystem.DeleteSave();
        var state=new JObject();
        // 새 방을 후보에서 뺀 같은 시드 던전. 값·목록도 이 동안 기록한다(after의 RoomPrefabData 값이 뺀 상태여야 before와 같다).
        string pools=Path.Combine(ProjectContentStore.Root,"WORLD","POOLS"), hidden=FilePath("POOLS hidden");
        var removed=new List<(List<GameObject> list,int index,GameObject prefab)>();
        try
        {
            if(before){ if(Directory.Exists(pools)) Directory.Move(pools,hidden); }
            else
            {
                var baked=((JObject)bake["rooms"]).Properties().Select(p=>AssetDatabase.LoadAssetAtPath<GameObject>((string)p.Value["path"])).ToHashSet();
                foreach(var data in Resources.FindObjectsOfTypeAll<RoomPrefabDataSO>())
                    foreach(var entry in data.roomEntries.Where(e=>e?.prefabs!=null))
                        for(int i=entry.prefabs.Count-1;i>=0;i--)
                            if(baked.Contains(entry.prefabs[i])){ removed.Add((entry.prefabs,i,entry.prefabs[i])); entry.prefabs.RemoveAt(i); }
            }
            ProjectRooms.Reset();
            yield return Battle();
            state["dungeon"]=Dungeon();
            state["values"]=Values();
            state["lists"]=Lists();
        }
        finally
        {
            if(Directory.Exists(hidden)) Directory.Move(hidden,pools);
            foreach(var (list,index,prefab) in Enumerable.Reverse(removed)) list.Insert(index,prefab);
            ProjectRooms.Reset();
        }
        // 새 방 내용: before는 원본 프리팹 + 방 문서(게임의 ProjectRooms.Apply와 같은 ApplyToGame), after는 구운 프리팹. 등록은 던전 후보 여부.
        var rooms=new JObject();
        if(before)
            foreach(string id in ProjectRooms.Ids())
            {
                var files=ProjectRooms.Snapshot(id);
                var doc=ProjectRooms.Description(id);
                if(doc.template==id || files.ContainsKey(ProjectStructure.RemovedFile)) continue;
                doc=ProjectRooms.Decode(id,files,catalog);
                var template=catalog.FindRoom(doc.template).room;
                rooms[id]=new JObject{["registered"]=ProjectRooms.IsRegistered(doc),
                    ["lines"]=new JArray(Instance(template.gameObject,r=>WorkshopGeometry.ApplyToGame(doc,r,catalog)))};
            }
        else
            foreach(var p in ((JObject)bake["rooms"]).Properties())
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>((string)p.Value["path"]);
                var template=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath((string)p.Value["template"]));
                Require(prefab!=null,"baked prefab "+p.Value["path"]);
                bool registered=Resources.FindObjectsOfTypeAll<RoomPrefabDataSO>().Any(d=>d.roomEntries.Any(e=>e?.prefabs!=null && e.prefabs.Contains(prefab) && e.prefabs.Contains(template)));
                rooms[p.Name]=new JObject{["registered"]=registered,["lines"]=new JArray(Instance(prefab,null))};
            }
        state["rooms"]=rooms;
        // 세이브: 고친 원본·새 아이템·삭제한 원본을 넣고 체크포인트를 쓴다(before). after는 그 파일을 불러온다.
        var growth=GameManager.Instance.dataManager.GET_GROWTH_REGISTRY();
        if(before)
        {
            var pouch=GameManager.Instance.itemPouch;
            foreach(string saved in new[]{"WindFeather","content_"+(string)fixture["item"],"PortableShield"})
            {
                var item=growth.ResolveSavedItem(saved);
                Require(item!=null && (pouch.TryAdd(item) || pouch.Contains(item)),"pouch holds "+saved);
            }
            Require(GameManager.Instance.SaveCurrentState(),"write the pre-bake checkpoint");
            File.Copy(Path.Combine(Application.persistentDataPath,"save_data.json"),FilePath("save_data.json"),true);
            state["pouch"]=Pouch();
            Write("before.json",state);
            Debug.Log(Tag+"recorded "+((JObject)state["values"]).Count+" assets, "+((JObject)state["dungeon"]).Count+" dungeon parts, "+rooms.Count+" new rooms");
            yield break;
        }
        Write("after.json",state);
        var differences=Compare(Read("before.json"),state);
        // 굽기 전 세이브를 불러온다: 체크포인트를 되돌리고 BattleScene을 새로 연다(GameManager.Awake가 읽는다).
        Directory.CreateDirectory(Application.persistentDataPath);
        File.Copy(FilePath("save_data.json"),Path.Combine(Application.persistentDataPath,"save_data.json"),true);
        yield return Battle(seed:false);
        var expected=Read("before.json")["pouch"].Values<string>().ToArray();
        var loaded=Pouch();
        if(!loaded.Values<string>().SequenceEqual(expected))
            differences.Add("세이브 주머니: 굽기 전 "+string.Join(",",expected)+" · 불러온 뒤 "+string.Join(",",loaded.Values<string>()));
        var madeItem=GameManager.Instance.dataManager.GET_GROWTH_REGISTRY().ResolveSavedItem("content_"+(string)fixture["item"]);
        if(madeItem==null || !AssetDatabase.Contains(madeItem) || !madeItem.formerAssetNames.Contains("content_"+(string)fixture["item"]) || madeItem.DisplayName!="QA 왕복 깃털")
            differences.Add("세이브: 새 아이템 content_"+fixture["item"]+" 이(가) 구운 에셋으로 풀리지 않음 ("+(madeItem!=null ? madeItem.name+" "+AssetDatabase.GetAssetPath(madeItem) : "없음")+")");
        File.WriteAllLines(FilePath("differences.txt"),differences.DefaultIfEmpty("차이 없음"));
        foreach(string line in differences.Take(60)) Debug.LogError(Tag+line);
        Require(differences.Count==0,differences.Count+" differences after the bake; see "+FilePath("differences.txt"));
        Debug.Log(Tag+"same assets, lists, seeded dungeon, new rooms and save after the bake");
    }
    private JArray Pouch()
    {
        var pouch=GameManager.Instance.itemPouch;
        return new JArray(Enumerable.Range(0,pouch.SlotCount).Select(i=>pouch.Get(i)).Where(i=>i!=null).Select(i=>Key(i)));
    }
    private static IEnumerator Battle(bool seed=true)
    {
        void Seed(Scene scene,LoadSceneMode mode)
        {
            if(scene.name!="BattleScene") return;
            TutorialFlow.IsRunning=false;
            GameManager.Instance.currentFloor=1;
            GameManager.Instance.debugStartFloor=1;
            GameManager.Instance.testMode_DisableAutoBattle=true;
            MapGenerator.Instance.SetNextLayoutSeed(907);
        }
        if(seed) SceneManager.sceneLoaded+=Seed;
        try
        {
            Require(UnityNote.SceneLoader.Load("BattleScene"),"load BattleScene");
            yield return null;
            while(UnityNote.SceneLoader.IsLoading || GameManager.Instance==null || !GameManager.Instance.IsPlayerReady) yield return null;
        }
        finally { SceneManager.sceneLoaded-=Seed; }
        Require(!WorkshopTrial.Active && (!seed || MapGenerator.Instance.LastLayoutSeed==907),"ordinary seeded dungeon");
        yield return Frames();
    }

    // 값 비교용 표기. 실행 중 새 콘텐츠와 구운 새 에셋은 content_<id>(옛 세이브 키), 원본은 에셋 GUID.
    private static string Key(Object o)
    {
        if(o==null) return "";
        var live=ProjectContentVariants.Entries.FirstOrDefault(e=>e.source==o);
        if(live!=null) return "content_"+live.id;
        if(o.GetType().GetField("formerAssetNames")?.GetValue(o) is string[] former &&
            former.FirstOrDefault(n=>n!=null && n.StartsWith("content_",StringComparison.Ordinal)) is string alias) return alias;
        return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string guid,out long _) ? guid : o.name;
    }
    private static string Guid(Object o)=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o));
    // 적용된 모든 바인딩 대상과 새 콘텐츠의 전체 필드. formerAssetNames는 굽기가 정하고, 표시 이름은 (표시 이름)으로 비교한다(ProjectDataBake.Snapshot과 같다).
    private static JObject Values()
    {
        var values=new JObject();
        foreach(var so in ProjectParameterBindings.All().Select(e=>e.source).Concat(ProjectContentVariants.Entries.Select(e=>e.source)).Distinct())
        {
            string display=ProjectParameters.DisplayNameField(so);
            var fields=new JObject{["(이름)"]=ProjectContentVariants.Title(so) ?? so.name};
            foreach(var f in ProjectParameters.Fields(so).Where(f=>!f.path.StartsWith("formerAssetNames/",StringComparison.Ordinal) && f.path!=display))
            {
                object value=f.read();
                fields[f.path]=f.IsReference ? Key((Object)value) : f.IsAsset ? WorkshopCatalog.AssetKey((Object)value) ?? Key((Object)value) :
                    Convert.ToString(value,CultureInfo.InvariantCulture);
            }
            values[Key(so)]=fields;
        }
        return values;
    }
    // 레지스트리 목록은 순서까지 비교한다(같은 시드의 보상·상점 추첨이 순서를 따른다).
    private static JObject Lists()
    {
        var lists=new JObject();
        foreach(var registry in ProjectParameterBindings.All().Select(e=>e.source).Where(s=>s is GrowthRegistrySO or ShopRegistrySO))
            foreach(var (field,list) in ProjectContentVariants.Lists(registry))
                if(list!=null) lists[registry.name+"/"+field]=new JArray(list.Cast<Object>().Select(Key));
        return lists;
    }
    private static readonly Regex CloneSuffix=new(@"\(Clone\)");
    private static string Name(Transform t)=>CloneSuffix.Replace(t.name,"").Trim();
    private static string PathOf(Transform root,Transform t)=>t==root || t==null ? "" : PathOf(root,t.parent)+"/"+Name(t);
    private static string Cells(Tilemap map)
    {
        var lines=new List<string>();
        foreach(var at in map.cellBounds.allPositionsWithin)
            if(map.GetTile(at) is { } tile)
            {
                // 원본 프리팹의 끊긴 타일 참조(예: Room_Shop 변형의 Unsteppable 6칸)는 실행마다 셀 색이 달라 색을 보지 않는다.
                if(tile==null || tile.name.Length==0){ lines.Add(at.x+","+at.y+" (끊긴 타일)"); continue; }
                var matrix=map.GetTransformMatrix(at); var color=(Color32)map.GetColor(at);
                lines.Add(at.x+","+at.y+" "+tile.name+(matrix==Matrix4x4.identity ? "" : " "+matrix.ToString().Replace("\n"," "))+(color.Equals(new Color32(255,255,255,255)) ? "" : " "+color));
            }
        return string.Join(";",lines);
    }
    // 방 하나의 내용: 타일맵 셀(모양·색), 하위 오브젝트의 경로·활성·위치, 출구, 전투 웨이브. 루트 이름과 (Clone)은 뺀다.
    private static IEnumerable<string> Room(Transform root)
    {
        var lines=new List<string>();
        foreach(var map in root.GetComponentsInChildren<Tilemap>(true)) lines.Add("타일 "+PathOf(root,map.transform)+" "+Cells(map));
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
            if(t!=root) lines.Add(PathOf(root,t)+" "+t.gameObject.activeSelf+" "+t.localPosition.x.ToString("F3",CultureInfo.InvariantCulture)+","+
                t.localPosition.y.ToString("F3",CultureInfo.InvariantCulture)+","+t.localPosition.z.ToString("F3",CultureInfo.InvariantCulture));
        foreach(var anchor in root.GetComponentsInChildren<RoomAnchor>(true)) lines.Add("출구 "+anchor.direction+" "+PathOf(root,anchor.transform));
        if(root.TryGetComponent<NormalRoomEvent>(out var combat))
            lines.Add("웨이브 "+string.Join(",",combat.AuthoredWaves.Select(w=>w!=null ? w.name : ""))+" 무작위 적 "+combat.UsesRandomEnemies);
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }
    private static IEnumerable<string> Instance(GameObject prefab,Action<RoomInstance> apply)
    {
        var parent=new GameObject("Bake round trip room"); parent.SetActive(false);   // Awake 없이 내용만 본다
        try
        {
            var room=Instantiate(prefab,parent.transform).GetComponent<RoomInstance>();
            apply?.Invoke(room);
            return Room(room.transform).ToArray();
        }
        finally { DestroyImmediate(parent); }
    }
    // 같은 시드 던전: 배치(칸·종류·프리팹·문), 방마다 내용, 방 밖 타일맵(글로벌 바닥·벽·미니맵) 셀.
    private static JObject Dungeon()
    {
        var map=MapGenerator.Instance;
        var dungeon=new JObject{["배치"]=new JArray(map.CurrentGridLayout.Rooms.Select(r=>r.Cell+" "+r.Type+" "+r.Prefab.name)
            .Concat(map.CurrentGridLayout.Doors.Select(d=>"문 "+d.From+"-"+d.To)))};
        foreach(var room in map.AllRooms.OrderBy(r=>r.gridPosition.x).ThenBy(r=>r.gridPosition.y))
            dungeon["방 "+room.gridPosition]=new JArray(Room(room.transform));
        foreach(var tilemap in Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                     .Where(m=>m.gameObject.scene.name=="BattleScene" && m.GetComponentInParent<RoomInstance>(true)==null).OrderBy(m=>PathOf(null,m.transform),StringComparer.Ordinal))
            dungeon["타일맵 "+PathOf(null,tilemap.transform)]=new JArray(Cells(tilemap).Split(';'));
        return dungeon;
    }

    // 항목별 차이: 값(에셋/필드), 목록(순서), 던전(배치·방·타일맵), 새 방(내용·등록).
    private static List<string> Compare(JObject before,JObject after)
    {
        var differences=new List<string>();
        var a=(JObject)before["values"]; var b=(JObject)after["values"];
        foreach(string key in Keys(a,b))
        {
            if(a[key]==null || b[key]==null){ differences.Add("값 "+key+" "+(a[key]?["(이름)"] ?? b[key]["(이름)"])+": "+(a[key]==null ? "굽기 뒤에만 있음" : "굽기 뒤에 없음")); continue; }
            foreach(string field in Keys((JObject)a[key],(JObject)b[key]))
                if((string)a[key][field]!=(string)b[key][field])
                    differences.Add("값 "+a[key]["(이름)"]+"/"+field+": 굽기 전 "+a[key][field]+" · 구운 뒤 "+b[key][field]);
        }
        foreach(string section in new[]{"lists","dungeon"})
        {
            var x=(JObject)before[section]; var y=(JObject)after[section];
            foreach(string key in Keys(x,y))
            {
                var p=x[key]?.Values<string>().ToArray() ?? Array.Empty<string>(); var q=y[key]?.Values<string>().ToArray() ?? Array.Empty<string>();
                if(p.SequenceEqual(q)) continue;
                string Head(IEnumerable<string> s)=>string.Join(" / ",s.Take(4).Select(l=>l.Length>240 ? l.Substring(0,240)+"…" : l));
                differences.Add((section=="lists" ? "목록 " : "던전 ")+key+": 굽기 전에만 "+Head(p.Except(q))+" · 구운 뒤에만 "+Head(q.Except(p))+(p.Except(q).Any() || q.Except(p).Any() ? "" : " · 순서가 다름"));
            }
        }
        var r=(JObject)before["rooms"]; var s2=(JObject)after["rooms"];
        foreach(string id in Keys(r,s2))
        {
            if(r[id]==null || s2[id]==null){ differences.Add("새 방 "+id+": "+(r[id]==null ? "굽기 전에 없음" : "구운 프리팹이 없음")); continue; }
            if((bool)r[id]["registered"]!=(bool)s2[id]["registered"]) differences.Add("새 방 "+id+": 던전 등록 굽기 전 "+r[id]["registered"]+" · 구운 뒤 "+s2[id]["registered"]);
            var p=r[id]["lines"].Values<string>().ToArray(); var q=s2[id]["lines"].Values<string>().ToArray();
            if(!p.SequenceEqual(q)) differences.Add("새 방 "+id+": 굽기 전에만 "+string.Join(" / ",p.Except(q).Take(4))+" · 구운 뒤에만 "+string.Join(" / ",q.Except(p).Take(4)));
        }
        return differences;
    }
    private static IEnumerable<string> Keys(JObject a,JObject b)=>a.Properties().Select(p=>p.Name).Union(b.Properties().Select(p=>p.Name)).OrderBy(k=>k,StringComparer.Ordinal);

    private static T Child<T>(string parent,string child) where T:Component
    {
        var found=Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Where(t=>t.gameObject.activeInHierarchy && t.name==parent)
            .SelectMany(t=>t.Cast<Transform>()).Where(c=>c.name==child && c.gameObject.activeInHierarchy).Select(c=>c.GetComponent<T>()).Where(c=>c!=null).ToArray();
        Require(found.Length==1,found.Length+" '"+child+"' under '"+parent+"'");
        return found[0];
    }
    private static void Click(string name)=>WorkshopStatusCheck.Click(name);
    private static IEnumerator Frames(int count=2)=>WorkshopStatusCheck.Frames(count);
    private static void Require(bool condition,string message)
    {
        if(!condition) throw new InvalidOperationException(Tag+message);
    }
}
#endif

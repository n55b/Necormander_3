using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// A room prefab edit (or an edit to a prefab it nests) changes the template revision, and every DATA room
// document of that template then fails validation. Rebake the catalog, move each document onto its template's
// current revision and validate its tiles, objects and exits against the new prefab before keeping the change.
// 자동 실행: 프리팹 임포트·저장 직후(delayCall로 한 번에), 플레이 시작 직전, 빌드 전(ProjectContentBuild.PrepareForBuild).
[InitializeOnLoad]
public static class ProjectRoomRebase
{
    // 배치 모드 검사는 DATA 차이와 카탈로그를 정확히 비교하므로 자동 처리를 끈다. ProjectRoomRebaseCheck만 켠다.
    internal static bool AutoInBatch;
    internal static int Runs { get; private set; }
    internal static bool Busy => pending || running;
    private static bool pending, running;
    private static readonly HashSet<string> changed=new(StringComparer.Ordinal);
    private static string RoomsRoot => Path.Combine(ProjectContentStore.Root,ProjectRooms.DirectoryName);

    static ProjectRoomRebase()
    {
        EditorApplication.playModeStateChanged+=state=>{ if(state==PlayModeStateChange.ExitingEditMode) Auto(null,"플레이 시작"); };
    }

    [MenuItem("Tools/제작실/원본 방 재기준(강제)")]
    public static void Run()
    {
        var failed=Rebase();
        if(failed.Count>0) throw new InvalidDataException("새 원본에서 검증하지 못한 방 문서는 그대로 두었습니다.\n"+string.Join("\n",failed));
    }

    internal static void BeforeBuild()=>Auto(null,"빌드");

    private sealed class Watch : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] movedFrom)
        {
            if(running || (Application.isBatchMode && !AutoInBatch)) return;
            foreach(string path in imported.Concat(moved))
                if(path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)) changed.Add(path);
            if(changed.Count==0 || pending) return;
            pending=true;
            EditorApplication.delayCall+=Flush;   // 일괄 임포트(풀·되돌리기)는 여러 번 불려도 한 번만 처리한다.
        }
    }

    private static void Flush()
    {
        pending=false;
        var paths=changed.ToArray(); changed.Clear();
        // 플레이 중이거나 컴파일 중에 놓친 변경은 다음 플레이 시작·빌드 검사가 찾는다.
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Auto(paths,"프리팹 저장");
    }

    private static void Auto(ICollection<string> paths,string when)
    {
        if(running || (Application.isBatchMode && !AutoInBatch) || !Directory.Exists(RoomsRoot)) return;
        var catalog=AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath);
        if(catalog==null) return;
        try
        {
            if(!Drifted(catalog,paths)) return;
            foreach(string failure in Rebase())
                Debug.LogWarning($"[Workshop] {when}: 원본 방이 바뀌었지만 이 방 문서는 새 원본에서 검증되지 않아 그대로 두었습니다. {failure}");
        }
        catch(Exception e) { Debug.LogWarning($"[Workshop] {when}: 원본 방 자동 재기준을 하지 못했습니다. {e.Message}"); }
    }

    private static IEnumerable<(string header,string template,string baseRevision)> Documents()
    {
        foreach(string folder in Directory.Exists(RoomsRoot) ? Directory.GetDirectories(RoomsRoot) : Array.Empty<string>())
        {
            string header=Path.Combine(folder,"room.json");
            if(!File.Exists(header)) continue;
            var json=JObject.Parse(File.ReadAllText(header));
            yield return (header,(string)json["template"],(string)json["baseRevision"]);
        }
    }

    // paths가 있으면 그 프리팹에 의존하는 원본 방만, 없으면 DATA 문서가 쓰는 원본 방을 본다.
    private static bool Drifted(WorkshopCatalog catalog,ICollection<string> paths)
    {
        var docs=Documents().ToArray();
        var templates=paths==null ? docs.Select(d=>d.template).Distinct() : catalog.rooms.Select(t=>t.id);
        foreach(string id in templates)
        {
            var template=catalog.FindRoom(id);
            string path=AssetDatabase.GUIDToAssetPath(id);
            if(template==null || string.IsNullOrEmpty(path)) return true;
            if(paths!=null && !AssetDatabase.GetDependencies(path).Any(paths.Contains)) continue;
            string now=WorkshopBuild.Revision(path);
            if(now!=template.revision || docs.Any(d=>d.template==id && d.baseRevision!=now)) return true;
        }
        return false;
    }

    // Returns the documents left on their old revision because they no longer validate against the new prefab.
    private static List<string> Rebase()
    {
        running=true;
        try
        {
            WorkshopBuild.Bake();
            Runs++;
            var catalog=AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath);
            var failed=new List<string>();
            int moved=0;
            foreach(var doc in Documents().ToArray())
            {
                string before=File.ReadAllText(doc.header), id=Path.GetFileName(Path.GetDirectoryName(doc.header));
                var json=JObject.Parse(before);
                try
                {
                    string revision=catalog.FindRoom(doc.template)?.revision ??
                        throw new InvalidDataException("원본 방을 카탈로그에서 찾을 수 없습니다.");
                    if(doc.baseRevision==revision) continue;
                    json["baseRevision"]=revision;
                    File.WriteAllText(doc.header,json.ToString(Formatting.Indented).Replace("\r\n","\n")+"\n");
                    try { ProjectRooms.ValidateForBuild(id,catalog); }
                    catch { File.WriteAllText(doc.header,before); throw; }
                    moved++;
                    Debug.Log($"[Workshop] {json["title"]} ({id}) now builds on revision {revision}.");
                }
                catch(Exception e) { failed.Add($"{json["title"]} ({id}): {e.Message}"); }
            }
            Debug.Log($"[Workshop] Rebased {moved} project room documents.");
            return failed;
        }
        finally { running=false; }
    }
}

// -Suite LevelWorkshop·ProjectContent: 방 프리팹과 중첩 프리팹을 저장만 해서 baseRevision이 옮겨지는지,
// 새 원본에서 검증되지 않는 문서는 그대로 남고 경고하는지, 일괄 임포트가 한 번만 처리되는지 확인한다.
public static class ProjectRoomRebaseCheck
{
    private const string Marker="Rebase Check Marker";
    private static readonly Queue<Action> steps=new();
    private static int idle;

    public static void Run()
    {
        if(!Application.isBatchMode || Application.companyName!="NecromanderVerification")
            throw new InvalidOperationException("Run in the isolated verification project.");
        try { Start(); }
        catch(Exception e) { Fail(e,new(),new(),null); }   // 준비 단계는 아무것도 바꾸지 않는다.
    }

    private static void Start()
    {
        ProjectRoomRebase.AutoInBatch=true;
        var warnings=new List<string>();
        Application.logMessageReceived+=(message,_,type)=>{ if(type==LogType.Warning && message.StartsWith("[Workshop]")) warnings.Add(message); };

        string root=Path.Combine(ProjectContentStore.Root,ProjectRooms.DirectoryName);
        var headers=Directory.GetDirectories(root).Select(f=>Path.Combine(f,"room.json")).Where(File.Exists).OrderBy(h=>h,StringComparer.Ordinal).ToArray();
        var original=headers.ToDictionary(h=>h,File.ReadAllText);
        static JObject Json(string h)=>JObject.Parse(File.ReadAllText(h));
        static string Template(string h)=>(string)Json(h)["template"];
        static string Base(string h)=>(string)Json(h)["baseRevision"];
        static WorkshopCatalog Catalog()=>AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath);
        static string PathOf(string guid)=>AssetDatabase.GUIDToAssetPath(guid);
        static Transform Find(Transform t,string name)=>t.GetComponentsInChildren<Transform>(true).FirstOrDefault(c=>c.name==name);

        foreach(string h in headers)
            Require(Base(h)==Catalog().FindRoom(Template(h))?.revision && Base(h)==WorkshopBuild.Revision(PathOf(Template(h))),
                "starts in sync "+h);
        var byTemplate=headers.GroupBy(Template).ToDictionary(g=>g.Key,g=>g.ToArray());
        var room=byTemplate.Where(p=>Find(AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(p.Key)).transform,"DecorateObject")!=null)
            .OrderByDescending(p=>p.Value.Length).ThenBy(p=>p.Key,StringComparer.Ordinal).First();
        string roomPath=PathOf(room.Key);
        var templatePaths=Catalog().rooms.Select(t=>PathOf(t.id)).ToHashSet();
        string nested=byTemplate.Keys.SelectMany(id=>AssetDatabase.GetDependencies(PathOf(id)))
            .Where(p=>p.StartsWith("Assets/Prefabs/Map/",StringComparison.Ordinal) && p.EndsWith(".prefab") && !templatePaths.Contains(p))
            .Distinct().OrderBy(p=>p,StringComparer.Ordinal).First();
        Debug.Log($"[ProjectRoomRebaseCheck] room {roomPath} ({room.Value.Length} documents), nested {nested}");
        var sources=new[]{roomPath,nested}.ToDictionary(p=>p,File.ReadAllBytes);
        string failing=room.Value[0], piece=null;
        Dictionary<string,string> before=null;
        int runs=0;

        void Save(string path,Action<Transform> change)
        {
            before=headers.ToDictionary(h=>h,File.ReadAllText); runs=ProjectRoomRebase.Runs;
            var contents=PrefabUtility.LoadPrefabContents(path);
            try { change(contents.transform); PrefabUtility.SaveAsPrefabAsset(contents,path); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        // 저장한 프리팹에 의존하는 방의 문서만 새 버전으로 옮겨졌고 나머지 문서는 바이트 그대로다.
        void Moved(string saved,params string[] stay)
        {
            Require(ProjectRoomRebase.Runs==runs+1,$"one rebase after saving {saved} ({ProjectRoomRebase.Runs-runs})");
            foreach(string h in headers)
            {
                string path=PathOf(Template(h));
                if(!AssetDatabase.GetDependencies(path).Contains(saved) || stay.Contains(h))
                    Require(File.ReadAllText(h)==before[h],"unaffected document kept "+h);
                else
                    Require(Base(h)!=(string)JObject.Parse(before[h])["baseRevision"] && Base(h)==Catalog().FindRoom(Template(h)).revision &&
                        Base(h)==WorkshopBuild.Revision(path),"document moved to the saved revision "+h);
            }
            Debug.Log($"[ProjectRoomRebaseCheck] saving {saved} moved its room documents");
        }

        steps.Enqueue(()=>Save(roomPath,t=>new GameObject(Marker).transform.SetParent(Find(t,"DecorateObject"),false)));
        steps.Enqueue(()=>{
            Moved(roomPath);
            Save(nested,t=>new GameObject(Marker+" Nested").transform.SetParent(t,false));
        });
        steps.Enqueue(()=>{
            Moved(nested);
            // 실패 경로: 문서가 이 표식을 지운 배치로 가리키게 한 뒤 표식을 원본에서 없앤다.
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(roomPath).transform;
            var entry=new WorkshopRoom.Piece { id="rebase-check",source=WorkshopCatalog.PathOf(asset,Find(asset,Marker)),removed=true };
            piece=Path.Combine(Path.GetDirectoryName(failing),"OBJECTS",BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(entry.id)).Replace("-","")+".json");
            Directory.CreateDirectory(Path.GetDirectoryName(piece));
            File.WriteAllText(piece,JsonUtility.ToJson(entry,true)+"\n");
            ProjectRooms.ValidateForBuild(Path.GetFileName(Path.GetDirectoryName(failing)),Catalog());
            warnings.Clear();
            Save(roomPath,t=>UnityEngine.Object.DestroyImmediate(Find(t,Marker).gameObject));
        });
        steps.Enqueue(()=>{
            Moved(roomPath,failing);
            Require(Base(failing)!=Catalog().FindRoom(room.Key).revision,"invalid document stays on its old revision");
            Require(warnings.Count==1 && warnings[0].Contains(Path.GetFileName(Path.GetDirectoryName(failing))),"one console warning: "+string.Join(" | ",warnings));
            // 되돌리기: 두 프리팹을 따로 임포트해도 재기준은 한 번이고, 모든 문서가 처음 버전으로 돌아온다.
            File.Delete(piece);
            if(!Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(piece)).Any()) Directory.Delete(Path.GetDirectoryName(piece));
            runs=ProjectRoomRebase.Runs;
            foreach(var source in sources) { File.WriteAllBytes(source.Key,source.Value); AssetDatabase.ImportAsset(source.Key); }
        });
        steps.Enqueue(()=>{
            Require(ProjectRoomRebase.Runs==runs+1,"one rebase for a batch import");
            foreach(string h in headers)
                Require(Base(h)==(string)JObject.Parse(original[h])["baseRevision"] && Base(h)==Catalog().FindRoom(Template(h)).revision,"restored "+h);
            Debug.Log("[ProjectRoomRebaseCheck] PASS");
            EditorApplication.Exit(0);
        });
        EditorApplication.update+=()=>{
            // 저장마다 임포트 → delayCall 재기준이 끝날 때까지 기다린다.
            if(ProjectRoomRebase.Busy || EditorApplication.isUpdating) { idle=0; return; }
            if(++idle<3 || steps.Count==0) return;
            idle=0;
            try { steps.Dequeue()(); }
            catch(Exception e) { Fail(e,sources,original,piece); }
        };
    }

    // 같은 실행의 다음 검사가 바뀐 프리팹·카탈로그를 보지 않게 되돌린 뒤 끝낸다.
    private static void Fail(Exception e,Dictionary<string,byte[]> sources,Dictionary<string,string> original,string piece)
    {
        Debug.LogError("[ProjectRoomRebaseCheck] FAIL "+e);
        ProjectRoomRebase.AutoInBatch=false;
        try
        {
            foreach(var source in sources) { File.WriteAllBytes(source.Key,source.Value); AssetDatabase.ImportAsset(source.Key); }
            foreach(var pair in original) File.WriteAllText(pair.Key,pair.Value);
            if(piece!=null && File.Exists(piece)) File.Delete(piece);
            if(sources.Count>0) WorkshopBuild.Bake();
        }
        catch(Exception cleanup) { Debug.LogError("[ProjectRoomRebaseCheck] cleanup "+cleanup); }
        EditorApplication.Exit(1);
    }

    private static void Require(bool condition,string what)
    {
        if(!condition) throw new InvalidOperationException(what);
    }
}

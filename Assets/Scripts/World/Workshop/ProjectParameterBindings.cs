using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Only a creator scene needs the entire authoring catalog. Ordinary builds reference the edited assets.
[DefaultExecutionOrder(-32000)]
public sealed class ProjectParameterBindings : MonoBehaviour
{
    [Serializable] public sealed class Entry
    {
        public string id,group,title;
        public ScriptableObject source;
        public string Document=>group+"/"+id;
        public string Title=>string.IsNullOrEmpty(title)?source.name:title;
    }
    public Entry[] entries;
    internal static Entry[] CreatorCatalog { get; private set; }
    private static readonly HashSet<ScriptableObject> loaded=new();
    private static readonly Dictionary<string,Entry> byId=new();
    private static readonly Dictionary<ScriptableObject,string> bySource=new();
    internal static string Reference(ScriptableObject source)=>source==null ? "" :
        bySource.TryGetValue(source,out var id) ? id : "(기존 연결)";
    internal static ScriptableObject Resolve(string id,Type type)
    {
        if(string.IsNullOrEmpty(id))return null;
        if(byId.TryGetValue(id,out var entry) && type.IsInstanceOfType(entry.source))return entry.source;
        throw new InvalidDataException(type.Name+" 참조를 찾을 수 없습니다. "+id);
    }
    private static void Index(IEnumerable<Entry> source)
    {
        foreach(var e in source){byId[e.id]=e;bySource[e.source]=e.id;}
    }
    internal static void Bind(Entry[] entries)
    {
        Index(entries);
        ProjectContentVariants.Bind(entries);
        Index(ProjectContentVariants.Entries);
        foreach(var e in entries)Load(e);
        foreach(var e in ProjectContentVariants.Entries)Load(e);
        // 항목이 레지스트리보다 먼저 바인딩될 수 있어 가입·삭제는 모든 문서를 적용한 뒤 한 번에 반영한다.
        Rebuild();
    }
    // 바인딩된 레지스트리. 초안 적용(DraftStore)과 저장도 구조를 바꾼 뒤 이 목록을 다시 계산한다.
    internal static ScriptableObject[] Registries()=>byId.Values.Select(e=>e.source).Where(s=>s is GrowthRegistrySO or ShopRegistrySO).Distinct().ToArray();
    internal static void Rebuild()=>ProjectStructure.Rebuild(Registries());
    internal static ScriptableObject Source(string id)=>id!=null && byId.TryGetValue(id,out var e) ? e.source : null;
    // 실행 중 저장 검증의 시작 아이템: 살아 있는 Growth 값(DATA 적용 뒤).
    internal static HashSet<string> StartingItems()=>new(Registries().OfType<GrowthRegistrySO>().Where(g=>g.startingItem!=null)
        .Select(g=>Reference(g.startingItem)),StringComparer.Ordinal);

    private void Awake()
    {
        if(gameObject.scene.name==WorkshopTrial.SceneName) CreatorCatalog=entries;
        Bind(entries);
    }
    private void OnDestroy()
    {
        if(CreatorCatalog==entries) CreatorCatalog=null;
    }
    private static void Load(Entry e)
    {
        // parameters.json이 없어도 구조 파일(LISTS·MEMBERS·removed.json)만 있는 문서가 있다.
        if(e.source==null || loaded.Contains(e.source) || !Directory.Exists(Path.Combine(ProjectContentStore.Root,e.Document))) return;
        if(ProjectContentStore.TryReadPublished(e.Document,()=>{
            var files=ProjectParameters.Snapshot(e.Document);
            try{ ProjectStructure.Apply(e,files); ProjectParameters.Apply(e.source,ProjectParameters.Values(files)); }
            catch{ ProjectStructure.Apply(e,ProjectStructure.None); throw; }   // 틀린 문서는 구조도 남기지 않는다
        }))loaded.Add(e.source);
    }
    // 미리 적용한 SO를 적용됨으로 표시한다. 그러지 않으면 제작판 시험 씬의 Awake→Bind→Load가 디스크 값으로 다시 덮는다.
    internal static void MarkLoaded(ScriptableObject source){ if(source!=null) loaded.Add(source); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        ProjectParameters.Restore();ProjectStructure.Restore();ProjectContentVariants.Release();loaded.Clear();byId.Clear();bySource.Clear();CreatorCatalog=null;
    }
    internal static void ReloadCheckout()
    {
        ProjectParameters.Restore();ProjectStructure.Restore();ProjectContentVariants.Release();
        loaded.Clear();byId.Clear();bySource.Clear();
        if(CreatorCatalog!=null)Bind(CreatorCatalog);
    }
#if UNITY_EDITOR
    public static string Group(string path)
    {
        if(path.StartsWith("Assets/SOData/",StringComparison.Ordinal))
        {
            string area=path.Split('/')[2];
            return area switch {"Dialogue"=>"DIALOGUE/CAST","Localization"=>"LOCALIZATION/TERMS","Sound"=>"AUDIO/CUES",
                "UI" or "MouseCursor"=>"UI/CONTENT","Map"=>"WORLD/GENERATION", _=>"BALANCE/"+area.ToUpperInvariant()};
        }
        return path switch {
            "Assets/Resources/FeedbackCatalog.asset"=>"PRESENTATION/FEEDBACK",
            "Assets/Resources/CameraSettings.asset"=>"PRESENTATION/CAMERA",
            "Assets/Resources/Settings/StatusBalance.asset"=>"BALANCE/STATUS",
            "Assets/Resources/WorldAtmosphere/Particles.asset"=>"PRESENTATION/PARTICLES",
            "Assets/Resources/WorldAtmosphere/Profiles.asset" or "Assets/Resources/WorldAtmosphere/TerrainRelief.asset"=>"PRESENTATION/ATMOSPHERE",
            "Assets/Resources/UI/UISoundSettings.asset"=>"AUDIO/UI",
            "Assets/Resources/UI/UISkinSettings.asset" or "Assets/Resources/UI/TextMotion.asset"=>"UI/STYLE",
            _=>null};
    }
    internal static Entry EntryFor(string id)
    {
        string path=AssetDatabase.GUIDToAssetPath(id);
        string group=Group(path);
        if(group==null) return null;
        var so=AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        return so!=null && so.GetType().Assembly==typeof(WorkshopRoom).Assembly ? new Entry{id=id,group=group,source=so} : null;
    }
    // strict: 빌드 검증. 아니면 틀린 문서 하나만 경고하고 건너뛴다(다른 판에서 만든 문서).
    public static Entry[] Published(bool strict=false)
    {
        if(!Directory.Exists(ProjectContentStore.Root)) return Array.Empty<Entry>();
        var entries=new List<Entry>();
        var definitions=ProjectContentVariants.Definitions(strict).ToArray();
        List<Entry> Dependencies(Entry domain,Newtonsoft.Json.Linq.JObject values)
        {
            var found=new List<Entry>{domain};
            foreach(var field in ProjectParameters.Fields(domain.source).Where(f=>f.IsReference))
            {
                string id=(string)values[field.path];
                if(string.IsNullOrEmpty(id))continue;
                var definition=Array.Find(definitions,d=>d.id==id);
                var target=EntryFor(definition?.template ?? id);
                if(target==null || !field.type.IsInstanceOfType(target.source))
                    throw new InvalidDataException(domain.Document+"/"+field.path+" · 연결할 콘텐츠를 찾을 수 없습니다. "+id);
                found.Add(target);
            }
            return found;
        }
        // 문서 하나를 검증한 뒤에만 entries에 넣는다.
        void Document(string what,Func<List<Entry>> read)
        {
            try{ entries.AddRange(read()); }
            catch(Exception error) when(!strict && ProjectContentStore.IsDataError(error))
            { Debug.LogWarning("[DATA] "+what+" · "+error.Message+" · 이 문서만 건너뜁니다."); }
        }
        if(definitions.Length>0)
        {
            foreach(var definition in definitions)
                Document(ProjectContentVariants.Folder+"/"+definition.id,()=>{
                    var template=EntryFor(definition.template);
                    if(template==null || !ProjectContentVariants.CanCreate(template))
                        throw new InvalidDataException("콘텐츠의 기반 원본을 확인하세요. "+definition.id);
                    return Dependencies(template,ProjectParameters.Values(definition.files));
                });
        }
        // 문서 = parameters.json·removed.json의 부모, 또는 /LISTS/·/MEMBERS/ 앞. 변형은 위에서 읽었다.
        // WORLD/ 아래(방 removed.json 등)는 다른 저장소가 읽는다. 맵 생성 SO의 그룹 WORLD/GENERATION만 이 바인딩 문서다.
        var documents=new SortedSet<string>(StringComparer.Ordinal);
        foreach(string file in Directory.EnumerateFiles(ProjectContentStore.Root,"*.json",SearchOption.AllDirectories))
        {
            string path=Path.GetRelativePath(ProjectContentStore.Root,file).Replace('\\','/');
            if(path.StartsWith(ProjectContentVariants.Folder+"/",StringComparison.Ordinal) ||
                path.StartsWith("WORLD/",StringComparison.Ordinal) && !path.StartsWith("WORLD/GENERATION/",StringComparison.Ordinal)) continue;
            int cut=path.IndexOf("/LISTS/",StringComparison.Ordinal);
            if(cut<0) cut=path.IndexOf("/MEMBERS/",StringComparison.Ordinal);
            if(cut<0 && Path.GetFileName(path) is "parameters.json" or ProjectStructure.RemovedFile) cut=path.LastIndexOf('/');
            if(cut>0) documents.Add(path.Substring(0,cut));
        }
        bool structured=false;
        foreach(string document in documents)
            Document(document,()=>{
                var entry=EntryFor(document.Substring(document.LastIndexOf('/')+1));
                if(entry==null || entry.Document!=document) throw new InvalidDataException("설정 대상 에셋을 찾을 수 없습니다. "+document);
                var files=ProjectParameters.Snapshot(document);
                var found=Dependencies(entry,ProjectParameters.Values(files));
                foreach(string path in files.Keys.Where(k=>k.StartsWith("MEMBERS/",StringComparison.Ordinal)))
                {
                    var parts=path.Split('/');
                    found.Add((parts.Length==3 ? EntryFor(parts[1]) : null) ?? throw new InvalidDataException(document+"/"+path+" · 가입할 레지스트리를 찾을 수 없습니다."));
                }
                structured|=files.Keys.Any(ProjectStructure.IsStructure);
                return found;
            });
        // 변형 가입과 구조 문서(삭제·가입)는 레지스트리 목록을 다시 계산한다.
        if(definitions.Length>0 || structured) entries.AddRange(All().Where(e=>e.source is GrowthRegistrySO or ShopRegistrySO));
        return entries.GroupBy(e=>e.id).Select(g=>g.First()).ToArray();
    }
    public static Entry[] All()=>AssetDatabase.FindAssets("t:ScriptableObject",new[]{"Assets/SOData","Assets/Resources"})
        .Select(EntryFor).Where(e=>e!=null).OrderBy(e=>e.group,StringComparer.Ordinal).ThenBy(e=>e.source.name,StringComparer.Ordinal).ToArray();
    // 편집 모드 검사가 DATA 값을 적용한 동안. Editor 어셈블리의 저장 필터가 읽는다.
    public static bool EditCheck { get; private set; }
    // 편집 모드 검사를 게임과 같은 DATA 값으로 돌린다. 끝나면 에셋을 원래 값으로 되돌린다.
    public static void WithPublished(Action check)
    {
        if(Application.isPlaying){ check(); return; }
        EditCheck=true;
        try{ Bind(Published(strict:true)); check(); }
        finally{ ProjectParameters.Restore(); ProjectStructure.Restore(); ProjectContentVariants.Release(); loaded.Clear(); byId.Clear(); bySource.Clear(); EditCheck=false; }
    }
    public static bool HasLiveValues(string path)
    {
        if(!(Application.isPlaying || EditCheck) || !path.EndsWith(".asset",StringComparison.Ordinal)) return false;
        var so=AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        return ProjectParameters.Applied(so) || ProjectStructure.Touched(so);
    }
    public static void ValidatePublished()
    {
        var published=Published(strict:true);
        ScriptableObject Registry(string id)=>Array.Find(published,e=>e.id==id)?.source;
        // 편집 모드에는 DATA가 적용돼 있지 않다. 시작 아이템은 Growth 문서 값, 없으면 에셋 값이다.
        var starting=new HashSet<string>(published.Where(e=>e.source is GrowthRegistrySO).Select(e=>
            (string)ProjectParameters.Values(ProjectParameters.Snapshot(e.Document))["startingItem"] ??
            (((GrowthRegistrySO)e.source).startingItem is ItemSO item ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item)) : null)),StringComparer.Ordinal);
        void Check(Entry entry,Newtonsoft.Json.Linq.JObject values,Dictionary<string,string> files)
        {
            ProjectStructure.Validate(entry,files,Registry,starting);
            // 새 원소의 값 경로는 구조를 적용해야 생긴다. 그동안 저장 필터가 이 에셋 저장을 막는다.
            bool held=EditCheck; EditCheck=true;
            try
            {
                ProjectStructure.Apply(entry,files);
                ProjectParameters.RequireKeys(entry.source,entry.Document);
                var fields=ProjectParameters.Fields(entry.source).ToDictionary(f=>f.path);
                foreach(var pair in values)
                {
                    if(!fields.TryGetValue(pair.Key,out var field)) throw new InvalidDataException(entry.Document+"/"+pair.Key);
                    if(field.readOnly || !field.IsReference)field.Parse((string)pair.Value);
                }
            }
            finally{ ProjectStructure.Apply(entry,ProjectStructure.None); EditCheck=held; }
        }
        foreach(var entry in published){ var files=ProjectParameters.Snapshot(entry.Document); Check(entry,ProjectParameters.Values(files),files); }
        foreach(var def in ProjectContentVariants.Definitions(strict:true))
        {
            var template=published.First(e=>e.id==def.template);
            var values=ProjectParameters.Values(def.files);
            if(ProjectParameters.DisplayNameField(template.source) is string name && values.ContainsKey(name))
                throw new InvalidDataException(ProjectContentVariants.Folder+"/"+def.id+" · 새 콘텐츠의 이름은 definition.json title로만 바꿉니다");
            // 변형의 구조 파일은 원본 에셋에 잠시 적용해 검사한다. 삭제 표시·시작 아이템 판정은 변형 id로 한다.
            Check(new Entry{id=def.id,group=ProjectContentVariants.Folder,source=template.source,title=def.title},values,def.files);
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyInEditor()
    {
        var entries=Published();
        Bind(entries);
    }
    [InitializeOnLoadMethod]
    private static void RestoreEditAssets()
    {
        void Restore(){ProjectParameters.Restore();ProjectStructure.Restore();ProjectContentVariants.Release();byId.Clear();bySource.Clear();loaded.Clear();}
        EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingPlayMode) Restore();};
        AssemblyReloadEvents.beforeAssemblyReload+=Restore;
    }
#endif
}

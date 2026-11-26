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
    }

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
        if(e.source==null || loaded.Contains(e.source)) return;
        var path=Path.Combine(ProjectContentStore.Root,e.Document,"parameters.json");
        if(File.Exists(path))
        {
            if(ProjectContentStore.TryReadPublished(e.Document,()=>
                ProjectParameters.Apply(e.source,ProjectParameters.Values(ProjectParameters.Snapshot(e.Document)))))loaded.Add(e.source);
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        ProjectParameters.Restore();ProjectContentVariants.Release();loaded.Clear();byId.Clear();bySource.Clear();CreatorCatalog=null;
    }
    internal static void ReloadCheckout()
    {
        ProjectParameters.Restore();ProjectContentVariants.Release();
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
    private static Entry EntryFor(string id)
    {
        string path=AssetDatabase.GUIDToAssetPath(id);
        string group=Group(path);
        if(group==null) return null;
        var so=AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        return so!=null && so.GetType().Assembly==typeof(WorkshopRoom).Assembly ? new Entry{id=id,group=group,source=so} : null;
    }
    public static Entry[] Published()
    {
        if(!Directory.Exists(ProjectContentStore.Root)) return Array.Empty<Entry>();
        var entries=new List<Entry>();
        var definitions=ProjectContentVariants.Definitions().ToArray();
        void Dependencies(Entry domain,Newtonsoft.Json.Linq.JObject values)
        {
            foreach(var field in ProjectParameters.Fields(domain.source).Where(f=>f.IsReference))
            {
                string id=(string)values[field.path];
                if(string.IsNullOrEmpty(id))continue;
                var definition=Array.Find(definitions,d=>d.id==id);
                var target=EntryFor(definition?.template ?? id);
                if(target==null || !field.type.IsInstanceOfType(target.source))
                    throw new InvalidDataException(domain.Document+"/"+field.path+" · 연결할 콘텐츠를 찾을 수 없습니다. "+id);
                entries.Add(target);
            }
        }
        if(definitions.Length>0)
        {
            entries.AddRange(All().Where(e=>e.source is GrowthRegistrySO or ShopRegistrySO));
            foreach(var definition in definitions)
            {
                var template=EntryFor(definition.template);
                if(template==null || !ProjectContentVariants.CanCreate(template))
                    throw new InvalidDataException("콘텐츠의 기반 원본을 확인하세요. "+definition.id);
                entries.Add(template);
                Dependencies(template,ProjectParameters.Values(definition.files));
            }
        }
        foreach(string file in Directory.EnumerateFiles(ProjectContentStore.Root,"parameters.json",SearchOption.AllDirectories))
        {
            if(definitions.Any(d=>Path.GetFullPath(Path.Combine(ProjectContentStore.Root,ProjectContentVariants.Folder,d.id,"parameters.json"))==Path.GetFullPath(file)))
                continue;
            var entry=EntryFor(Path.GetFileName(Path.GetDirectoryName(file)));
            if(entry==null || Path.GetFullPath(Path.Combine(ProjectContentStore.Root,entry.Document,"parameters.json"))!=Path.GetFullPath(file))
                throw new InvalidDataException("설정 대상 에셋을 찾을 수 없습니다. "+file);
            entries.Add(entry);
            Dependencies(entry,ProjectParameters.Values(ProjectParameters.Snapshot(entry.Document)));
        }
        return entries.GroupBy(e=>e.id).Select(g=>g.First()).ToArray();
    }
    public static Entry[] All()=>AssetDatabase.FindAssets("t:ScriptableObject",new[]{"Assets/SOData","Assets/Resources"})
        .Select(EntryFor).Where(e=>e!=null).OrderBy(e=>e.group,StringComparer.Ordinal).ThenBy(e=>e.source.name,StringComparer.Ordinal).ToArray();
    public static bool HasLiveValues(string path)=>Application.isPlaying && path.EndsWith(".asset",StringComparison.Ordinal) &&
        (ProjectParameters.Applied(AssetDatabase.LoadAssetAtPath<ScriptableObject>(path)) ||
        ProjectContentVariants.Protects(AssetDatabase.LoadAssetAtPath<ScriptableObject>(path)));
    public static void ValidatePublished()
    {
        var published=Published();
        void Check(Entry entry,Newtonsoft.Json.Linq.JObject values)
        {
            ProjectParameters.RequireKeys(entry.source,entry.Document);
            var fields=ProjectParameters.Fields(entry.source).ToDictionary(f=>f.path);
            foreach(var pair in values)
            {
                if(!fields.TryGetValue(pair.Key,out var field)) throw new InvalidDataException(entry.Document+"/"+pair.Key);
                if(field.readOnly || !field.IsReference)field.Parse((string)pair.Value);
            }
        }
        foreach(var entry in published)Check(entry,ProjectParameters.Values(ProjectParameters.Snapshot(entry.Document)));
        foreach(var def in ProjectContentVariants.Definitions())
            Check(published.First(e=>e.id==def.template),ProjectParameters.Values(def.files));
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
        void Restore(){ProjectParameters.Restore();ProjectContentVariants.Release();byId.Clear();bySource.Clear();loaded.Clear();}
        EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingPlayMode) Restore();};
        AssemblyReloadEvents.beforeAssemblyReload+=Restore;
    }
#endif
}

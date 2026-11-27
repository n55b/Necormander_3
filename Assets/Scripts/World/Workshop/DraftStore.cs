using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Entry = ProjectParameterBindings.Entry;

// 문서 경로(entry.Document)마다 basis·초안·실행 취소 스택 하나. 화면을 오가거나 시험·작업 공간 전환을 다녀와도 남는다.
// ProjectContentStore.Root는 작업 공간과 무관하므로 프로젝트 연결(체크아웃 변경) 때만 비운다.
// 초안 = parameters.json 값(values) + 그 밖의 파일(files: definition.json·LISTS·MEMBERS·removed.json).
// live 초안은 바뀔 때마다 살아 있는 SO에 적용한다(구조 → 값 → 변형 이름 → 레지스트리 목록 → 필드 목록).
internal static class DraftStore
{
    internal sealed class Draft
    {
        internal readonly Entry entry;
        internal Dictionary<string,string> basis;        // 마지막으로 읽거나 저장한 문서 파일들
        internal JObject published;                      // basis의 parameters.json
        internal JObject values;                         // 초안 = 저장할 parameters.json 전체
        internal Dictionary<string,string> files;        // 초안의 parameters.json 밖 파일
        internal bool live;                              // 화면이 연 초안: 바뀔 때마다 살아 있는 SO에 바로 적용한다
        internal List<ProjectParameters.Field> fields;   // 구조가 바뀌면 Refields가 다시 만든다
        internal Dictionary<string,ProjectParameters.Field> byPath;
        internal readonly Stack<string> undo=new(), redo=new();
        internal string lastPath;                        // 같은 필드를 연달아 고치면 실행 취소 한 번으로 묶는다
        // 파일 비교는 JSON 값으로 한다. 손으로 고친 파일의 들여쓰기만 달라도 미저장으로 보이지 않게.
        internal bool Dirty=>!JToken.DeepEquals(values,published) || !Same(files,Others(basis));
        internal bool StructureChanged=>files.Keys.Any(ProjectStructure.IsStructure);
        internal string Title=>files.TryGetValue("definition.json",out var json) ? (string)JObject.Parse(json)["title"] : null;
        // 이 판의 SO에 없는 키: 새 판에서 만든 문서이거나 이 판에서 지운 필드가 남은 문서다. 매번 계산하므로 정리·Rebase 뒤 바로 풀린다.
        // 초안 LISTS의 새 원소 값은 그 원소가 아직 SO에 없어도(live가 아닌 미저장 초안) 이 판의 키다. 실제 검증은 저장이 구조를 적용한 뒤 한다.
        internal string[] Unknown
        {
            get
            {
                var added=new HashSet<string>(StringComparer.Ordinal);
                foreach(var pair in files)
                {
                    var parts=pair.Key.Split('/');
                    if(parts.Length!=3 || parts[0]!="LISTS" || !parts[2].EndsWith(".json",StringComparison.Ordinal)) continue;
                    try{ if(JObject.Parse(pair.Value) is var data && data["type"]!=null && data["removed"]?.Value<bool>()!=true) added.Add(parts[1]+"/@"+Uri.EscapeDataString(parts[2].Substring(0,parts[2].Length-5))+"/"); }
                    catch(Exception e) when(ProjectContentStore.IsDataError(e) || e is InvalidCastException){}
                }
                return values.Properties().Select(p=>p.Name).Where(k=>!byPath.ContainsKey(k) && !added.Any(a=>k.StartsWith(a,StringComparison.Ordinal))).ToArray();
            }
        }
        // 이 판이 모르는 파일: 변형의 definition.json과 구조 파일 밖의 것.
        internal string[] UnknownFiles=>files.Keys.Where(k=>!(k=="definition.json" && entry.group==ProjectContentVariants.Folder) && !ProjectStructure.IsStructure(k))
            .OrderBy(k=>k,StringComparer.Ordinal).ToArray();
        internal string Blocked
        {
            get
            {
                var u=Unknown;
                if(u.Length>0) return "이 판에 없는 항목 "+u.Length+"개 ("+u[0]+") · 제작판을 갱신하세요";
                var f=UnknownFiles;
                return f.Length==0 ? null : "이 판에 없는 파일 "+f.Length+"개 ("+f[0]+") · 제작판을 갱신하세요";
            }
        }
        internal string Value(ProjectParameters.Field f)=>values.TryGetValue(f.path,out var v) ? (string)v : ProjectParameters.Original(entry.source,f);
        internal bool Changed(ProjectParameters.Field f)=>values.ContainsKey(f.path);
        internal bool Unsaved(ProjectParameters.Field f)=>!JToken.DeepEquals(values[f.path],published[f.path]);
        internal Draft(Entry entry,Dictionary<string,string> basis){ this.entry=entry; Refields(this); Rebase(basis); }
        internal void Rebase(Dictionary<string,string> files)
        { basis=files; published=ProjectParameters.Values(files); values=(JObject)published.DeepClone(); this.files=Others(files); lastPath=null; }
    }
    private static readonly Dictionary<string,Draft> drafts=new(StringComparer.Ordinal);
    private static readonly List<Draft> previewed=new();
    internal static bool Previewing=>previewed.Count>0;
    internal static IEnumerable<Draft> All=>drafts.Values;
    internal static IEnumerable<Draft> Unsaved=>drafts.Values.Where(d=>d.Dirty);

    private static Dictionary<string,string> Others(Dictionary<string,string> f)=>
        f.Where(p=>p.Key!="parameters.json").ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
    private static bool Same(Dictionary<string,string> a,Dictionary<string,string> b)=>
        a.Count==b.Count && a.All(p=>b.TryGetValue(p.Key,out var other) && (p.Value==other || JToken.DeepEquals(Parse(p.Value),Parse(other))));
    private static JToken Parse(string json){ try{ return JToken.Parse(json); } catch(Newtonsoft.Json.JsonException){ return json; } }
    private static void Refields(Draft d)
    {
        d.fields=ProjectParameters.Fields(d.entry.source,labels:true);
        d.byPath=d.fields.ToDictionary(f=>f.path,StringComparer.Ordinal);
    }

    internal static Draft Open(Entry entry)
    {
        if(drafts.TryGetValue(entry.Document,out var d) && d.entry.source==entry.source)
        {   // 깨끗하면 팀원이 pull한 최신을 다시 읽는다. 화면이 연 초안이면 그 최신을 바로 적용한다.
            if(!d.Dirty){ d.Rebase(ProjectParameters.Snapshot(entry.Document)); if(d.live) Apply(d); }
            return d;
        }
        return drafts[entry.Document]=new Draft(entry,ProjectParameters.Snapshot(entry.Document));
    }
    // 열지 않고 찾는다(목록의 미저장·삭제 표시).
    internal static Draft Find(string document)=>drafts.TryGetValue(document,out var d) ? d : null;
    // 영구 삭제한 변형처럼 SO가 사라진 초안을 되돌리지 않고 버린다.
    internal static void Forget(string document){ if(drafts.Remove(document,out var d)) previewed.Remove(d); }

    // value가 null이거나 원본값과 같으면 키를 지운다(= 원본값으로). 파싱 실패는 예외로 화면에 알린다.
    internal static void Set(Draft d,ProjectParameters.Field f,string value)
    {
        if(value!=null && value!=ProjectParameters.Original(d.entry.source,f)) f.Parse(value); else value=null;
        if(d.lastPath!=f.path){ Push(d); d.lastPath=f.path; }   // Push가 lastPath를 비우므로 그 뒤에 넣는다
        if(value==null) d.values.Remove(f.path); else d.values[f.path]=value;
        if(d.live) Apply(d);
    }
    // 구조 편집(ProjectStructure.AddElement 등)과 여러 값을 한 번에 바꾸는 편집. 실행 취소 한 번이다.
    internal static void Edit(Draft d,Action<Dictionary<string,string>,JObject> change)
    {
        Push(d);
        change(d.files,d.values);
        if(d.live) Apply(d);
    }
    // 변형 이름(definition.json title). 입력칸의 연속 편집은 실행 취소 한 번으로 묶는다.
    internal static void SetTitle(Draft d,string title)
    {
        title=title?.Trim() ?? "";
        if(title.Length is 0 or >64) throw new InvalidDataException("콘텐츠 이름을 1~64자로 입력하세요.");
        if(!d.files.TryGetValue("definition.json",out var json)) throw new InvalidOperationException("이름은 새 콘텐츠에서만 바꿉니다.");
        const string path="definition.json/title";
        if(d.lastPath!=path){ Push(d); d.lastPath=path; }
        var data=JObject.Parse(json); data["title"]=title;
        d.files["definition.json"]=ProjectContentStore.Json(data);
        if(d.live) Apply(d);
    }
    internal static void ResetFields(Draft d,IEnumerable<ProjectParameters.Field> fields)
    { Push(d); foreach(var f in fields) d.values.Remove(f.path); if(d.live) Apply(d); }
    internal static void Replace(Draft d,JObject values){ Push(d); d.values=values; if(d.live) Apply(d); }
    internal static void Revert(Draft d)
    { Push(d); d.values=(JObject)d.published.DeepClone(); d.files=Others(d.basis); if(d.live) Apply(d); }
    // [판 차이 정리]: 이 판에 없는 키와 파일을 초안에서 뺀다. 저장하면 3자 병합이 디스크에서도 지운다. 최신 제작판에서만 쓴다(문구로 알린다).
    internal static void DropUnknown(Draft d)
    {
        Push(d);
        foreach(var k in d.Unknown) d.values.Remove(k);
        foreach(var k in d.UnknownFiles) d.files.Remove(k);
        if(d.live) Apply(d);
    }
    internal static bool Undo(Draft d)=>Swap(d,d.undo,d.redo);
    internal static bool Redo(Draft d)=>Swap(d,d.redo,d.undo);
    internal static string Save(Draft d)
    {
        if(d.Blocked is string reason) throw new InvalidOperationException(reason);
        try{ d.Rebase(ProjectParameters.Save(d.entry,d.basis,d.values,d.files)); }   // 저장 직후 살아 있는 SO에 적용
        catch{ if(d.live) Apply(d); throw; }   // 저장 검증이 저장값으로 되돌렸다. 화면의 초안은 그대로 보인다
        if(d.entry.group==ProjectContentVariants.Folder && d.Title is string title) ProjectContentVariants.Retitle(d.entry,title);
        previewed.Remove(d);
        Refields(d);
        return ProjectParameters.When(d.entry.source);
    }
    // 막힌 문서는 건너뛰고 제목을 돌려준다. 하나 때문에 [모두 저장]·[모두 저장 후 계속]이 통째로 멈추지 않게 한다.
    internal static List<string> SaveAll()
    {
        var skipped=new List<string>();
        foreach(var d in Unsaved.ToArray()) if(d.Blocked!=null) skipped.Add(d.entry.Title); else Save(d);
        return skipped;
    }
    // 화면이 연 초안 하나만 live다. 다른 초안은 저장값으로 돌아간다.
    internal static void Live(Draft d)
    {
        foreach(var other in drafts.Values.Where(o=>o.live && o!=d).ToArray()) Unlive(other);
        d.live=true; Apply(d);
    }
    internal static void Unlive(Draft d){ d.live=false; Apply(d,d.basis,d.published); }
    // 시험 동안 미저장 초안을 살아 있는 SO에 적용한다(구조 포함). 끄면 저장값으로 되돌리고 화면이 연 초안은 다시 적용한다.
    internal static void Preview(bool on)
    {
        if(on)
        {
            foreach(var d in Unsaved.ToArray()){ Apply(d); previewed.Add(d); }
            return;
        }
        foreach(var d in previewed) Apply(d,d.basis,d.published);
        previewed.Clear();
        foreach(var d in drafts.Values.Where(o=>o.live)) Apply(d);
    }
    internal static void Clear()
    {
        foreach(var d in drafts.Values.Where(o=>o.live).ToArray()) Unlive(d);
        Preview(false); drafts.Clear();
    }

    private static void Apply(Draft d)=>Apply(d,d.files,d.values);
    // 구조 → 값 → 변형 이름 → 레지스트리 목록 → 필드 목록. 시험 씬의 바인딩이 되덮지 않게 적용됨으로 표시한다.
    private static void Apply(Draft d,Dictionary<string,string> files,JObject values)
    {
        var source=d.entry.source;
        if(source==null) return;
        try{ ProjectStructure.Apply(d.entry,files); }
        catch(Exception e) when(ProjectContentStore.IsDataError(e))
        {   // 다른 판의 구조 파일: 구조 없이 값만 적용한다(바인딩 Load와 같다). 저장 검증은 그대로 막는다.
            Debug.LogWarning("[DATA] "+d.entry.Document+" · "+e.Message+" · 구조를 적용하지 않습니다.");
            ProjectStructure.Apply(d.entry,ProjectStructure.None);
        }
        ProjectParameters.Apply(source,values);
        if(d.entry.group==ProjectContentVariants.Folder && files.TryGetValue("definition.json",out var json) &&
            (string)JObject.Parse(json)["title"] is string title) ProjectContentVariants.Name(source,title);
        ProjectParameterBindings.Rebuild();
        ProjectParameterBindings.MarkLoaded(source);
        Refields(d);
    }
    private static string Snapshot(Draft d)=>new JObject{["v"]=d.values.DeepClone(),["f"]=JObject.FromObject(d.files)}.ToString(Newtonsoft.Json.Formatting.None);
    private static void Push(Draft d){ d.undo.Push(Snapshot(d)); d.redo.Clear(); d.lastPath=null; }
    private static bool Swap(Draft d,Stack<string> from,Stack<string> to)
    {
        if(from.Count==0) return false;
        to.Push(Snapshot(d));
        var state=JObject.Parse(from.Pop());
        d.values=(JObject)state["v"];
        d.files=((JObject)state["f"]).Properties().ToDictionary(p=>p.Name,p=>(string)p.Value,StringComparer.Ordinal);
        d.lastPath=null;
        if(d.live) Apply(d);
        return true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset(){ drafts.Clear(); previewed.Clear(); }
}

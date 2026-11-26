using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Entry = ProjectParameterBindings.Entry;

// 문서 경로(entry.Document)마다 basis·초안·실행 취소 스택 하나. 화면을 오가거나 시험·작업 공간 전환을 다녀와도 남는다.
// ProjectContentStore.Root는 작업 공간과 무관하므로 프로젝트 연결(체크아웃 변경) 때만 비운다.
internal static class DraftStore
{
    internal sealed class Draft
    {
        internal readonly Entry entry;
        internal Dictionary<string,string> basis;        // 마지막으로 읽거나 저장한 문서 파일들
        internal JObject published;                      // basis의 parameters.json
        internal JObject values;                         // 초안 = 저장할 parameters.json 전체
        internal readonly List<ProjectParameters.Field> fields;
        internal readonly Dictionary<string,ProjectParameters.Field> byPath;
        internal readonly Stack<string> undo=new(), redo=new();
        internal string lastPath;                        // 같은 필드를 연달아 고치면 실행 취소 한 번으로 묶는다
        internal bool Dirty=>!JToken.DeepEquals(values,published);
        // 이 판의 SO에 없는 키: 새 판에서 만든 문서이거나 이 판에서 지운 필드가 남은 문서다. 매번 계산하므로 정리·Rebase 뒤 바로 풀린다.
        internal string[] Unknown=>values.Properties().Select(p=>p.Name).Where(k=>!byPath.ContainsKey(k)).ToArray();
        internal string Blocked { get { var u=Unknown; return u.Length==0 ? null : "이 판에 없는 항목 "+u.Length+"개 ("+u[0]+") · 제작판을 갱신하세요"; } }
        internal string Value(ProjectParameters.Field f)=>values.TryGetValue(f.path,out var v) ? (string)v : ProjectParameters.Original(entry.source,f);
        internal bool Changed(ProjectParameters.Field f)=>values.ContainsKey(f.path);
        internal bool Unsaved(ProjectParameters.Field f)=>!JToken.DeepEquals(values[f.path],published[f.path]);
        internal Draft(Entry entry,Dictionary<string,string> basis)
        {
            this.entry=entry; fields=ProjectParameters.Fields(entry.source,labels:true);
            byPath=fields.ToDictionary(f=>f.path,StringComparer.Ordinal); Rebase(basis);
        }
        internal void Rebase(Dictionary<string,string> files){ basis=files; published=ProjectParameters.Values(files); values=(JObject)published.DeepClone(); lastPath=null; }
    }
    private static readonly Dictionary<string,Draft> drafts=new(StringComparer.Ordinal);
    private static readonly List<Draft> previewed=new();
    internal static bool Previewing=>previewed.Count>0;
    internal static IEnumerable<Draft> All=>drafts.Values;
    internal static IEnumerable<Draft> Unsaved=>drafts.Values.Where(d=>d.Dirty);

    internal static Draft Open(Entry entry)
    {
        if(drafts.TryGetValue(entry.Document,out var d) && d.entry.source==entry.source)
        { if(!d.Dirty) d.Rebase(ProjectParameters.Snapshot(entry.Document)); return d; }   // 깨끗하면 팀원이 pull한 최신을 다시 읽는다
        return drafts[entry.Document]=new Draft(entry,ProjectParameters.Snapshot(entry.Document));
    }
    // value가 null이거나 원본값과 같으면 키를 지운다(= 원본값으로). 파싱 실패는 예외로 화면에 알린다.
    internal static void Set(Draft d,ProjectParameters.Field f,string value)
    {
        if(value!=null && value!=ProjectParameters.Original(d.entry.source,f)) f.Parse(value); else value=null;
        if(d.lastPath!=f.path){ Push(d); d.lastPath=f.path; }   // Push가 lastPath를 비우므로 그 뒤에 넣는다
        if(value==null) d.values.Remove(f.path); else d.values[f.path]=value;
    }
    internal static void ResetFields(Draft d,IEnumerable<ProjectParameters.Field> fields){ Push(d); foreach(var f in fields) d.values.Remove(f.path); }
    internal static void Replace(Draft d,JObject values){ Push(d); d.values=values; }
    internal static void Revert(Draft d)=>Replace(d,(JObject)d.published.DeepClone());
    // [판 차이 정리]: 이 판에 없는 키를 초안에서 뺀다. 저장하면 3자 병합이 디스크에서도 지운다. 최신 제작판에서만 쓴다(문구로 알린다).
    internal static void DropUnknown(Draft d){ Push(d); foreach(var k in d.Unknown) d.values.Remove(k); }
    internal static bool Undo(Draft d)=>Swap(d,d.undo,d.redo);
    internal static bool Redo(Draft d)=>Swap(d,d.redo,d.undo);
    internal static string Save(Draft d)
    {
        if(d.Blocked is string reason) throw new InvalidOperationException(reason);
        d.Rebase(ProjectParameters.Save(d.entry.Document,d.entry.source,d.basis,d.values));   // 저장 직후 살아 있는 SO에 적용
        previewed.Remove(d);
        return ProjectParameters.When(d.entry.source);
    }
    // 막힌 문서는 건너뛰고 제목을 돌려준다. 하나 때문에 [모두 저장]·[모두 저장 후 계속]이 통째로 멈추지 않게 한다.
    internal static List<string> SaveAll()
    {
        var skipped=new List<string>();
        foreach(var d in Unsaved.ToArray()) if(d.Blocked!=null) skipped.Add(d.entry.Title); else Save(d);
        return skipped;
    }
    // 시험 동안 미저장 초안을 살아 있는 SO에 적용한다. 되돌리기는 저장값을 다시 적용하는 것이다(Apply가 이전 덮어쓰기를 먼저 푼다).
    internal static void Preview(bool on)
    {
        if(on)
        {
            foreach(var d in Unsaved)
            { ProjectParameters.Apply(d.entry.source,d.values); previewed.Add(d); ProjectParameterBindings.MarkLoaded(d.entry.source); }
            return;
        }
        foreach(var d in previewed) if(d.entry.source!=null) ProjectParameters.Apply(d.entry.source,d.published);
        previewed.Clear();
    }
    internal static void Clear(){ Preview(false); drafts.Clear(); }
    private static void Push(Draft d){ d.undo.Push(d.values.ToString(Newtonsoft.Json.Formatting.None)); d.redo.Clear(); d.lastPath=null; }
    private static bool Swap(Draft d,Stack<string> from,Stack<string> to)
    {
        if(from.Count==0) return false;
        to.Push(d.values.ToString(Newtonsoft.Json.Formatting.None)); d.values=JObject.Parse(from.Pop()); d.lastPath=null; return true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset(){ drafts.Clear(); previewed.Clear(); }
}

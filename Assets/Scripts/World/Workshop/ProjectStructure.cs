using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Entry=ProjectParameterBindings.Entry;

// DATA 문서의 구조 파일: 목록 원소 추가·삭제·순서(LISTS), 레지스트리 가입(MEMBERS), 삭제 표시(removed.json).
// 적용은 ProjectParameters보다 먼저(새 원소가 있어야 그 값 경로가 생긴다), 복원은 나중이다.
// 레지스트리 목록은 원래 내용(pristine)에서 매번 다시 계산하므로 순서가 DATA로만 정해진다(같은 시드 같은 상점).
internal static class ProjectStructure
{
    internal const string RemovedFile="removed.json";
    internal static readonly IReadOnlyDictionary<string,string> None=new Dictionary<string,string>();
    internal static bool IsStructure(string path)=>path==RemovedFile || path.StartsWith("LISTS/",StringComparison.Ordinal) || path.StartsWith("MEMBERS/",StringComparison.Ordinal);
    internal static bool IsRemoved(IReadOnlyDictionary<string,string> files)=>files.ContainsKey(RemovedFile);

    private sealed class Spec { internal bool removed; internal readonly Dictionary<(string registry,string field),bool> members=new(); }
    private sealed class Line { internal string type; internal int? order; internal bool removed; }
    private static readonly Dictionary<ScriptableObject,Spec> specs=new();                                        // 항목 → 가입·삭제
    private static readonly HashSet<(ScriptableObject asset,ScriptableObject registry,string field)> implicitJoins=new();   // 변형 자동 가입
    private static readonly Dictionary<(ScriptableObject owner,string field),(FieldInfo info,IList list)> listOriginals=new();   // LISTS 이전 목록 참조
    private static readonly Dictionary<IList,object[]> pristine=new();                                            // 레지스트리 목록 원래 내용
    private static readonly HashSet<GrowthRegistrySO> growths=new();

    // 원래 목록: Rebuild가 다시 계산하기 전의 내용. 변형의 가입 판정은 이것으로 한다(원본이 삭제돼도 판정이 같다).
    internal static IList Original(IList list)=>list!=null && pristine.TryGetValue(list,out var kept) ? kept : list;
    internal static void Join(ScriptableObject asset,ScriptableObject registry,string field)=>implicitJoins.Add((asset,registry,field));
    internal static bool Touched(ScriptableObject so)=>so!=null && (listOriginals.Keys.Any(k=>k.owner==so) ||
        ProjectContentVariants.Lists(so).Any(l=>l.list!=null && pristine.ContainsKey(l.list)));

    internal static void Apply(Entry entry,IReadOnlyDictionary<string,string> files)
    {
        var source=entry.source;
        var lists=new Dictionary<string,Dictionary<string,Line>>(StringComparer.Ordinal);
        // 실행 중에는 Validate를 거치지 않는다. 세이브 해석(retired)은 아이템만 있으므로 다른 타입의 삭제 표시는 무시한다(장비 세이브가 풀리지 않게).
        var spec=new Spec{removed=files.ContainsKey(RemovedFile) && source is ItemSO};
        foreach(var pair in files)
        {
            if(!IsStructure(pair.Key) || pair.Key==RemovedFile) continue;
            var (kind,name,id)=Split(pair.Key,entry);
            if(kind=="MEMBERS") { spec.members[(name,id)]=Included(pair.Value,pair.Key); continue; }
            if(!lists.TryGetValue(name,out var lines)) lists[name]=lines=new(StringComparer.Ordinal);
            lines[id]=Read(pair.Value,pair.Key);
        }
        // 원래 원소가 빠지면 ProjectParameters의 원래 값 복원이 그 원소에 닿지 못한다. 구조를 바꾸기 전에 값을 먼저 되돌린다(호출자가 값을 다시 적용한다).
        if((lists.Count>0 || listOriginals.Keys.Any(k=>k.owner==source)) && ProjectParameters.Applied(source)) ProjectParameters.Apply(source,new JObject());
        foreach(var key in listOriginals.Keys.Where(k=>k.owner==source && !lists.ContainsKey(k.field)).ToArray())
        { var held=listOriginals[key]; held.info.SetValue(source,held.list); listOriginals.Remove(key); }
        foreach(var (name,lines) in lists)
        {
            var info=ListField(source.GetType(),name,out var element,entry.Document);
            if(!listOriginals.TryGetValue((source,name),out var held)) listOriginals[(source,name)]=held=(info,(IList)info.GetValue(source));
            var rows=new List<(int order,int index,string id,object value)>();
            var known=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<(held.list?.Count ?? 0);i++)
            {
                var value=held.list[i];
                string id=Key(value);
                Line line=null;
                if(id!=null){ known.Add(id); lines.TryGetValue(id,out line); }
                if(line?.removed==true) continue;
                rows.Add((line?.order ?? i,i,id ?? "",value));
            }
            // 원래 목록에 없는 id: type이 있으면 새 원소, 없으면(세션 복제본에 없는 원소의 순서·삭제) 무시한다.
            foreach(var (id,line) in lines)
            {
                if(known.Contains(id) || line.type==null || line.removed) continue;
                var value=Activator.CreateInstance(Element(element,line.type,entry.Document+"/LISTS/"+name+"/"+id));
                element.GetField("id").SetValue(value,id);
                rows.Add((line.order ?? int.MaxValue,int.MaxValue,id,value));
            }
            var list=(IList)Activator.CreateInstance(info.FieldType);
            foreach(var row in rows.OrderBy(r=>r.order).ThenBy(r=>r.index).ThenBy(r=>r.id,StringComparer.Ordinal)) list.Add(row.value);
            info.SetValue(source,list);
        }
        if(spec.removed || spec.members.Count>0) specs[source]=spec; else specs.Remove(source);
    }

    // 레지스트리마다 Lists(registry) 목록을 다시 만든다. DATA가 그 목록에 아무 영향이 없으면 참조·내용을 그대로 둔다.
    internal static void Rebuild(IEnumerable<ScriptableObject> registries)
    {
        bool Removed(object o)=>o is ScriptableObject so && specs.TryGetValue(so,out var s) && s.removed;
        foreach(var registry in registries)
        {
            var growth=registry as GrowthRegistrySO;   // Growth 목록은 모두 세이브 해석 목록이라 MEMBERS를 따르지 않는다(D2)
            if(growth!=null){ growth.retired.Clear(); growths.Add(growth); }
            string rid=ProjectParameterBindings.Reference(registry);
            foreach(var (field,list) in ProjectContentVariants.Lists(registry))
            {
                if(list==null) continue;
                var key=(rid,field);
                bool? Member(ScriptableObject so)=>growth==null && specs.TryGetValue(so,out var s) && s.members.TryGetValue(key,out var m) ? m : null;
                var joined=implicitJoins.Where(j=>j.registry==registry && j.field==field).Select(j=>j.asset).ToList();
                pristine.TryGetValue(list,out var original);
                if(original==null && joined.Count==0 && !list.Cast<object>().Any(Removed) &&
                    (growth!=null || !specs.Values.Any(s=>s.members.ContainsKey(key)))) continue;
                original ??= pristine[list]=list.Cast<object>().ToArray();
                var element=list.GetType().GetGenericArguments()[0];
                var result=original.Where(o=>!Removed(o) && !(o is ScriptableObject so && Member(so)==false)).ToList();
                var others=growth!=null ? joined : joined.Concat(specs.Where(p=>p.Value.members.TryGetValue(key,out var m) && m).Select(p=>p.Key));
                result.AddRange(others.Where(a=>a!=null && Array.IndexOf(original,a)<0 && !Removed(a) && element.IsInstanceOfType(a) && (Member(a) ?? joined.Contains(a)))
                    .Distinct().OrderBy(a=>ProjectParameterBindings.Reference(a),StringComparer.Ordinal));
                list.Clear();
                foreach(var o in result) list.Add(o);
                if(growth!=null) foreach(var gone in original.Concat(joined).Where(Removed).Cast<ScriptableObject>().Distinct()) growth.retired.Add(gone);
            }
        }
    }

    internal static void Restore()
    {
        foreach(var (list,items) in pristine){ list.Clear(); foreach(var o in items) list.Add(o); }
        foreach(var pair in listOriginals) if(pair.Key.owner!=null) pair.Value.info.SetValue(pair.Key.owner,pair.Value.list);
        foreach(var growth in growths) if(growth!=null) growth.retired.Clear();
        pristine.Clear(); listOriginals.Clear(); specs.Clear(); implicitJoins.Clear(); growths.Clear();
    }

#if UNITY_EDITOR
    // DATA 굽기: 지금 적용한 목록을 에셋의 값으로 남긴다. Restore가 이 대상의 목록을 되돌리지 않는다.
    internal static void Commit(ScriptableObject target)
    {
        foreach(var key in listOriginals.Keys.Where(k=>k.owner==target).ToArray()) listOriginals.Remove(key);
        specs.Remove(target);
    }
    // DATA 굽기 미리보기의 구조 변경 줄.
    internal static IEnumerable<string> Describe(Entry entry,IReadOnlyDictionary<string,string> files,Func<string,ScriptableObject> registryById)
    {
        var lists=new SortedDictionary<string,List<(string id,Line line)>>(StringComparer.Ordinal);
        foreach(var pair in files.Where(p=>IsStructure(p.Key) && p.Key!=RemovedFile).OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            var (kind,name,id)=Split(pair.Key,entry);
            if(kind=="MEMBERS")
            {
                var registry=registryById(name);
                yield return "목록 가입 "+(registry!=null ? registry.name : name)+"/"+id+": "+(Included(pair.Value,pair.Key) ? "켬" : "끔");
                continue;
            }
            if(!lists.TryGetValue(name,out var lines)) lists[name]=lines=new();
            lines.Add((id,Read(pair.Value,pair.Key)));
        }
        foreach(var (name,lines) in lists)
        {
            var info=ListField(entry.source.GetType(),name,out var element,entry.Document);
            string label=DesignerInspectorLabels.Describe(info).label;
            var added=lines.Where(l=>l.line.type!=null && !l.line.removed).Select(l=>Element(element,l.line.type,entry.Document+"/LISTS/"+name+"/"+l.id)).ToArray();
            if(added.Length>0) yield return label+" 추가 "+added.Length+"개 · "+string.Join(", ",added.Select(t=>t.GetCustomAttribute<ItemEffectInfoAttribute>()?.Name ?? t.Name));
            if(lines.Any(l=>l.line.type==null && !l.line.removed && l.line.order!=null)) yield return label+" 순서";
            foreach(var l in lines.Where(l=>l.line.removed)) yield return label+" 삭제 "+l.id;
        }
    }
#endif

    // 저장·빌드 검증. startingItems: 지금 시작 아이템의 id(편집 모드는 Growth 문서 값 또는 에셋 값, 실행 중은 살아 있는 값).
    internal static void Validate(Entry entry,IReadOnlyDictionary<string,string> files,Func<string,ScriptableObject> registryById,ICollection<string> startingItems)
    {
        var source=entry.source;
        foreach(var pair in files)
        {
            if(!IsStructure(pair.Key)) continue;
            string where=entry.Document+"/"+pair.Key;
            if(pair.Key==RemovedFile)
            {
                var data=JObject.Parse(pair.Value);
                if(data.Count!=1 || data["removed"]?.Type!=JTokenType.Boolean || !(bool)data["removed"])
                    throw new InvalidDataException(where+" · 삭제 표시는 {\"removed\":true}입니다");
                if(source is not ItemSO) throw new InvalidDataException(where+" · 이 판은 아이템 삭제만 지원합니다");
                if(startingItems.Contains(entry.id)) throw new InvalidDataException(where+" · 시작 아이템은 삭제할 수 없습니다. 성장 레지스트리의 시작 아이템을 먼저 바꾸세요");
                continue;
            }
            var (kind,name,id)=Split(pair.Key,entry);
            if(kind=="LISTS")
            {
                ListField(source.GetType(),name,out var element,entry.Document);
                if(!Regex.IsMatch(id,"^[0-9a-f]{12,}$")) throw new InvalidDataException(where+" · 원소 id를 확인하세요");
                var line=Read(pair.Value,where);
                if(line.type==null) continue;   // 순서·삭제는 목록에 없는 id도 허용한다(세션 복제본이 원본에서 지운 원소를 가리킬 수 있다)
                Element(element,line.type,where);
                if(line.removed) throw new InvalidDataException(where+" · 새 원소는 삭제 표시 대신 파일을 지웁니다");
                continue;
            }
            var registry=registryById(name);
            if(registry is not (GrowthRegistrySO or ShopRegistrySO)) throw new InvalidDataException(where+" · 가입할 레지스트리를 찾을 수 없습니다");
            var list=Array.Find(ProjectContentVariants.Lists(registry),l=>l.field==id).list;
            if(list==null) throw new InvalidDataException(where+" · 레지스트리에 없는 목록입니다");
            if(registry is GrowthRegistrySO) throw new InvalidDataException(where+" · 세이브 해석 목록은 가입을 바꾸지 않습니다. 삭제 표시를 쓰세요");
            if(!list.GetType().GetGenericArguments()[0].IsInstanceOfType(source)) throw new InvalidDataException(where+" · 이 목록에 넣을 수 없는 종류입니다");
            Included(pair.Value,where);
        }
    }

    // LISTS/<필드>/<id>.json, MEMBERS/<레지스트리 GUID>/<필드>.json
    private static (string kind,string name,string id) Split(string path,Entry entry)
    {
        var parts=path.Split('/');
        if(parts.Length!=3 || parts[1].Length==0 || parts[2].Length<=5 || !parts[2].EndsWith(".json",StringComparison.Ordinal))
            throw new InvalidDataException(entry.Document+"/"+path+" · 알 수 없는 구조 파일입니다");
        return (parts[0],parts[1],parts[2].Substring(0,parts[2].Length-5));
    }
    private static Line Read(string json,string where)
    {
        var line=new Line();
        foreach(var p in JObject.Parse(json).Properties())
            switch(p.Name)
            {
                case "type" when p.Value.Type==JTokenType.String: line.type=(string)p.Value; break;
                case "order" when p.Value.Type==JTokenType.Integer: line.order=(int)p.Value; break;
                case "removed" when p.Value.Type==JTokenType.Boolean: line.removed=(bool)p.Value; break;
                default: throw new InvalidDataException(where+" · 알 수 없는 값 "+p.Name);
            }
        return line;
    }
    private static bool Included(string json,string where)
    {
        var data=JObject.Parse(json);
        if(data.Count!=1 || data["included"]?.Type!=JTokenType.Boolean) throw new InvalidDataException(where+" · 가입 파일은 {\"included\":true|false}입니다");
        return (bool)data["included"];
    }
    private static string Key(object value)=>value?.GetType().GetField("id")?.GetValue(value) as string;
    // SO의 최상위 List<T> 필드만. 원소 타입에 [WorkshopKey] id가 있어야 한다.
    private static bool IsKeyedList(FieldInfo f,out Type element)
    {
        element=f!=null && f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition()==typeof(List<>) ? f.FieldType.GetGenericArguments()[0] : null;
        var key=element?.GetField("id");
        return element!=null && !f.IsInitOnly && !f.IsNotSerialized && (f.IsPublic || f.IsDefined(typeof(SerializeField)) || f.IsDefined(typeof(SerializeReference))) &&
            key!=null && key.FieldType==typeof(string) && key.IsDefined(typeof(WorkshopKeyAttribute));
    }
    private static FieldInfo ListField(Type owner,string name,out Type element,string document)
    {
        FieldInfo f=null;
        for(var t=owner;t!=null && f==null;t=t.BaseType)
            f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
        if(!IsKeyedList(f,out element)) throw new InvalidDataException(document+"/LISTS/"+name+" · 원소 키가 있는 목록 필드만 바꿀 수 있습니다");
        return f;
    }

    // ---- 초안 편집 도우미: files·values만 바꾼다. 살아 있는 SO에 적용하는 일은 DraftStore가 한다.
    private static string ListPath(string list,string id)=>"LISTS/"+list+"/"+id+".json";
    private static IList Current(ScriptableObject target,string list)=>(IList)ListField(target.GetType(),list,out _,target.name).GetValue(target);

    // 새 원소 하나. order는 지금 목록 끝(같은 목록의 기존 order보다 크게). 원래 원소에는 order를 쓰지 않는다(기본값이 인덱스).
    internal static string AddElement(Dictionary<string,string> files,ScriptableObject target,string list,Type type)
    {
        ListField(target.GetType(),list,out var element,target.name);
        Element(element,type.FullName,target.name+"/LISTS/"+list);
        string id=Guid.NewGuid().ToString("N").Substring(0,12);
        int order=Current(target,list)?.Count ?? 0;
        foreach(var pair in files.Where(p=>p.Key.StartsWith("LISTS/"+list+"/",StringComparison.Ordinal)))
            if(Read(pair.Value,pair.Key).order is int o) order=Math.Max(order,o+1);
        files[ListPath(list,id)]=ProjectContentStore.Json(new JObject{["type"]=type.FullName,["order"]=order});
        return id;
    }
    // 지금 살아 있는 목록 순서에서 이웃과 맞바꾸고 모든 원소에 order를 쓴다. 새 원소 파일은 type을 그대로 둔다.
    internal static void MoveElement(Dictionary<string,string> files,ScriptableObject target,string list,string id,int delta)
    {
        var ids=(Current(target,list)?.Cast<object>() ?? Enumerable.Empty<object>()).Select(Key).ToList();
        if(ids.Any(k=>k==null)) throw new InvalidDataException(target.name+"/"+list+" · 목록 키가 비었습니다. 에셋을 열어 다시 저장하세요");
        int from=ids.IndexOf(id),to=from+delta;
        if(from<0 || to<0 || to>=ids.Count) return;
        (ids[from],ids[to])=(ids[to],ids[from]);
        for(int i=0;i<ids.Count;i++)
        {
            string path=ListPath(list,ids[i]);
            var data=files.TryGetValue(path,out var json) ? JObject.Parse(json) : new JObject();
            data["order"]=i;
            files[path]=ProjectContentStore.Json(data);
        }
    }
    // 새 원소는 파일을 지우고, 원래 원소는 삭제 표시를 쓴다. 두 경우 모두 그 원소의 값 키를 지운다.
    // 세션 중 만든 복제본은 원본의 새 원소를 원래 원소로 들고 있다(R26). 원래 목록에 있으면 새 원소 파일이라도 삭제 표시를 써야
    // 이 세션에서 빠지고, 다음 실행(파일 없음 + 없는 id의 삭제 표시는 무시)에서도 같다.
    internal static void RemoveElement(Dictionary<string,string> files,JObject values,ScriptableObject target,string list,string id)
    {
        ListField(target.GetType(),list,out _,target.name);
        string path=ListPath(list,id);
        var original=listOriginals.TryGetValue((target,list),out var held) ? held.list : Current(target,list);
        bool kept=original!=null && original.Cast<object>().Any(o=>Key(o)==id);
        if(!kept && files.TryGetValue(path,out var json) && Read(json,path).type!=null) files.Remove(path);
        else files[path]=ProjectContentStore.Json(new JObject{["removed"]=true});
        string key=list+"/@"+Uri.EscapeDataString(id);
        foreach(string k in values.Properties().Select(p=>p.Name).Where(k=>k==key || k.StartsWith(key+"/",StringComparison.Ordinal)).ToArray()) values.Remove(k);
    }
    // 원래 상태(원래 목록에 있었는지, 변형이면 자동 가입)와 같으면 파일을 두지 않는다.
    internal static void SetMember(Dictionary<string,string> files,ScriptableObject target,ScriptableObject registry,string field,bool included)
    {
        if(registry is not ShopRegistrySO) throw new InvalidOperationException("세이브 해석 목록은 가입을 바꾸지 않습니다. 삭제 표시를 쓰세요");
        var list=Array.Find(ProjectContentVariants.Lists(registry),l=>l.field==field).list ?? throw new InvalidOperationException("레지스트리에 없는 목록입니다: "+field);
        string rid=ProjectParameterBindings.Reference(registry);
        if(!Guid.TryParseExact(rid,"N",out _)) throw new InvalidOperationException("바인딩되지 않은 레지스트리입니다: "+registry.name);
        bool original=Original(list).Contains(target) || implicitJoins.Contains((target,registry,field));
        string path="MEMBERS/"+rid+"/"+field+".json";
        if(included==original) files.Remove(path); else files[path]=ProjectContentStore.Json(new JObject{["included"]=included});
    }
    // 초안 files 기준의 가입 여부(살아 있지 않은 초안의 표 보기): MEMBERS 파일이 있으면 그 값, 없으면 원래 가입.
    internal static bool IsMember(IReadOnlyDictionary<string,string> files,ScriptableObject target,ScriptableObject registry,string field)
    {
        if(files.TryGetValue("MEMBERS/"+ProjectParameterBindings.Reference(registry)+"/"+field+".json",out var json))
            return JObject.Parse(json)["included"]?.Value<bool>()==true;
        var list=Array.Find(ProjectContentVariants.Lists(registry),l=>l.field==field).list;
        return list!=null && Original(list).Contains(target) || implicitJoins.Contains((target,registry,field));
    }
    // 키가 있는 최상위 목록마다 지금 원소와 LISTS 이전 원래 원소의 id(빈 아이템의 삭제 표시용, R27).
    internal static IEnumerable<(string field,string id)> Elements(ScriptableObject owner)
    {
        for(var t=owner.GetType();t!=null && t!=typeof(ScriptableObject);t=t.BaseType)
            foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
            {
                if(!IsKeyedList(f,out _)) continue;
                var items=((IList)f.GetValue(owner))?.Cast<object>() ?? Enumerable.Empty<object>();
                if(listOriginals.TryGetValue((owner,f.Name),out var held) && held.list!=null) items=items.Concat(held.list.Cast<object>());
                foreach(string id in items.Select(Key).Where(k=>!string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal)) yield return (f.Name,id);
            }
    }
    // 바인딩된 엔트리의 참조 필드 중 asset을 가리키는 것. 살아 있는 SO라 에셋 배선과 DATA 덮어쓰기를 함께 잡는다.
    internal static IEnumerable<(string owner,string label,string document,string path)> References(ScriptableObject asset,IEnumerable<Entry> catalog)=>
        catalog.Where(e=>e.source!=null && e.source!=asset).SelectMany(e=>ProjectParameters.Fields(e.source,labels:true)
            .Where(f=>f.IsReference && ReferenceEquals(f.read(),asset)).Select(f=>(e.Title,f.label,e.Document,f.path)));
    private static Type Element(Type element,string name,string where)
    {
        var type=element.Assembly.GetType(name);
        if(type==null || type.IsAbstract || !element.IsAssignableFrom(type) || !type.IsValueType && type.GetConstructor(Type.EmptyTypes)==null)
            throw new InvalidDataException(where+" · 원소 종류를 찾을 수 없습니다: "+name);
        return type;
    }
}

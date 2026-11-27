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
    private static FieldInfo ListField(Type owner,string name,out Type element,string document)
    {
        FieldInfo f=null;
        for(var t=owner;t!=null && f==null;t=t.BaseType)
            f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
        element=f!=null && f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition()==typeof(List<>) ? f.FieldType.GetGenericArguments()[0] : null;
        var key=element?.GetField("id");
        if(element==null || f.IsInitOnly || f.IsNotSerialized || !(f.IsPublic || f.IsDefined(typeof(SerializeField)) || f.IsDefined(typeof(SerializeReference))) ||
            key==null || key.FieldType!=typeof(string) || !key.IsDefined(typeof(WorkshopKeyAttribute)))
            throw new InvalidDataException(document+"/LISTS/"+name+" · 원소 키가 있는 목록 필드만 바꿀 수 있습니다");
        return f;
    }
    private static Type Element(Type element,string name,string where)
    {
        var type=element.Assembly.GetType(name);
        if(type==null || type.IsAbstract || !element.IsAssignableFrom(type) || !type.IsValueType && type.GetConstructor(Type.EmptyTypes)==null)
            throw new InvalidDataException(where+" · 원소 종류를 찾을 수 없습니다: "+name);
        return type;
    }
}

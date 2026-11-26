using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

// 제작실 데이터 화면에 값은 보이지만 DATA로 바꾸지 않는 필드(세이브 이름, 실행 상태 등). 하위 원소까지 막는다.
[AttributeUsage(AttributeTargets.Field)] public sealed class WorkshopReadOnlyAttribute : Attribute {}
// 목록 원소의 숨김 키. DATA 경로가 "목록/@키"로 원소를 가리킨다. 소유 SO의 OnValidate가 ProjectParameters.IssueKeys로 채운다.
[AttributeUsage(AttributeTargets.Field)] public sealed class WorkshopKeyAttribute : Attribute {}

// Project-owned ScriptableObject references use stable IDs. Engine resources retain their serialized wiring.
public static class ProjectParameters
{
    // 세이브·목록 키라 화면에서 뺀다. 표시 이름 허용은 계획 4.1-4(P2)에서 한다.
    private static readonly string[] KeyNames={"id","key","itemName","minionName","equipmentName"};
    // 애니메이터 상태 이름과 패턴 문구 원본은 보이기만 한다.
    private static readonly string[] ReadOnlyPrefixes={"animState_","label_"};

    internal sealed class Field
    {
        internal string path,label;
        internal Type type;
        internal Func<object> read;
        internal Action<object> write;
        internal float min=float.NegativeInfinity,max=float.PositiveInfinity;
        internal bool readOnly;
        internal bool IsReference=>typeof(ScriptableObject).IsAssignableFrom(type);
        internal string Format(object value)=>IsReference ? ProjectParameterBindings.Reference((ScriptableObject)value) :
            Convert.ToString(value,CultureInfo.InvariantCulture);
        internal string Text()=>Format(read());
        internal object Parse(string value)
        {
            if(readOnly) throw new InvalidDataException(path+" 은(는) 읽기 전용입니다.");
            if(IsReference)return ProjectParameterBindings.Resolve(value,type);
            if(type==typeof(string)) return value;
            if(type.IsEnum)
            {
                if(!Enum.IsDefined(type,value)) throw new InvalidDataException("선택 가능한 값: "+string.Join(", ",Enum.GetNames(type)));
                return Enum.Parse(type,value);
            }
            if(type==typeof(bool)) return bool.Parse(value);
            object n=Convert.ChangeType(value,type,CultureInfo.InvariantCulture);
            double number=Convert.ToDouble(n,CultureInfo.InvariantCulture);
            if(double.IsNaN(number)||double.IsInfinity(number)||number<min||number>max) throw new InvalidDataException(label+" 값의 범위를 확인하세요.");
            return n;
        }
    }

    internal static List<Field> Fields(ScriptableObject target)=>Fields(target,out _);
    private static readonly HashSet<string> warnedKeys=new(StringComparer.Ordinal);
    // keyProblems: [WorkshopKey] 원소의 키가 비었거나 중복된 목록 원소 경로. 그 원소는 인덱스 경로로 떨어진다.
    internal static List<Field> Fields(ScriptableObject target,out List<string> keyProblems)
    {
        var fields=new List<Field>();
        keyProblems=new List<string>();
        Traverse(target.GetType(),()=>target,_=>{}, "",0,fields,false,keyProblems);
        foreach(var problem in keyProblems)
            if(warnedKeys.Add(target.name+"/"+problem)) Debug.LogWarning("[DATA] "+target.name+"/"+problem+" · 목록 키가 비었거나 중복돼 순서 번호로 찾습니다.");
        return fields;
    }
    public static void RequireKeys(ScriptableObject target,string document)
    {
        Fields(target,out var problems);
        if(problems.Count>0) throw new InvalidDataException(document+" · 목록 키가 비었거나 중복됩니다. 에셋을 열어 다시 저장하세요: "+string.Join(", ",problems));
    }
    // OnValidate와 코드로 원소를 넣는 에디터 도구가 부른다. 목록 +와 복사가 키까지 복제하므로 비었거나 중복된 키를 새로 발급한다.
    // 불러올 때 발급한 키도 다음 저장에 남도록 소유 에셋을 변경 표시한다. 저장되지 않은 키는 다음 실행에서 바뀐다.
    public static void IssueKeys(UnityEngine.Object owner,IList list)
    {
        if(list==null) return;
        var seen=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<list.Count;i++)
        {
            var value=list[i];
            var key=KeyField(value?.GetType());
            if(key==null || key.GetValue(value) is string id && id.Length>0 && seen.Add(id)) continue;
            string fresh=Guid.NewGuid().ToString("N").Substring(0,12);
            key.SetValue(value,fresh); seen.Add(fresh);
            list[i]=value; // 구조체 원소는 상자 복사본이라 되써야 한다.
#if UNITY_EDITOR
            if(owner!=null) UnityEditor.EditorUtility.SetDirty(owner);
#endif
        }
    }
    private static FieldInfo KeyField(Type type)
    {
        var f=type?.GetField("id");
        return f!=null && f.FieldType==typeof(string) && f.IsDefined(typeof(WorkshopKeyAttribute)) ? f : null;
    }

    private static void Traverse(Type type,Func<object> read,Action<object> write,string prefix,int depth,List<Field> fields,bool locked,List<string> keyProblems)
    {
        for(var t=type;t!=null && t!=typeof(ScriptableObject) && t!=typeof(UnityEngine.Object) && t!=typeof(object);t=t.BaseType)
        foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
        {
            if(f.IsInitOnly || f.IsNotSerialized || f.IsDefined(typeof(HideInInspector)) ||
                (!f.IsPublic && !f.IsDefined(typeof(SerializeField)) && !f.IsDefined(typeof(SerializeReference)))) continue;
            if(Array.IndexOf(KeyNames,f.Name)>=0 || f.Name.EndsWith("Id",StringComparison.Ordinal)) continue;
            bool readOnly=locked || f.IsDefined(typeof(WorkshopReadOnlyAttribute)) || ReadOnlyPrefixes.Any(p=>f.Name.StartsWith(p,StringComparison.Ordinal));
            Func<object> get=()=>f.GetValue(read());
            void Set(object v) { var parent=read(); f.SetValue(parent,v); if(type.IsValueType) write(parent); }
            Add(f.FieldType,get,Set,prefix+f.Name,depth,fields,f,readOnly,keyProblems);
        }
    }

    private static void Add(Type t,Func<object> read,Action<object> write,string path,int depth,List<Field> fields,FieldInfo info,bool readOnly,List<string> keyProblems)
    {
        if(typeof(UnityEngine.Object).IsAssignableFrom(t))
        {
            if(typeof(ScriptableObject).IsAssignableFrom(t) && t.Assembly==typeof(WorkshopRoom).Assembly)
                fields.Add(new Field{type=t,path=path,label=info.Name,read=read,write=write,readOnly=readOnly});
            return;
        }
        if(t.IsEnum || t==typeof(bool) || t==typeof(string) || t==typeof(int) || t==typeof(byte) || t==typeof(float) || t==typeof(double))
        {
            var range=info.GetCustomAttribute<RangeAttribute>();
            fields.Add(new Field{path=path,label=info.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? path,type=t,read=read,write=write,
                min=range?.min ?? info.GetCustomAttribute<MinAttribute>()?.min ?? float.NegativeInfinity,max=range?.max ?? float.PositiveInfinity,readOnly=readOnly});
            return;
        }
        if(depth>=6 || read()==null) return;
        if(read() is IList list)
        {
            var element=t.IsArray ? t.GetElementType() : t.IsGenericType ? t.GetGenericArguments()[0] : null;
            if(element==null || typeof(UnityEngine.Object).IsAssignableFrom(element)) return;
            var ids=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<list.Count;i++)
            {
                int index=i;
                var value=list[i];
                var itemType=value?.GetType() ?? element;
                var id=value==null ? null : itemType.GetField("id")?.GetValue(value) as string;
                bool keyed=!string.IsNullOrEmpty(id) && ids.Add(id);
                if(!keyed && KeyField(value?.GetType())!=null) keyProblems.Add(path+"/"+i.ToString(CultureInfo.InvariantCulture));
                string key=keyed ? "@"+Uri.EscapeDataString(id) : i.ToString(CultureInfo.InvariantCulture);
                Add(typeof(UnityEngine.Object).IsAssignableFrom(element)?element:itemType,
                    ()=>((IList)read())[index],v=>((IList)read())[index]=v,path+"/"+key,depth+1,fields,info,readOnly,keyProblems);
            }
            return;
        }
        var concrete=read().GetType();
        if(concrete.IsSerializable && (concrete.Assembly==typeof(ProjectParameters).Assembly ||
            concrete==typeof(Color) || concrete==typeof(Color32) || concrete==typeof(Vector2) || concrete==typeof(Vector3) ||
            concrete==typeof(Vector2Int) || concrete==typeof(Vector3Int)))
            Traverse(concrete,read,write,path+"/",depth+1,fields,readOnly,keyProblems);
    }

    private static readonly Dictionary<ScriptableObject,Dictionary<string,object>> originals=new();
    internal static bool Applied(ScriptableObject source)=>source!=null && originals.ContainsKey(source);
    internal static string Original(ScriptableObject source,Field field)=>originals.TryGetValue(source,out var values) &&
        values.TryGetValue(field.path,out var original) ? field.Format(original) : field.Text();
    internal static void Apply(ScriptableObject source,JObject values)
    {
        if(values.Count==0 && !Applied(source)) return;
        var fields=Fields(source).ToDictionary(f=>f.path,StringComparer.Ordinal);
        var pending=new Dictionary<Field,object>();
        foreach(var pair in values)
        {
            if(!fields.TryGetValue(pair.Key,out var field) || pair.Value.Type!=JTokenType.String)
                throw new InvalidDataException(source.name+" 에 없는 필드: "+pair.Key);
            pending[field]=field.Parse((string)pair.Value);
        }
        if(originals.TryGetValue(source,out var prior))
            foreach(var p in prior) if(fields.TryGetValue(p.Key,out var f)) f.write(p.Value);
        var baseline=new Dictionary<string,object>();
        foreach(var pair in pending)
        {
            baseline[pair.Key.path]=pair.Key.read();
            pair.Key.write(pair.Value);
        }
        if(baseline.Count==0) originals.Remove(source); else originals[source]=baseline;
    }
    internal static void Restore()
    {
        foreach(var pair in originals.Keys.ToArray()) if(pair!=null) Apply(pair,new JObject());
        originals.Clear();
    }

    internal static JObject Values(Dictionary<string,string> files) =>
        files.TryGetValue("parameters.json",out var json)?JObject.Parse(json):new JObject();
    internal static Dictionary<string,string> Snapshot(string document)=>
        ProjectContentStore.WithDocument(document,(folder,_)=>ProjectContentStore.Read(folder));
    internal static Dictionary<string,string> Save(string document,ScriptableObject target,Dictionary<string,string> basis,JObject edits)
    {
        var next=new Dictionary<string,string>(basis);
        if(edits.Count==0) next.Remove("parameters.json"); else next["parameters.json"]=ProjectContentStore.Json(edits);
        var saved=ProjectContentStore.Save(document,basis,next,merged=>{
            RequireKeys(target,document);
            var fields=Fields(target).ToDictionary(f=>f.path);
            foreach(var p in Values(merged)) fields[p.Key].Parse((string)p.Value);
        });
        Apply(target,Values(saved)); return saved;
    }
}

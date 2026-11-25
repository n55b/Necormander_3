using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Project-owned ScriptableObject references use stable IDs. Engine resources retain their serialized wiring.
internal static class ProjectParameters
{
    internal sealed class Field
    {
        internal string path,label;
        internal Type type;
        internal Func<object> read;
        internal Action<object> write;
        internal float min=float.NegativeInfinity,max=float.PositiveInfinity;
        internal bool IsReference=>typeof(ScriptableObject).IsAssignableFrom(type);
        internal string Format(object value)=>IsReference ? ProjectParameterBindings.Reference((ScriptableObject)value) :
            Convert.ToString(value,CultureInfo.InvariantCulture);
        internal string Text()=>Format(read());
        internal object Parse(string value)
        {
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

    internal static List<Field> Fields(ScriptableObject target)
    {
        var fields=new List<Field>();
        Traverse(target.GetType(),()=>target,_=>{}, "",0,fields);
        return fields;
    }

    private static void Traverse(Type type,Func<object> read,Action<object> write,string prefix,int depth,List<Field> fields)
    {
        for(var t=type;t!=null && t!=typeof(ScriptableObject) && t!=typeof(UnityEngine.Object) && t!=typeof(object);t=t.BaseType)
        foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
        {
            if(f.IsInitOnly || f.IsNotSerialized || f.IsDefined(typeof(HideInInspector)) ||
                (!f.IsPublic && !f.IsDefined(typeof(SerializeField)) && !f.IsDefined(typeof(SerializeReference)))) continue;
            if(f.Name is "id" or "key" or "itemName" or "minionName" or "equipmentName" || f.Name.EndsWith("Id",StringComparison.Ordinal)) continue;
            Func<object> get=()=>f.GetValue(read());
            void Set(object v) { var parent=read(); f.SetValue(parent,v); if(type.IsValueType) write(parent); }
            Add(f.FieldType,get,Set,prefix+f.Name,depth,fields,f);
        }
    }

    private static void Add(Type t,Func<object> read,Action<object> write,string path,int depth,List<Field> fields,FieldInfo info)
    {
        if(typeof(UnityEngine.Object).IsAssignableFrom(t))
        {
            if(typeof(ScriptableObject).IsAssignableFrom(t) && t.Assembly==typeof(WorkshopRoom).Assembly)
                fields.Add(new Field{type=t,path=path,label=info.Name,read=read,write=write});
            return;
        }
        if(t.IsEnum || t==typeof(bool) || t==typeof(string) || t==typeof(int) || t==typeof(byte) || t==typeof(float) || t==typeof(double))
        {
            var range=info.GetCustomAttribute<RangeAttribute>();
            fields.Add(new Field{path=path,label=info.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? path,type=t,read=read,write=write,
                min=range?.min ?? info.GetCustomAttribute<MinAttribute>()?.min ?? float.NegativeInfinity,max=range?.max ?? float.PositiveInfinity});
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
                string key=!string.IsNullOrEmpty(id) && ids.Add(id) ? "@"+Uri.EscapeDataString(id) : i.ToString(CultureInfo.InvariantCulture);
                Add(typeof(UnityEngine.Object).IsAssignableFrom(element)?element:itemType,
                    ()=>((IList)read())[index],v=>((IList)read())[index]=v,path+"/"+key,depth+1,fields,info);
            }
            return;
        }
        var concrete=read().GetType();
        if(concrete.IsSerializable && (concrete.Assembly==typeof(ProjectParameters).Assembly ||
            concrete==typeof(Color) || concrete==typeof(Color32) || concrete==typeof(Vector2) || concrete==typeof(Vector3) ||
            concrete==typeof(Vector2Int) || concrete==typeof(Vector3Int)))
            Traverse(concrete,read,write,path+"/",depth+1,fields);
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
            var fields=Fields(target).ToDictionary(f=>f.path);
            foreach(var p in Values(merged)) fields[p.Key].Parse((string)p.Value);
        });
        Apply(target,Values(saved)); return saved;
    }
}

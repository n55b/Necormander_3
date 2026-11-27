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
    // 세이브·목록 키라 화면에서 뺀다. 아래 표시 이름 표에 있는 원본의 최상위 이름 필드만 연다.
    private static readonly string[] KeyNames={"id","key","itemName","minionName","equipmentName"};
    // 세이브 키가 아닌 표시 이름. 판정은 SO의 실제 타입(minionName은 MinionDataSO 선언이라 선언 타입으로 보면 메인 소환수까지 열린다).
    // 변형의 이름은 definition.json title 하나라 변형에서는 열지 않는다.
    private static readonly (Type owner,string field)[] DisplayNames={(typeof(ItemSO),"itemName"),(typeof(EquipmentSO),"equipmentName"),(typeof(EnemyMinionDataSO),"minionName")};
    internal static string DisplayNameField(ScriptableObject target)=>target==null ? null : Array.Find(DisplayNames,n=>n.owner.IsInstanceOfType(target)).field;
    // 애니메이터 상태 이름과 패턴 문구 원본은 보이기만 한다.
    private static readonly string[] ReadOnlyPrefixes={"animState_","label_"};

    internal sealed class Field
    {
        // labels:true일 때만 한글 label과 help·unit·group·scale을 채운다. 아니면 label은 InspectorName 또는 path다.
        internal string path,label,help,unit,group;
        internal float scale=1f;
        internal Type type;
        internal Func<object> read;
        internal Action<object> write;
        internal float min=float.NegativeInfinity,max=float.PositiveInfinity;
        internal bool readOnly;
        internal bool IsReference=>typeof(ScriptableObject).IsAssignableFrom(type);
        // 그림 참조(Sprite). 값은 WorkshopCatalog.assets의 키 "<텍스처 GUID>:<조각 이름>"이다.
        internal bool IsAsset=>!IsReference && typeof(UnityEngine.Object).IsAssignableFrom(type);
        internal string Format(object value)=>IsReference ? ProjectParameterBindings.Reference((ScriptableObject)value) :
            IsAsset ? WorkshopCatalog.AssetKey((UnityEngine.Object)value) ?? "(기존 연결)" :
            Convert.ToString(value,CultureInfo.InvariantCulture);
        internal string Text()=>Format(read());
        internal object Parse(string value)
        {
            if(readOnly) throw new InvalidDataException(label+" 은(는) 읽기 전용입니다.");
            if(IsReference)return ProjectParameterBindings.Resolve(value,type);
            if(IsAsset) return value.Length==0 ? null : WorkshopCatalog.FindAsset(value,type);
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

    // labels: 표시 사전(DesignerInspectorLabels)으로 한글 라벨을 계산한다. 제작실 화면만 켠다. 적용·바인딩·빌드 검증은 끈다.
    internal static List<Field> Fields(ScriptableObject target,bool labels=false)=>Fields(target,out _,labels);
    private static readonly HashSet<string> warnedKeys=new(StringComparer.Ordinal);
    // keyProblems: [WorkshopKey] 원소의 키가 비었거나 중복된 목록 원소 경로. 그 원소는 인덱스 경로로 떨어진다.
    internal static List<Field> Fields(ScriptableObject target,out List<string> keyProblems,bool labels=false)
    {
        var fields=new List<Field>();
        keyProblems=new List<string>();
        Traverse(target.GetType(),()=>target,_=>{}, "",0,fields,false,keyProblems,labels,null,target);
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

    // 표시 정보(labels:true). label은 상위 필드 라벨을 " · "로 이은 전체 이름, group은 최상위 필드 위의 Header다.
    private sealed class Meta { internal string label,help,unit,group; internal float scale=1f; }

    private static void Traverse(Type type,Func<object> read,Action<object> write,string prefix,int depth,List<Field> fields,bool locked,List<string> keyProblems,bool labels,Meta parent,ScriptableObject root=null)
    {
        string displayName=depth==0 && !ProjectContentVariants.IsVariant(root) ? DisplayNameField(root) : null;
        for(var t=type;t!=null && t!=typeof(ScriptableObject) && t!=typeof(UnityEngine.Object) && t!=typeof(object);t=t.BaseType)
        {
            string header=null;
            foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
            {
                if(labels && f.GetCustomAttributes<HeaderAttribute>().LastOrDefault() is {} h) header=h.header;
                if(f.IsInitOnly || f.IsNotSerialized || f.IsDefined(typeof(HideInInspector)) ||
                    (!f.IsPublic && !f.IsDefined(typeof(SerializeField)) && !f.IsDefined(typeof(SerializeReference)))) continue;
                if(Array.IndexOf(KeyNames,f.Name)>=0 && f.Name!=displayName || f.Name.EndsWith("Id",StringComparison.Ordinal)) continue;
                bool readOnly=locked || f.IsDefined(typeof(WorkshopReadOnlyAttribute)) || ReadOnlyPrefixes.Any(p=>f.Name.StartsWith(p,StringComparison.Ordinal));
                Func<object> get=()=>f.GetValue(read());
                void Set(object v) { var owner=read(); f.SetValue(owner,v); if(type.IsValueType) write(owner); }
                Meta meta=null;
                if(labels)
                {
                    var d=DesignerInspectorLabels.Describe(f);
                    meta=new Meta{label=parent==null ? d.label : parent.label+" · "+d.label,help=d.help,unit=d.unit,scale=d.scale,
                        group=parent==null ? header : parent.group};
                }
                Add(f.FieldType,get,Set,prefix+f.Name,depth,fields,f,readOnly,keyProblems,meta);
            }
        }
    }

    private static Field Make(Type t,string path,string label,Func<object> read,Action<object> write,bool readOnly,Meta meta)=>
        new Field{type=t,path=path,label=meta?.label ?? label,help=meta?.help,unit=meta?.unit,group=meta?.group,scale=meta?.scale ?? 1f,
            read=read,write=write,readOnly=readOnly};

    private static void Add(Type t,Func<object> read,Action<object> write,string path,int depth,List<Field> fields,FieldInfo info,bool readOnly,List<string> keyProblems,Meta meta)
    {
        if(typeof(UnityEngine.Object).IsAssignableFrom(t))
        {
            if(typeof(ScriptableObject).IsAssignableFrom(t) && t.Assembly==typeof(WorkshopRoom).Assembly || t==typeof(Sprite))
                fields.Add(Make(t,path,info.Name,read,write,readOnly,meta));
            return;
        }
        if(t.IsEnum || t==typeof(bool) || t==typeof(string) || t==typeof(int) || t==typeof(byte) || t==typeof(float) || t==typeof(double))
        {
            var range=info.GetCustomAttribute<RangeAttribute>();
            var field=Make(t,path,info.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? path,read,write,readOnly,meta);
            field.min=range?.min ?? info.GetCustomAttribute<MinAttribute>()?.min ?? float.NegativeInfinity; field.max=range?.max ?? float.PositiveInfinity;
            fields.Add(field);
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
                var itemMeta=meta==null ? null : new Meta{label=meta.label+" "+(i+1).ToString(CultureInfo.InvariantCulture),
                    help=meta.help,unit=meta.unit,group=meta.group,scale=meta.scale};
                Add(typeof(UnityEngine.Object).IsAssignableFrom(element)?element:itemType,
                    ()=>((IList)read())[index],v=>((IList)read())[index]=v,path+"/"+key,depth+1,fields,info,readOnly,keyProblems,itemMeta);
            }
            return;
        }
        var concrete=read().GetType();
        if(concrete.IsSerializable && (concrete.Assembly==typeof(ProjectParameters).Assembly ||
            concrete==typeof(Color) || concrete==typeof(Color32) || concrete==typeof(Vector2) || concrete==typeof(Vector3) ||
            concrete==typeof(Vector2Int) || concrete==typeof(Vector3Int)))
            Traverse(concrete,read,write,path+"/",depth+1,fields,readOnly,keyProblems,meta!=null,meta);
    }

    private static readonly Dictionary<ScriptableObject,Dictionary<string,object>> originals=new();
    internal static bool Applied(ScriptableObject source)=>source!=null && originals.ContainsKey(source);
    internal static string Original(ScriptableObject source,Field field)=>originals.TryGetValue(source,out var values) &&
        values.TryGetValue(field.path,out var original) ? field.Format(original) : field.Text();
    private static readonly HashSet<string> skipped=new(StringComparer.Ordinal);
    internal static void Apply(ScriptableObject source,JObject values)
    {
        if(values.Count==0 && !Applied(source)) return;
        var fields=Fields(source).ToDictionary(f=>f.path,StringComparer.Ordinal);
        var pending=new Dictionary<Field,object>();
        foreach(var pair in values)
        {
            try
            {
                if(!fields.TryGetValue(pair.Key,out var field) || pair.Value.Type!=JTokenType.String)
                    throw new InvalidDataException(source.name+" 에 없는 필드: "+pair.Key);
                pending[field]=field.Parse((string)pair.Value);
            }
            catch(Exception error) when(ProjectContentStore.IsDataError(error))
            {   // 다른 판에서 만든 문서: 이 항목만 건너뛰고 나머지는 적용한다. 빌드 검증(ValidatePublished)과 Save 검증은 그대로 막는다.
                if(skipped.Add(source.name+"/"+pair.Key)) Debug.LogWarning("[DATA] "+error.Message+" · 이 항목만 건너뜁니다.");
            }
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
        if(Reflect.TryGetValue(source.GetType(),out var reflect)) reflect.refresh?.Invoke(source);
    }
    // SO 타입별 반영 방법: 즉시 갱신이 필요하면 refresh(Apply 끝에서 부른다), 상태 줄의 반영 시점 문구는 when.
    internal static readonly Dictionary<Type,(string when,Action<ScriptableObject> refresh)> Reflect=new()
    {
        [typeof(StatusBalanceSO)]=("다음 부여부터",null),   // StatusRules가 부여마다 읽는다
    };
    internal static string When(ScriptableObject source)=>source!=null && Reflect.TryGetValue(source.GetType(),out var r) ? r.when : null;
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
            foreach(var p in Values(merged))
            {
                if(!fields.TryGetValue(p.Key,out var f)) throw new InvalidDataException("이 판에 없는 항목 "+p.Key+" · 제작판을 갱신하세요");
                f.Parse((string)p.Value);
            }
        });
        Apply(target,Values(saved)); return saved;
    }
}

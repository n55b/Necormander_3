using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Entry=ProjectParameterBindings.Entry;

internal static class ProjectContentVariants
{
    internal const string Folder="CATALOG/VARIANTS";
    internal sealed class Definition {internal string id,title,template; internal Dictionary<string,string> files;}
    private static readonly Dictionary<string,Entry> live=new();
    private static readonly List<(ScriptableObject registry,IList list,ScriptableObject asset)> memberships=new();
    internal static IEnumerable<Entry> Entries=>live.Values;
    internal static bool Protects(ScriptableObject registry)=>memberships.Any(m=>m.registry==registry);
    // 상태이상 공통 설정은 StatusRules가 하나만 읽는 싱글턴이라 복제하지 않는다.
    internal static bool CanCreate(Entry source)=>source.group!=Folder && (source.source is ItemSO or EquipmentSO or MainMinionDataSO or TreasureSO ||
        source.group.StartsWith("BALANCE/",StringComparison.Ordinal) && !source.group.EndsWith("/REGISTRY",StringComparison.Ordinal)) &&
        source.source is not StatusBalanceSO;
    private static readonly HashSet<string> warned=new(StringComparer.Ordinal);
    // strict: 빌드 검증. 아니면 틀린 정의 하나만 경고하고 건너뛴다.
    internal static IEnumerable<Definition> Definitions(bool strict=false)
    {
        string root=Path.Combine(ProjectContentStore.Root,Folder);
        if(!Directory.Exists(root))yield break;
        foreach(string dir in Directory.GetDirectories(root).OrderBy(f=>f,StringComparer.Ordinal))
        {
            if(TryDefinition(dir,out var definition,out var error)) yield return definition;
            else if(strict) throw error;
            else if(warned.Add(dir)) Debug.LogWarning("[DATA] "+error.Message+" · 이 콘텐츠만 건너뜁니다.");
        }
    }
    private static bool TryDefinition(string dir,out Definition definition,out Exception error)
    {
        definition=null; error=null;
        try
        {
            string id=Path.GetFileName(dir);
            if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("콘텐츠 ID를 확인하세요. "+dir);
            var files=ProjectParameters.Snapshot(Folder+"/"+id);
            if(!files.TryGetValue("definition.json",out var json))throw new InvalidDataException("콘텐츠 정의가 없습니다. "+dir);
            var data=JObject.Parse(json);
            string title=(string)data["title"],template=(string)data["template"];
            if(string.IsNullOrWhiteSpace(title)||title.Length>64||!Guid.TryParseExact(template,"N",out _))
                throw new InvalidDataException("콘텐츠 이름이나 원본을 확인하세요. "+dir);
            definition=new Definition{id=id,title=title,template=template,files=files};
            return true;
        }
        catch(Exception e) when(ProjectContentStore.IsDataError(e)){ error=e; return false; }
    }
    internal static string Create(Entry source,string title)
    {
        title=title.Trim();
        if(!CanCreate(source)||title.Length is 0 or >64)throw new InvalidDataException("콘텐츠 이름을 1~64자로 입력하세요.");
        string id=Guid.NewGuid().ToString("N");
        var files=new Dictionary<string,string>{["definition.json"]=ProjectContentStore.Json(new JObject{["template"]=source.id,["title"]=title})};
        var values=ProjectParameters.Values(ProjectParameters.Snapshot(source.Document));
        if(values.Count>0)files["parameters.json"]=ProjectContentStore.Json(values);
        ProjectContentStore.Save(Folder+"/"+id,new(),files,_=>{});
        return id;
    }
    internal static void Bind(Entry[] sources)
    {
        foreach(var definition in Definitions())
        {
            var template=Array.Find(sources,s=>s.id==definition.template);
            if(template==null)continue;
            if(!CanCreate(template))
            {
                if(warned.Add(definition.id)) Debug.LogWarning("[DATA] 복제할 수 없는 콘텐츠입니다. "+template.Document+" · 이 콘텐츠만 건너뜁니다.");
                continue;
            }
            if(!live.TryGetValue(definition.id,out var instance))
            {
                var copy=UnityEngine.Object.Instantiate(template.source);
                copy.name="content_"+definition.id;
                copy.hideFlags=HideFlags.DontSave;
                switch(copy)
                {
                    case ItemSO item:item.itemName=definition.title;break;
                    case EquipmentSO equipment:equipment.equipmentName=definition.title;break;
                    case MainMinionDataSO minion:minion.minionName=definition.title;break;
                    case TreasureSO treasure:treasure.itemName=definition.title;treasure.localizedItemName=null;break;
                }
                instance=new Entry{id=definition.id,source=copy,group=Folder,title=definition.title};
                live.Add(definition.id,instance);
            }
            foreach(var source in sources)
            {
                void Add(IList list)
                {
                    if(list==null||!list.Contains(template.source)||list.Contains(instance.source))return;
                    list.Add(instance.source);memberships.Add((source.source,list,instance.source));
                }
                if(source.source is GrowthRegistrySO growth)
                {Add(growth.items);Add(growth.equipments);Add(growth.minionDatas);Add(growth.treasures);}
                if(source.source is ShopRegistrySO shop)Add(shop.itemPool);
            }
        }
    }
    internal static void Release()
    {
        foreach(var member in memberships)member.list.Remove(member.asset);
        memberships.Clear();
        foreach(var entry in live.Values)
            if(entry.source!=null)UnityEngine.Object.DestroyImmediate(entry.source);
        live.Clear();
    }
}

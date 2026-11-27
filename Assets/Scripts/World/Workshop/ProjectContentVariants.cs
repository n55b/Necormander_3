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
    // autoJoin: 정의에 없으면 true(옛 정의: 원본이 든 모든 목록에 가입). false면 세이브 해석 목록(Growth)에만 가입하고 상점 진열은 MEMBERS로 정한다.
    internal sealed class Definition {internal string id,title,template; internal bool autoJoin=true; internal Dictionary<string,string> files;}
    private static readonly Dictionary<string,Entry> live=new();
    internal static IEnumerable<Entry> Entries=>live.Values;
    internal static bool IsVariant(ScriptableObject so)=>so!=null && live.Values.Any(e=>e.source==so);
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
            var join=data["autoJoin"];
            if(join!=null && join.Type!=JTokenType.Boolean)throw new InvalidDataException("autoJoin은 true나 false입니다. "+dir);
            definition=new Definition{id=id,title=title,template=template,autoJoin=join==null || (bool)join,files=files};
            return true;
        }
        catch(Exception e) when(ProjectContentStore.IsDataError(e)){ error=e; return false; }
    }
    // 변형도 복제한다. 그 변형의 template·parameters·LISTS를 물려받는다.
    internal static bool CanDuplicate(Entry source)=>CanCreate(source) || source.group==Folder && live.ContainsKey(source.id);
    // 새 정의는 autoJoin:false라 세이브 해석 목록(Growth)에만 자동 가입한다. 상점 진열은 join(MEMBERS), 보상 등장은 reward(ItemSO)로 정한다.
    // blank: 이 타입의 기본값을 모든 편집 가능 필드에 명시 키로 쓰고, 원본의 모든 목록 원소에 삭제 표시를 쓴다(빈 아이템에서 시작).
    internal static string Create(Entry source,string title,bool blank=false,bool? reward=null,(ScriptableObject registry,string field)[] join=null)
    {
        title=title?.Trim() ?? "";
        if(!CanDuplicate(source)||title.Length is 0 or >64)throw new InvalidDataException("콘텐츠 이름을 1~64자로 입력하세요.");
        var snapshot=ProjectParameters.Snapshot(source.Document);
        string template=source.group==Folder ? (string)JObject.Parse(snapshot["definition.json"])["template"] : source.id;
        var files=snapshot.Where(p=>p.Key!="definition.json" && p.Key!=ProjectStructure.RemovedFile && !p.Key.StartsWith("MEMBERS/",StringComparison.Ordinal))
            .ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
        string name=ProjectParameters.DisplayNameField(source.source);
        var values=ProjectParameters.Values(files);
        if(blank)
        {
            values=new JObject();
            var probe=ScriptableObject.CreateInstance(source.source.GetType());
            try
            {
                foreach(var f in ProjectParameters.Fields(probe))
                    if(!f.readOnly && f.path!=name && !f.path.Split('/').Skip(1).Any(p=>p.StartsWith("@",StringComparison.Ordinal) || p.All(char.IsDigit)))
                        values[f.path]=f.Text();
            }
            finally{ UnityEngine.Object.DestroyImmediate(probe); }
            foreach(string key in files.Keys.Where(k=>k.StartsWith("LISTS/",StringComparison.Ordinal)).ToArray()) files.Remove(key);
            // 세션의 원본은 DATA 새 원소까지 들고 있다. 지금 원소와 원래 원소 모두에 삭제 표시를 쓴다(없는 id는 적용에서 무시된다).
            var origin=ProjectParameterBindings.Source(template) ?? source.source;
            foreach(var (field,id) in ProjectStructure.Elements(origin)) files["LISTS/"+field+"/"+id+".json"]=ProjectContentStore.Json(new JObject{["removed"]=true});
        }
        if(name!=null) values.Remove(name);   // 변형의 이름은 title
        if(reward!=null && source.source is ItemSO) values[nameof(ItemSO.availableAsReward)]=reward.Value.ToString();
        if(values.Count>0) files["parameters.json"]=ProjectContentStore.Json(values); else files.Remove("parameters.json");
        foreach(var (registry,field) in join ?? Array.Empty<(ScriptableObject,string)>())
            files["MEMBERS/"+ProjectParameterBindings.Reference(registry)+"/"+field+".json"]=ProjectContentStore.Json(new JObject{["included"]=true});
        files["definition.json"]=ProjectContentStore.Json(new JObject{["template"]=template,["title"]=title,["autoJoin"]=false});
        string created=Guid.NewGuid().ToString("N");
        ProjectContentStore.Save(Folder+"/"+created,new(),files,_=>{});
        return created;
    }
    internal static void Retitle(Entry entry,string title){ entry.title=title; Name(entry.source,title); }
    // 영구 삭제는 Git에 한 번도 커밋되지 않은 새 콘텐츠만(D12). null이면 지울 수 있다.
    internal static string PurgeBlocker(string id,string checkout=null)
    {
        checkout??=ProjectDataConnection.Checkout;
        string folder="DATA/"+Folder+"/"+id;
        string status,tracked;
        try
        {
            if(checkout==null) throw new IOException("연결된 프로젝트가 없습니다.");
            status=ProjectGitReview.Git("status --porcelain --untracked-files=all -- "+folder,checkout);
            tracked=ProjectGitReview.Git("ls-files -- "+folder,checkout);
        }
        catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return "Git 상태를 읽지 못했습니다. Git 체크아웃에서 지우세요. "+e.Message.Trim(); }
        var lines=status.Split('\n').Select(l=>l.TrimEnd('\r')).Where(l=>l.Length>0).ToArray();
        if(lines.Length==0 && tracked.Trim().Length==0) return "지울 파일이 없습니다.";
        if(tracked.Trim().Length>0 || lines.Any(l=>!l.StartsWith("??",StringComparison.Ordinal)))
            return "Git에 올린 적 있는 새 콘텐츠는 동료의 세이브가 들고 있을 수 있어 삭제 표시만 합니다.";
        return null;
    }
    // 폴더를 지우고 체크아웃을 다시 묶는다. 호출한 화면이 DraftStore.Forget과 카탈로그 다시 읽기를 한다(R21).
    internal static void Purge(Entry entry)
    {
        if(entry.group!=Folder) throw new InvalidOperationException("새 콘텐츠만 파일까지 지웁니다.");
        if(PurgeBlocker(entry.id) is string reason) throw new InvalidOperationException(reason);
        string document=Folder+"/"+entry.id;
        ProjectContentStore.Save(document,ProjectParameters.Snapshot(document),new(),_=>{});
        string folder=Path.Combine(ProjectContentStore.Root,document);
        if(Directory.Exists(folder) && !Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Any()) Directory.Delete(folder,true);
        ProjectParameterBindings.ReloadCheckout();
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
                // 원본의 옛 세이브 키를 들고 있으면 원본을 삭제 표시했을 때 그 세이브가 복사본으로 풀린다. 굽기가 새 에셋의 별칭을 따로 쓴다.
                copy.GetType().GetField("formerAssetNames")?.SetValue(copy,new string[0]);
                Name(copy,definition.title);
                instance=new Entry{id=definition.id,source=copy,group=Folder,title=definition.title};
                live.Add(definition.id,instance);
            }
            // 원래 목록으로 판정한다. Bind는 씬마다 다시 오고, 그때 목록은 Rebuild 결과라 삭제·가입 끔인 원본이 빠져 있다.
            foreach(var source in sources)
                foreach(var (field,list) in Lists(source.source))
                    if(ProjectStructure.Original(list)?.Contains(template.source)==true && (definition.autoJoin || source.source is GrowthRegistrySO))
                        ProjectStructure.Join(instance.source,source.source,field);
        }
    }
    // 변형의 표시 이름. 세이브 키(에셋 이름)는 따로 정한다. DATA 굽기도 같은 규칙으로 새 에셋 이름을 쓴다.
    internal static void Name(ScriptableObject copy,string title)
    {
        switch(copy)
        {
            case ItemSO item:item.itemName=title;break;
            case EquipmentSO equipment:equipment.equipmentName=title;break;
            case MainMinionDataSO minion:minion.minionName=title;break;
            case TreasureSO treasure:treasure.itemName=title;treasure.localizedItemName=null;break;
        }
    }
    internal static string Title(ScriptableObject source)=>source switch
        {ItemSO i=>i.itemName, EquipmentSO e=>e.equipmentName, MinionDataSO m=>m.minionName, TreasureSO t=>t.itemName, _=>null};
    // 변형이 원본을 따라 가입하는 레지스트리 목록. DATA 굽기도 같은 목록에 새 에셋을 넣는다.
    internal static (string field,IList list)[] Lists(ScriptableObject registry)=>registry switch
    {
        GrowthRegistrySO g=>new (string,IList)[]{("items",g.items),("equipments",g.equipments),("minionDatas",g.minionDatas),("treasures",g.treasures)},
        ShopRegistrySO s=>new (string,IList)[]{("itemPool",s.itemPool)},
        _=>Array.Empty<(string,IList)>()
    };
    // 레지스트리 목록은 ProjectStructure.Restore가 먼저 원래 내용으로 돌린다.
    internal static void Release()
    {
        foreach(var entry in live.Values)
            if(entry.source!=null)UnityEngine.Object.DestroyImmediate(entry.source);
        live.Clear();
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Entry=ProjectParameterBindings.Entry;

// Tools > DATA 굽기의 본체(창은 Assets/Editor/Map/ProjectDataBakeWindow.cs).
// 원본 수치 문서 <group>/<guid>/parameters.json은 그 .asset에, 새 콘텐츠 CATALOG/VARIANTS/<id>는 원본 옆의 새 .asset에 쓰고
// 그 문서를 지운다. 세이브 키는 에셋 이름이라 새 에셋의 formerAssetNames에 옛 키 content_<id>를 남긴다.
// 구조 문서(LISTS 목록 원소, MEMBERS 레지스트리 가입)도 에셋에 쓴다. 삭제 표시(removed.json)는 세이브 해석으로만 남으므로 굽지 않고 DATA에 둔다.
// 구운 뒤 WithPublished로 다시 적용한 값이 굽기 전 플레이 적용 값과 같은지 비교한다. 굽기 전 DATA는 Git에 커밋돼 있어야 한다.
public static class ProjectDataBake
{
    public sealed class Item
    {
        public string document,title,error;
        public bool variant,selected=true;
        public readonly List<string> changes=new();
        internal Entry entry;   // 원본 수치는 그 에셋, 새 콘텐츠는 복제할 원본
        internal ProjectContentVariants.Definition definition;
        internal JObject values;
        internal Dictionary<string,string> files;   // 문서의 모든 파일(parameters.json·LISTS·MEMBERS·removed.json, 변형은 definition.json까지)
        internal string path;   // 새 콘텐츠의 새 에셋 경로. 미리보기와 굽기가 같은 값을 쓴다(모든 변형 순서로 이름을 예약).
    }

    public static string Blocker(bool requireCleanGit=true)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return "플레이 모드를 끝낸 뒤 실행하세요.";
        if(!ProjectContentStore.Authoring) return "제작실에서 기본 작업 공간으로 돌아온 뒤 실행하세요.";
        return requireCleanGit ? GitBlocker(ProjectDataConnection.Checkout) : null;
    }
    public static string GitBlocker(string checkout)
    {
        string status;
        try{ status=ProjectGitReview.Git("status --porcelain --untracked-files=all -- DATA",checkout); }
        catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return "DATA의 Git 상태를 읽지 못했습니다. Git 체크아웃에서 실행하세요. "+e.Message.Trim(); }
        var lines=status.Split('\n').Select(l=>l.TrimEnd('\r')).Where(l=>l.Length>0).ToArray();
        if(lines.Length==0) return null;
        bool conflict=lines.Any(l=>l.Length>1 && (l[0]=='U' || l[1]=='U' || l.StartsWith("AA",StringComparison.Ordinal) || l.StartsWith("DD",StringComparison.Ordinal)));
        return (conflict ? "DATA에 Git 충돌이 있습니다. 해결해 커밋한 뒤 실행하세요." : "DATA에 커밋하지 않은 변경이 있습니다. 커밋하거나 되돌린 뒤 실행하세요.")+
            "\n"+string.Join("\n",lines.Take(10))+(lines.Length>10 ? "\n…" : "");
    }

    // 편집기 검사(ProjectDataBakeCheck)가 임시 Git 저장소로 영구 삭제 조건을 확인한다. 본체는 ProjectContentVariants.PurgeBlocker.
    public static string PurgeBlocker(string id,string checkout)=>ProjectContentVariants.PurgeBlocker(id,checkout);

    public static List<Item> Plan()
    {
        var items=new List<Item>();
        string root=ProjectContentStore.Root;
        if(!Directory.Exists(root)) return items;
        var definitions=ProjectContentVariants.Definitions().ToArray();
        // 문서 찾기는 바인딩(Published)과 같다. 삭제 표시(removed.json)는 굽지 않으므로 그것만 있는 문서는 목록에 없다.
        foreach(string document in ProjectParameterBindings.Documents())
        {
            Dictionary<string,string> files=null;
            try{ files=ProjectParameters.Snapshot(document); } catch(Exception e) when(ProjectContentStore.IsDataError(e)){}
            if(files!=null && !files.Keys.Any(Bakes)) continue;
            var item=new Item{document=document,title=document};
            items.Add(item);
            Guard(item,()=>{
                var entry=ProjectParameterBindings.EntryFor(document.Substring(document.LastIndexOf('/')+1));
                if(entry==null || entry.Document!=document) throw new InvalidDataException("설정 대상 에셋을 찾을 수 없습니다.");
                item.entry=entry; item.title=entry.source.name;
                item.files=files ?? ProjectParameters.Snapshot(document);
                item.values=ProjectParameters.Values(item.files);
                Describe(item,entry,definitions);
                if(ProjectStructure.IsRemoved(item.files)) item.changes.Add("삭제 표시는 굽지 않습니다(DATA에 남음)");
                foreach(var d in definitions.Where(d=>d.template==entry.id && ProjectStructure.IsRemoved(d.files)))
                    item.changes.Add("삭제 표시된 새 콘텐츠 "+d.title+"은 구운 원본 값을 물려받습니다(기존 세이브의 그 아이템 수치가 바뀜)");
            });
        }
        var taken=Names();
        var all=ProjectParameterBindings.All();
        foreach(var definition in definitions)
        {
            var item=new Item{document=ProjectContentVariants.Folder+"/"+definition.id,title=definition.title,variant=true,definition=definition};
            items.Add(item);
            Guard(item,()=>{
                // 삭제 표시는 세이브 해석으로만 남는다. 구우면 그 해석(retired)을 잃으므로 고를 수 없다.
                if(ProjectStructure.IsRemoved(definition.files)) throw new InvalidDataException("삭제 표시된 새 콘텐츠는 굽지 않습니다");
                var template=ProjectParameterBindings.EntryFor(definition.template);
                if(template==null || !ProjectContentVariants.CanCreate(template)) throw new InvalidDataException("콘텐츠의 기반 원본을 확인하세요.");
                item.entry=template; item.files=definition.files; item.values=ProjectParameters.Values(definition.files);
                string path=item.path=NewPath(template,definition.title,taken);
                item.changes.Add("새 에셋 "+path+" · 복제 원본 "+template.source.name);
                item.changes.Add("세이브 키 "+Path.GetFileNameWithoutExtension(path)+" · 옛 키 content_"+definition.id+" 별칭");
                // 자동 가입: 옛 정의(autoJoin 없음)는 원본의 모든 목록, autoJoin:false는 세이브 해석 목록(Growth)만. 상점 진열은 MEMBERS 줄이 정한다.
                var members=definition.files.Keys.Where(k=>k.StartsWith("MEMBERS/",StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
                foreach(var m in Memberships(all,template.source))
                    if((definition.autoJoin || m.registry is GrowthRegistrySO) && !members.Contains("MEMBERS/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m.registry))+"/"+m.field+".json"))
                        item.changes.Add("목록 가입 "+m.registry.name+"/"+m.field);
                Describe(item,new Entry{id=definition.id,group=ProjectContentVariants.Folder,source=template.source,title=definition.title},definitions);
                foreach(string referrer in Referrers(definition.id)) item.changes.Add("참조를 새 에셋으로 교체 "+referrer);
            });
        }
        return items;
    }
    private static void Guard(Item item,Action read)
    {
        try{ read(); }
        catch(Exception e) when(ProjectContentStore.IsDataError(e)){ item.error=e.Message; item.selected=false; }
    }
    private static bool Bakes(string file)=>file=="parameters.json" || file.StartsWith("LISTS/",StringComparison.Ordinal) || file.StartsWith("MEMBERS/",StringComparison.Ordinal);
    // 새 원소의 값 키(effects/@<새 id>/value)는 구조를 적용해야 경로가 생긴다. 적용은 이 안에서만 하고 끝에 되돌린다.
    private static void Describe(Item item,Entry entry,ProjectContentVariants.Definition[] definitions)
    {
        ProjectParameterBindings.WithStructure(entry,item.files,()=>{
            var fields=ProjectParameters.Fields(entry.source,labels:true).ToDictionary(f=>f.path,StringComparer.Ordinal);
            foreach(var pair in item.values)
            {
                if(!fields.TryGetValue(pair.Key,out var field)) throw new InvalidDataException("이 판에 없는 항목 "+pair.Key);
                string data=(string)pair.Value, shown=data;
                if(field.IsReference)
                    shown=data.Length==0 ? "(없음)" : Array.Find(definitions,d=>d.id==data) is {} d ? "새 콘텐츠 "+d.title :
                        Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(data)) is {Length:>0} name ? name : data;
                object now=field.read();
                string current=field.IsReference || field.IsAsset ? (now as UnityEngine.Object)?.name ?? "(없음)" : Convert.ToString(now,CultureInfo.InvariantCulture);
                item.changes.Add(field.label+" ("+pair.Key+"): "+current+" → "+shown);
            }
        });
        item.changes.AddRange(ProjectStructure.Describe(entry,item.files,id=>ProjectParameterBindings.EntryFor(id)?.source));
    }
    private static IEnumerable<string> Referrers(string id)
    {
        foreach(string file in Directory.EnumerateFiles(ProjectContentStore.Root,"parameters.json",SearchOption.AllDirectories))
        {
            JObject values;
            try{ values=JObject.Parse(File.ReadAllText(file)); }
            catch(Exception e) when(ProjectContentStore.IsDataError(e)){ continue; }
            foreach(var pair in values)
                if(pair.Value.Type==JTokenType.String && (string)pair.Value==id)
                    yield return Path.GetDirectoryName(Path.GetRelativePath(ProjectContentStore.Root,file)).Replace('\\','/')+"/"+pair.Key;
        }
    }
    private static IEnumerable<(ScriptableObject registry,string field,System.Collections.IList list)> Memberships(Entry[] all,ScriptableObject template)
    {
        foreach(var entry in all)
            foreach(var (field,list) in ProjectContentVariants.Lists(entry.source))
                if(list!=null && list.Contains(template)) yield return (entry.source,field,list);
    }
    // 세이브 키 충돌을 피하려고 SOData·Resources 전체의 에셋 이름, 다른 에셋의 옛 키(formerAssetNames), 보물의 이름 키와 겹치지 않게 한다.
    // ResolveSaved*는 현재 이름을 먼저 보므로 겹치면 옛 세이브가 새 에셋으로 넘어간다.
    private static HashSet<string> Names()
    {
        var taken=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string guid in AssetDatabase.FindAssets("t:ScriptableObject",new[]{"Assets/SOData","Assets/Resources"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            taken.Add(Path.GetFileNameWithoutExtension(path));
            var type=AssetDatabase.GetMainAssetTypeAtPath(path);
            if(type==null || type.GetField("formerAssetNames")==null) continue;
            var asset=AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if(type.GetField("formerAssetNames").GetValue(asset) is string[] former) taken.UnionWith(former.Where(n=>!string.IsNullOrEmpty(n)));
            if(asset is TreasureSO treasure && !string.IsNullOrEmpty(treasure.itemName)) taken.Add(treasure.itemName);
        }
        return taken;
    }
    private static string NewPath(Entry template,string title,HashSet<string> taken)
    {
        string folder=Path.GetDirectoryName(AssetDatabase.GetAssetPath(template.source)).Replace('\\','/');
        var invalid=Path.GetInvalidFileNameChars();
        string stem=new string(title.Select(c=>Array.IndexOf(invalid,c)>=0 ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if(stem.Length==0) stem="content";
        string name=stem;
        for(int n=2;taken.Contains(name) || File.Exists(folder+"/"+name+".asset");n++) name=stem+" "+n.ToString(CultureInfo.InvariantCulture);
        taken.Add(name);
        return folder+"/"+name+".asset";
    }

    // 값 비교용 표기. 참조는 에셋 GUID, 변형(실행 중 복제본 또는 구운 새 에셋)은 content_<id>.
    private static string Key(UnityEngine.Object o,Dictionary<string,string> baked)
    {
        if(o==null) return "";
        var live=ProjectContentVariants.Entries.FirstOrDefault(e=>e.source==o);
        if(live!=null) return "content_"+live.id;
        if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string guid,out long _)) return o.name;
        return baked!=null && baked.TryGetValue(guid,out var id) ? "content_"+id : guid;
    }
    private static Dictionary<string,string> Snapshot(ScriptableObject source,Func<UnityEngine.Object,string> key)
    {
        // formerAssetNames는 굽기가 새로 정한다(실행 중 복제본은 원본의 옛 이름을 그대로 들고 있다).
        // 표시 이름 필드는 변형(실행 중 복제본)에는 없고 구운 새 에셋에는 있다. 아래 (표시 이름)이 비교한다.
        string displayName=ProjectParameters.DisplayNameField(source);
        var values=ProjectParameters.Fields(source).Where(f=>!f.path.StartsWith("formerAssetNames/",StringComparison.Ordinal) && f.path!=displayName)
            .ToDictionary(f=>f.path,f=>f.IsReference ? key((UnityEngine.Object)f.read()) : Convert.ToString(f.read(),CultureInfo.InvariantCulture),StringComparer.Ordinal);
        values["(표시 이름)"]=ProjectContentVariants.Title(source) ?? "";
        return values;
    }
    private static List<string> Members(IEnumerable<ScriptableObject> registries,Func<UnityEngine.Object,string> key)=>
        registries.SelectMany(r=>ProjectContentVariants.Lists(r).Select(l=>r.name+"/"+l.field+": "+
            string.Join(",",l.list.Cast<UnityEngine.Object>().Select(key).OrderBy(k=>k,StringComparer.Ordinal)))).ToList();

    private static object Parse(ProjectParameters.Field field,string value,Dictionary<string,ScriptableObject> created)
    {
        if(!field.IsReference) return field.Parse(value);
        if(field.readOnly) throw new InvalidDataException(field.path+" 은(는) 읽기 전용입니다.");
        if(value.Length==0) return null;
        var target=created.TryGetValue(value,out var made) ? made : AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(value));
        if(target==null || !field.type.IsInstanceOfType(target)) throw new InvalidDataException(field.path+" · 연결할 콘텐츠를 찾을 수 없습니다. "+value);
        return target;
    }
    // 방 굽기(Assets/Editor/Map/ProjectRoomBake.cs)도 이 경로로 문서를 지운다. 비면 폴더까지 지운다.
    public static void Rewrite(string document,Func<Dictionary<string,string>,Dictionary<string,string>> next)
    {
        var files=ProjectParameters.Snapshot(document);
        ProjectContentStore.Save(document,files,next(files),_=>{});
        string folder=Path.Combine(ProjectContentStore.Root,document);
        if(Directory.Exists(folder) && !Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Any()) Directory.Delete(folder,true);
    }

    // documents: Plan()의 Item.document. 계획을 다시 읽어 디스크의 현재 문서를 굽는다.
    public static string Execute(ICollection<string> documents,bool requireCleanGit=true)
    {
        string blocked=Blocker(requireCleanGit);
        if(blocked!=null) throw new InvalidOperationException(blocked);
        ProjectContentStore.RecoverPending();
        var items=Plan().Where(i=>documents.Contains(i.document)).ToList();
        if(items.Count==0) return "구울 문서를 고르지 않았습니다.";
        var broken=items.Where(i=>i.error!=null).ToArray();
        if(broken.Length>0) throw new InvalidDataException("오류가 있는 문서는 구울 수 없습니다.\n"+string.Join("\n",broken.Select(i=>i.document+" · "+i.error)));
        ProjectParameterBindings.ValidatePublished();
        var definitions=ProjectContentVariants.Definitions(strict:true).ToArray();
        var variantIds=definitions.Select(d=>d.id).ToHashSet(StringComparer.Ordinal);
        var baking=items.Where(i=>i.variant).Select(i=>i.definition.id).ToHashSet(StringComparer.Ordinal);
        foreach(var item in items)
            foreach(var pair in item.values)
                if(pair.Value.Type==JTokenType.String && variantIds.Contains((string)pair.Value) && !baking.Contains((string)pair.Value))
                    throw new InvalidDataException(item.title+"/"+pair.Key+" 이(가) 굽지 않는 새 콘텐츠를 참조합니다. 그 콘텐츠도 함께 고르세요. "+(string)pair.Value);
        // 실행 중 새 콘텐츠는 DATA 값이 적용되기 전의 원본 .asset을 복제한다. 원본 수치만 구우면 남은 새 콘텐츠가
        // 자기 parameters에 없는 항목에서 구운 값을 물려받아 바뀌므로 함께 고르게 한다.
        // 원본의 목록 원소(LISTS)·가입(MEMBERS)을 구우면 남은 새 콘텐츠가 원소를 물려받거나 잃고, 자동 가입한 목록도 바뀐다.
        // 삭제 표시된 새 콘텐츠는 고를 수 없으므로 여기서 빼고 미리보기 줄로 알린다.
        foreach(var item in items.Where(i=>!i.variant))
        {
            bool structured=item.files.Keys.Any(k=>k.StartsWith("LISTS/",StringComparison.Ordinal) || k.StartsWith("MEMBERS/",StringComparison.Ordinal));
            foreach(var d in definitions.Where(d=>d.template==item.entry.id && !baking.Contains(d.id) && !ProjectStructure.IsRemoved(d.files)))
            {
                var own=ProjectParameters.Values(d.files);
                if(structured)
                    throw new InvalidDataException(item.title+" 을(를) 원본으로 하는 새 콘텐츠 "+d.title+" 의 목록 원소나 가입이 바뀝니다. 그 콘텐츠도 함께 고르세요. "+d.id);
                // 원본의 표시 이름은 변형이 title로 덮으므로 물려받지 않는다.
                if(item.values.Properties().Any(p=>own[p.Name]==null && p.Name!=ProjectParameters.DisplayNameField(item.entry.source)))
                    throw new InvalidDataException(item.title+" 을(를) 원본으로 하는 새 콘텐츠 "+d.title+" 의 값이 바뀝니다. 그 콘텐츠도 함께 고르세요. "+d.id);
            }
        }

        var all=ProjectParameterBindings.All();
        var registries=all.Select(e=>e.source).Where(s=>s is GrowthRegistrySO or ShopRegistrySO).ToArray();
        Dictionary<string,Dictionary<string,string>> expected=null;
        List<string> expectedMembers=null;
        // 새 에셋이 들어갈 목록: 굽기 전 플레이에서 그 변형 복사본이 든 목록(autoJoin·MEMBERS를 반영한 결과).
        var joins=new Dictionary<string,(ScriptableObject registry,string field)[]>(StringComparer.Ordinal);
        ProjectParameterBindings.WithPublished(()=>{
            string LiveKey(UnityEngine.Object o)=>Key(o,null);
            ScriptableObject Copy(Item i)=>ProjectContentVariants.Entries.FirstOrDefault(e=>e.id==i.definition.id)?.source ?? throw new InvalidDataException("새 콘텐츠를 적용하지 못했습니다. "+i.document);
            expected=items.ToDictionary(i=>i.document,i=>Snapshot(i.variant ? Copy(i) : i.entry.source,LiveKey));
            expectedMembers=Members(registries,LiveKey);
            foreach(var item in items.Where(i=>i.variant))
            {
                var copy=Copy(item);
                joins[item.document]=registries.SelectMany(r=>ProjectContentVariants.Lists(r).Where(l=>l.list!=null && l.list.Contains(copy)).Select(l=>(r,l.field))).ToArray();
            }
        });

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("DATA 굽기");
        var created=new Dictionary<string,ScriptableObject>(StringComparer.Ordinal);   // 변형 id → 새 에셋
        var baked=new Dictionary<string,string>(StringComparer.Ordinal);              // 새 GUID → 변형 id
        // 레지스트리 끝에 새로 넣는 항목. 실행 중 Rebuild와 같은 엔트리 id 순(원본 GUID와 변형 id를 섞어 Ordinal)으로 넣어야 같은 시드의 보상·상점이 같다.
        var additions=new List<(ScriptableObject registry,string field,string id,ScriptableObject asset)>();
        foreach(var item in items.Where(i=>i.variant))
        {
            // 실행 중 복제본처럼 DATA 값이 적용되지 않은 원본 파일을 복사한다(원본 수치는 아래에서 따로 굽는다).
            string path=item.path;
            if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(item.entry.source),path)) throw new IOException("에셋을 복사하지 못했습니다. "+path);
            var asset=AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            ProjectContentVariants.Name(asset,item.definition.title);
            asset.GetType().GetField("formerAssetNames")?.SetValue(asset,new[]{"content_"+item.definition.id});
            EditorUtility.SetDirty(asset);
            foreach(var (registry,field) in joins[item.document]) additions.Add((registry,field,item.definition.id,asset));
            created[item.definition.id]=asset;
            baked[AssetDatabase.AssetPathToGUID(path)]=item.definition.id;
        }
        // 원본의 가입(MEMBERS)을 레지스트리 에셋에 쓴다.
        foreach(var item in items.Where(i=>!i.variant))
            foreach(var pair in item.files.Where(p=>p.Key.StartsWith("MEMBERS/",StringComparison.Ordinal)))
            {
                var parts=pair.Key.Split('/');
                var registry=ProjectParameterBindings.EntryFor(parts[1])?.source ?? throw new InvalidDataException(item.document+"/"+pair.Key+" · 가입할 레지스트리를 찾을 수 없습니다.");
                string field=Path.GetFileNameWithoutExtension(parts[2]);
                var list=Array.Find(ProjectContentVariants.Lists(registry),l=>l.field==field).list ?? throw new InvalidDataException(item.document+"/"+pair.Key+" · 레지스트리에 없는 목록입니다.");
                var source=item.entry.source;
                if((bool)JObject.Parse(pair.Value)["included"]){ if(!list.Contains(source)) additions.Add((registry,field,item.entry.id,source)); continue; }
                Undo.RecordObject(registry,"DATA 굽기");
                while(list.Contains(source)) list.Remove(source);
                EditorUtility.SetDirty(registry);
            }
        foreach(var group in additions.GroupBy(a=>(a.registry,a.field)))
        {
            Undo.RecordObject(group.Key.registry,"DATA 굽기");
            var list=Array.Find(ProjectContentVariants.Lists(group.Key.registry),l=>l.field==group.Key.field).list;
            foreach(var a in group.OrderBy(a=>a.id,StringComparer.Ordinal)) list.Add(a.asset);
            EditorUtility.SetDirty(group.Key.registry);
        }
        // 값을 쓰기 전에 목록 원소(LISTS)를 에셋에 영구 적용한다. 새 원소가 있어야 그 값 경로(effects/@<id>/…)가 Fields에 생긴다.
        foreach(var item in items.Where(i=>i.files.Keys.Any(k=>k.StartsWith("LISTS/",StringComparison.Ordinal))))
        {
            var target=item.variant ? created[item.definition.id] : item.entry.source;
            Undo.RecordObject(target,"DATA 굽기");
            ProjectStructure.Apply(item.variant ? new Entry{id=item.definition.id,group=ProjectContentVariants.Folder,source=target,title=item.definition.title} : item.entry,item.files);
            ProjectStructure.Commit(target);
            EditorUtility.SetDirty(target);
        }
        // 모두 해석한 뒤에 쓴다. 틀린 값 하나로 에셋 일부만 바뀌지 않게 한다.
        var writes=new List<(ScriptableObject target,ProjectParameters.Field field,object value)>();
        foreach(var item in items)
        {
            var target=item.variant ? created[item.definition.id] : item.entry.source;
            var fields=ProjectParameters.Fields(target).ToDictionary(f=>f.path,StringComparer.Ordinal);
            foreach(var pair in item.values)
            {
                if(!fields.TryGetValue(pair.Key,out var field)) throw new InvalidDataException(item.document+" · 이 판에 없는 항목 "+pair.Key);
                writes.Add((target,field,Parse(field,(string)pair.Value,created)));
            }
        }
        foreach(var target in writes.Select(w=>w.target).Distinct()) Undo.RecordObject(target,"DATA 굽기");
        foreach(var w in writes){ w.field.write(w.value); EditorUtility.SetDirty(w.target); }
        AssetDatabase.SaveAssets();

        // 원본은 구운 파일(parameters·LISTS·MEMBERS)만 지우고 삭제 표시는 남긴다. 변형은 폴더 전체.
        foreach(var item in items)
            Rewrite(item.document,files=>item.variant ? new Dictionary<string,string>() : files.Where(p=>!Bakes(p.Key)).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal));
        // 남은 문서가 구운 변형을 가리키면 새 에셋 GUID로 바꾼다.
        var guids=created.ToDictionary(p=>p.Key,p=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p.Value)),StringComparer.Ordinal);
        foreach(string file in Directory.EnumerateFiles(ProjectContentStore.Root,"parameters.json",SearchOption.AllDirectories).ToArray())
        {
            string document=Path.GetDirectoryName(Path.GetRelativePath(ProjectContentStore.Root,file)).Replace('\\','/');
            var values=JObject.Parse(File.ReadAllText(file));
            bool changed=false;
            foreach(var pair in values.Properties().ToArray())
                if(pair.Value.Type==JTokenType.String && guids.TryGetValue((string)pair.Value,out var guid)){ pair.Value=guid; changed=true; }
            if(changed) Rewrite(document,files=>new Dictionary<string,string>(files){["parameters.json"]=ProjectContentStore.Json(values)});
        }

        Dictionary<string,Dictionary<string,string>> actual=null;
        List<string> actualMembers=null;
        ProjectParameterBindings.WithPublished(()=>{
            string BakedKey(UnityEngine.Object o)=>Key(o,baked);
            actual=items.ToDictionary(i=>i.document,i=>Snapshot(i.variant ? created[i.definition.id] : i.entry.source,BakedKey));
            actualMembers=Members(registries,BakedKey);
        });
        var differences=new List<string>();
        foreach(var item in items)
            foreach(string path in expected[item.document].Keys.Union(actual[item.document].Keys))
            {
                expected[item.document].TryGetValue(path,out var before); actual[item.document].TryGetValue(path,out var after);
                if(before!=after) differences.Add(item.title+"/"+path+": 굽기 전 "+before+" · 구운 뒤 "+after);
            }
        differences.AddRange(expectedMembers.Except(actualMembers).Select(m=>"목록이 달라짐 "+m));
        foreach(var item in items.Where(i=>i.variant))
        {
            string alias="content_"+item.definition.id;
            foreach(var growth in registries.OfType<GrowthRegistrySO>())
            {
                var asset=created[item.definition.id];
                UnityEngine.Object resolved=asset switch {ItemSO=>growth.ResolveSavedItem(alias),EquipmentSO=>growth.ResolveSavedEquipment(alias),
                    TreasureSO=>growth.ResolveSavedTreasure(alias),MinionDataSO=>growth.ResolveSavedMinion(alias),_=>asset};
                bool member=ProjectContentVariants.Lists(growth).Any(l=>l.list.Contains(asset));
                if(member && resolved!=asset) differences.Add(item.title+" · 옛 세이브 키 "+alias+" 를 새 에셋으로 해석하지 못함");
            }
        }
        if(differences.Count>0)
            throw new InvalidDataException("구운 값이 굽기 전 플레이 값과 다릅니다. Git으로 DATA와 에셋을 되돌리세요.\n"+string.Join("\n",differences.Take(20)));
        string summary=$"원본 수치 {items.Count(i=>!i.variant)}개, 새 콘텐츠 {created.Count}개를 구웠습니다. 굽기 전 플레이 값과 같습니다.";
        foreach(var asset in created.Values) summary+="\n새 에셋 "+AssetDatabase.GetAssetPath(asset);
        Debug.Log("[DATA 굽기] "+summary);
        return summary;
    }
}
#endif

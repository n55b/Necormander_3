using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class ProjectVillageData
{
    internal const string Document="WORLD/VILLAGES/main";
    internal static Dictionary<string,string> Snapshot()=>ProjectContentStore.WithDocument(Document,(folder,_)=>ProjectContentStore.Read(folder));
    private static string ActorFile(string id)
    {
        if(string.IsNullOrEmpty(id) || id.Length>100 || id.Any(c=>!char.IsLetterOrDigit(c) && c!='-' && c!='_'))
            throw new InvalidDataException("마을 NPC ID를 확인하세요. "+id);
        return "NPC/"+id+".json";
    }
    internal static void Read(WorkshopVillage doc,Dictionary<string,string> files)
    {
        doc.actors=new();
        foreach(var file in files.Where(f=>f.Key.StartsWith("NPC/",StringComparison.Ordinal)))
        {
            var actor=JsonUtility.FromJson<WorkshopVillage.Actor>(file.Value);
            if(ActorFile(actor.id)!=file.Key) throw new InvalidDataException("NPC 파일 경로와 ID가 다릅니다.");
            if(files.TryGetValue("GREETINGS/"+actor.id+".json",out string words)) actor.greeting=(string)JObject.Parse(words)["text"];
            doc.actors.Add(actor);
        }
        doc.useLighting=files.TryGetValue("lighting.json",out string light);
        doc.lighting=doc.useLighting ? JsonUtility.FromJson<LightingPreview>(light) : null;
        doc.Validate();
    }
    internal static Dictionary<string,string> Save(WorkshopVillage doc,Dictionary<string,string> basis)
    {
        var files=new Dictionary<string,string>();
        foreach(var actor in doc.actors)
        {
            var at=JObject.Parse(JsonUtility.ToJson(actor));
            at.Remove("greeting"); files[ActorFile(actor.id)]=ProjectContentStore.Json(at);
            if(!string.IsNullOrEmpty(actor.greeting)) files["GREETINGS/"+actor.id+".json"]=ProjectContentStore.Json(new JObject{["text"]=actor.greeting});
        }
        if(doc.useLighting) files["lighting.json"]=ProjectContentStore.Json(JObject.Parse(JsonUtility.ToJson(doc.lighting)));
        var merged=ProjectContentStore.Save(Document,basis,files,f=>Read(new WorkshopVillage(),f));
        Read(doc,merged); return merged;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded-=Loaded; SceneManager.sceneLoaded+=Loaded;
        SceneManager.sceneUnloaded-=Unloaded; SceneManager.sceneUnloaded+=Unloaded;
        applied=null;
    }
    private static LightingPreview applied, previous;
    private static void Loaded(Scene scene,LoadSceneMode mode)
    {
        if(scene.name!="VillageScene" || WorkshopTrial.Active || !Directory.Exists(Path.Combine(ProjectContentStore.Root,Document))) return;
        var doc=new WorkshopVillage();
        if(!ProjectContentStore.TryReadPublished(Document,()=>Read(doc,Snapshot())))return;
        foreach(var npc in scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<NPCBase>(true)))
        {
            var actor=doc.actors.Find(a=>a.id==npc.WorkshopId);
            if(actor==null || npc.WorkshopActor==null) continue;
            npc.WorkshopActor.position=new Vector3(actor.position.x,actor.position.y,npc.WorkshopActor.position.z);
            npc.WorkshopGreeting=actor.greeting;
            npc.WorkshopActor.gameObject.SetActive(!actor.hidden);
        }
        Physics2D.SyncTransforms();
        if(doc.useLighting) {previous=LightingPreview.Active; LightingPreview.Active=applied=doc.lighting;}
    }
    private static void Unloaded(Scene scene)
    {
        if(scene.name!="VillageScene" || applied==null) return;
        if(LightingPreview.Active==applied) LightingPreview.Active=previous;
        applied=null; previous=null;
    }
}

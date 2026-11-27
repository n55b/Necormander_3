using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

public sealed class ProjectDialogue
{
    internal const string Folder="DIALOGUE/LINES";
    internal readonly string id,locale;
    private Dictionary<string,string> basis;
    internal List<string> order;
    internal Dictionary<string,DialogueLine> lines;
    private string Document=>Folder+"/"+id+"/"+locale;
    private static string Token(string token)
    {
        if(string.IsNullOrWhiteSpace(token)||token.Length>128||token.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'&&c!='-'))
            throw new InvalidDataException("대사 ID에는 글자, 숫자, 밑줄, 붙임표를 사용하세요.");
        return token;
    }
    internal static string[] Ids()
    {
        var root=Path.Combine(ProjectContentStore.Root,Folder);
        return Directory.Exists(root)?Directory.GetDirectories(root).Select(Path.GetFileName).OrderBy(x=>x,StringComparer.Ordinal).ToArray():Array.Empty<string>();
    }
    internal ProjectDialogue(string id,string locale,IEnumerable<DialogueLine> initial=null)
    {
        this.id=Token(id);this.locale=Token(locale);
        basis=ProjectContentStore.WithDocument(Document,(folder,_)=>ProjectContentStore.Read(folder));
        order=new();lines=new();
        if(basis.Count>0) Read(basis);
        else if(initial!=null)
            foreach(var line in initial)
            {
                string key="line-"+(order.Count+1).ToString("D3");
                order.Add(key);lines.Add(key,new DialogueLine{id=id,cast=line.cast,speaker=line.speaker,displayName=line.displayName,text=line.text});
            }
    }
    private void Read(Dictionary<string,string> files)
    {
        if(!files.TryGetValue("order.json",out string json)) throw new InvalidDataException("대사 순서가 없습니다. "+Document);
        var keys=JArray.Parse(json).Values<string>().ToList();
        if(keys.Count>1024 || keys.Distinct().Count()!=keys.Count || keys.Count+1!=files.Count)
            throw new InvalidDataException("대사 줄 수와 순서를 확인하세요. "+Document);
        var content=new Dictionary<string,DialogueLine>();
        foreach(var key in keys)
        {
            var data=JObject.Parse(files[Token(key)+".json"]);
            var text=(string)data["text"] ?? throw new InvalidDataException("대사 본문이 없습니다.");
            if(text.Length>8192) throw new InvalidDataException("대사 한 줄이 너무 깁니다.");
            content.Add(key,new DialogueLine{id=id,cast=(data["cast"] as JArray)?.Values<string>().ToArray(),
                speaker=(string)data["speaker"]??"",displayName=(string)data["displayName"]??"",text=text});
        }
        order=keys;lines=content;
    }
    internal void Save()
    {
        var files=new Dictionary<string,string>{["order.json"]=ProjectContentStore.Json(new JArray(order))};
        foreach(var key in order)
        {
            var line=lines[key];
            files[Token(key)+".json"]=ProjectContentStore.Json(new JObject{["cast"]=line.cast==null ? null : new JArray(line.cast),
                ["speaker"]=line.speaker??"",["displayName"]=line.displayName??"",["text"]=line.text??""});
        }
        var saved=ProjectContentStore.Save(Document,basis,files,f=>{
            var copy=(ProjectDialogue)MemberwiseClone();copy.Read(f);
        });
        Read(saved);basis=saved;
    }
    internal static void Apply(string locale,string fallback,Dictionary<string,List<DialogueLine>> lookup)
    {
        foreach(var id in Ids())
        foreach(var code in new[]{locale,locale.Split('-')[0],fallback}.Distinct())
        {
            if(!File.Exists(Path.Combine(ProjectContentStore.Root,Folder,id,code,"order.json"))) continue;
            if(ProjectContentStore.TryReadPublished(Folder+"/"+id+"/"+code,()=>{
                var doc=new ProjectDialogue(id,code);
                lookup[id]=doc.order.Select(key=>doc.lines[key]).ToList();
            }))break;
        }
    }
    public static void ValidatePublished()
    {
        foreach (string id in Ids())
        foreach (string path in Directory.GetDirectories(Path.Combine(ProjectContentStore.Root,Folder,id)))
            _ = new ProjectDialogue(id,Path.GetFileName(path));
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

public sealed class ProjectTranslations : ITablePostprocessor
{
    internal const string Folder="LOCALIZATION/STRINGS";
    private static readonly Dictionary<StringTableEntry,string> original=new();
    private static readonly HashSet<(StringTable table,long id)> addedEntries=new();
    private static readonly HashSet<(SharedTableData shared,long id)> addedKeys=new();
    private ITablePostprocessor previous;
    private static string Document(StringTable table)=>Folder+"/"+table.SharedData.TableCollectionNameGuid.ToString("N")+"/"+table.LocaleIdentifier.Code;
    private static string FileName(StringTableEntry entry)=>entry.KeyId.ToString(CultureInfo.InvariantCulture)+".json";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        var db=LocalizationSettings.StringDatabase;
        if(db.TablePostprocessor is not ProjectTranslations)
            db.TablePostprocessor=new ProjectTranslations{previous=db.TablePostprocessor};
    }
    public void PostprocessTable(LocalizationTable source)
    {
        previous?.PostprocessTable(source);
        if(source is not StringTable table)return;
        string document=Document(table);
        if(!Directory.Exists(Path.Combine(ProjectContentStore.Root,document)))return;
        ProjectContentStore.TryReadPublished(document,()=>
            Apply(table,ProjectContentStore.WithDocument(document,(folder,_)=>ProjectContentStore.Read(folder))));
    }
    private static long KeyId(string key)
    {
        using var hash=System.Security.Cryptography.SHA256.Create();
        var bytes=hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
        long value=0;
        for(int i=0;i<8;i++)value=unchecked(value*256+bytes[i]);
        return (value & 0x3fffffffffffffffL) | 0x4000000000000000L;
    }
    private static (long id,string key,string text) Record(StringTable table,string file,string json)
    {
        var data=JObject.Parse(json);
        string key=(string)data["key"];
        if(!long.TryParse(Path.GetFileNameWithoutExtension(file),out long id) || file!=id.ToString(CultureInfo.InvariantCulture)+".json" ||
            data["text"]?.Type!=JTokenType.String || ((string)data["text"]).Length>16384)
            throw new InvalidDataException("번역 본문과 파일명을 확인하세요. "+file);
        if(key==null)
        {
            if(table.GetEntry(id)==null)throw new InvalidDataException("번역 키가 없습니다. "+file);
        }
        else if(string.IsNullOrWhiteSpace(key)||key.Length>128||key.IndexOfAny(new[]{'\r','\n'})>=0 ||
            (KeyId(key)!=id && table.SharedData.GetEntry(key)?.Id!=id) ||
            (table.SharedData.GetEntry(id) is { } known && known.Key!=key) ||
            (table.SharedData.GetEntry(key) is { } same && same.Id!=id))
            throw new InvalidDataException("번역 키와 ID가 일치하지 않습니다. "+file);
        return (id,key,(string)data["text"]);
    }
    private static void Apply(StringTable table,Dictionary<string,string> files)
    {
        var records=files.Select(kv=>Record(table,kv.Key,kv.Value)).ToArray();
        foreach(var row in records)
        {
            var entry=table.GetEntry(row.id);
            if(entry==null)
            {
                if(table.SharedData.GetEntry(row.id)==null)
                {
                    table.SharedData.AddKey(row.key,row.id);addedKeys.Add((table.SharedData,row.id));
                }
                table.AddEntry(row.id,row.text);addedEntries.Add((table,row.id));continue;
            }
            if(entry.Value==row.text)continue;
#if UNITY_EDITOR || LEVEL_WORKSHOP
            if(!original.ContainsKey(entry))original.Add(entry,entry.Value);
#endif
            entry.Value=row.text;
        }
    }
    internal static Dictionary<string,string> Snapshot(StringTable table)=>
        ProjectContentStore.WithDocument(Document(table),(folder,_)=>ProjectContentStore.Read(folder));
    internal static void Create(StringTable table,string key,string text)
    {
        key=key.Trim();
        if(table.GetEntry(key)!=null)throw new InvalidDataException("이미 이 언어에 등록된 키입니다.");
        long id=table.SharedData.GetEntry(key)?.Id ?? KeyId(key);
        var basis=Snapshot(table);
        string file=id.ToString(CultureInfo.InvariantCulture)+".json";
        if(basis.ContainsKey(file))throw new InvalidDataException("디스크에 이미 같은 키가 있습니다. 언어 목록을 다시 여세요.");
        SaveRecord(table,basis,file,new JObject{["key"]=key,["text"]=text});
    }
    internal static void Save(StringTable table,StringTableEntry entry,Dictionary<string,string> basis,string text)
    {
        string file=FileName(entry);
        var record=basis.TryGetValue(file,out var saved)?JObject.Parse(saved):new JObject();
        record["text"]=text;
        SaveRecord(table,basis,file,record);
    }
    private static void SaveRecord(StringTable table,Dictionary<string,string> basis,string file,JObject record)
    {
        var updated=new Dictionary<string,string>(basis){[file]=ProjectContentStore.Json(record)};
        var merged=ProjectContentStore.Save(Document(table),basis,updated,files=>{
            foreach(var pair in files)Record(table,pair.Key,pair.Value);
        });
        Apply(table,merged);
    }
    public static bool HasLiveValues(StringTable table)=>table!=null && (table.Values.Any(original.ContainsKey)||addedEntries.Any(e=>e.table==table));
    public static bool HasLiveKeys(SharedTableData shared)=>shared!=null && addedKeys.Any(k=>k.shared==shared);
    private static void RestoreTables()
    {
        foreach(var pair in original)pair.Key.Value=pair.Value;
        original.Clear();
        foreach(var (table,id) in addedEntries)if(table!=null)table.RemoveEntry(id);
        foreach(var (shared,id) in addedKeys)if(shared!=null)shared.RemoveKey(id);
        addedEntries.Clear();addedKeys.Clear();
    }
    internal static void ReloadCheckout()
    {
        RestoreTables();
        if(LocalizationSettings.StringDatabase.TablePostprocessor is ProjectTranslations processor)
            foreach(var table in Resources.FindObjectsOfTypeAll<StringTable>())
                processor.PostprocessTable(table);
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RestoreOnExit()
    {
        UnityEditor.EditorApplication.playModeStateChanged+=state=>{
            if(state==UnityEditor.PlayModeStateChange.ExitingPlayMode)Restore();
        };
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload+=Restore;
    }
    private static void Restore()
    {
        RestoreTables();
        if(LocalizationSettings.HasSettings && LocalizationSettings.StringDatabase.TablePostprocessor is ProjectTranslations ours)
            LocalizationSettings.StringDatabase.TablePostprocessor=ours.previous;
    }
    public static void ValidatePublished()
    {
        string root=Path.Combine(ProjectContentStore.Root,Folder);
        if(!Directory.Exists(root))return;
        var tables=UnityEditor.AssetDatabase.FindAssets("t:StringTable",new[]{"Assets/Settings/Localization"})
            .Select(g=>UnityEditor.AssetDatabase.LoadAssetAtPath<StringTable>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
            .Where(t=>t!=null).ToArray();
        foreach(var path in Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories))
        {
            string locale=Directory.GetParent(path).Name;
            string id=Directory.GetParent(path).Parent.Name;
            var table=tables.FirstOrDefault(t=>t.LocaleIdentifier.Code==locale && t.SharedData.TableCollectionNameGuid.ToString("N")==id);
            if(table==null)throw new InvalidDataException("번역 테이블을 찾을 수 없습니다. "+path);
            Record(table,Path.GetFileName(path),File.ReadAllText(path));
        }
    }
#endif
}

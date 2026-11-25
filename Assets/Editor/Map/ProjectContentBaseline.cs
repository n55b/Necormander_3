using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;

// Run in the verification checkout. Export only missing project records, retaining all published edits.
public static class ProjectContentBaseline
{
    public static void Export()
    {
        if(!Application.isBatchMode || Application.companyName!="NecromanderVerification")
            throw new InvalidOperationException("Use the isolated verification checkout.");
        string output=Environment.GetEnvironmentVariable("NECROMANDER_BASELINE_EXPORT");
        if(string.IsNullOrEmpty(output))throw new InvalidOperationException("Specify a new export directory.");
        if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("The baseline export directory must be empty.");
        void Write(string path,JToken value)
        {
            string target=Path.Combine(output,path);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target,value.ToString(Formatting.Indented).Replace("\r\n","\n")+"\n",new UTF8Encoding(false));
        }
        var catalog=AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath);
        int rooms=0,translations=0;
        foreach(var template in catalog.rooms.Where(t=>t.inPool||t.tutorialOrder>0))
        {
            var doc=WorkshopRoom.Create(template);
            var data=new JObject{["version"]=doc.version,["id"]=template.id,["title"]=template.title,["template"]=template.id,
                ["baseRevision"]=template.revision,["seed"]=doc.seed};
            Write(ProjectRooms.DirectoryName+"/"+template.id+"/room.json",data);rooms++;
        }
        foreach(string guid in AssetDatabase.FindAssets("t:StringTable",new[]{"Assets/Settings/Localization"}))
        {
            var table=AssetDatabase.LoadAssetAtPath<StringTable>(AssetDatabase.GUIDToAssetPath(guid));
            if(table==null)continue;
            string folder="LOCALIZATION/STRINGS/"+table.SharedData.TableCollectionNameGuid.ToString("N")+"/"+table.LocaleIdentifier.Code;
            foreach(var row in table.Values){Write(folder+"/"+row.KeyId+".json",new JObject{["text"]=row.Value??""});translations++;}
        }
        if(rooms==0 || translations==0)throw new InvalidOperationException("No game content found.");
        var movement=new SerializedObject(catalog.playerPrefab.GetComponent<PlayerController>());
        var melee=new SerializedObject(catalog.playerPrefab.GetComponent<MeleeCombatController>());
        JObject Values(SerializedObject source,params string[] keys)
        {
            var obj=new JObject();
            foreach(string key in keys)obj[key]=source.FindProperty(key).floatValue;
            return obj;
        }
        Write("BALANCE/PLAYER/movement.json",Values(movement,"clearedRoomSprint","hurtPushDistance"));
        Write("BALANCE/PLAYER/melee.json",Values(melee,"aimAssistDegrees","reachAssistDistance","attackWalkSpeedMultiplier"));
        Debug.Log($"[ProjectContentBaseline] {rooms} rooms, {translations} translations, 5 player settings");
    }
}

using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// A room prefab edit (or an edit to a prefab it nests) changes the template revision, and every DATA room
// document of that template then fails validation. Rebake the catalog, move each document onto its template's
// current revision and validate its tiles, objects and exits against the new prefab before keeping the change.
public static class ProjectRoomRebase
{
    [MenuItem("Tools/Level Workshop/Rebase project rooms")]
    public static void Run()
    {
        WorkshopBuild.Bake();
        var catalog=AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath);
        string root=Path.Combine(ProjectContentStore.Root,ProjectRooms.DirectoryName);
        int moved=0;
        foreach(string folder in Directory.Exists(root) ? Directory.GetDirectories(root) : Array.Empty<string>())
        {
            string header=Path.Combine(folder,"room.json");
            if(!File.Exists(header)) continue;
            string before=File.ReadAllText(header);
            var json=JObject.Parse(before);
            string revision=catalog.FindRoom((string)json["template"])?.revision ??
                throw new InvalidDataException("원본 방을 카탈로그에서 찾을 수 없습니다. "+folder);
            if((string)json["baseRevision"]==revision) continue;
            json["baseRevision"]=revision;
            File.WriteAllText(header,json.ToString(Formatting.Indented).Replace("\r\n","\n")+"\n");
            try { ProjectRooms.ValidateForBuild(Path.GetFileName(folder),catalog); }
            catch { File.WriteAllText(header,before); throw; }
            moved++;
            Debug.Log($"[Workshop] {json["title"]} ({Path.GetFileName(folder)}) now builds on revision {revision}.");
        }
        Debug.Log($"[Workshop] Rebased {moved} project room documents.");
    }
}

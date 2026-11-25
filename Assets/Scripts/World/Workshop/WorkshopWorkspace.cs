using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

internal static class WorkshopWorkspace
{
    [Serializable] internal sealed class Entry
    {
        public string id, title;
        public List<WorkshopPackage.Revision> parents;
    }

    private static string activeId;
    private static string home;
    internal static string Home => home ??= Path.Combine(Application.persistentDataPath,"LevelWorkshop");
    internal static string Shared => Path.Combine(Home,"Shared");
    private static string Copies => Path.Combine(Home,"Workspaces");
    private static string SelectionFile => Path.Combine(Home,"active-workspace.txt");
    internal static string ActiveId
    {
        get
        {
            if (activeId!=null) return activeId;
            var saved=File.Exists(SelectionFile) ? File.ReadAllText(SelectionFile).Trim() : "";
            return activeId=IsCopy(saved) && File.Exists(Marker(saved)) ? saved : "";
        }
    }
    internal static string Current => ActiveId.Length==0 ? Home : Path.Combine(Copies,ActiveId);
    private static bool IsCopy(string id) => Guid.TryParseExact(id,"N",out _);
    private static string Marker(string id) => Path.Combine(Copies,id,"workspace.json");

    internal static void Switch(string id)
    {
        if (id!="" && (!IsCopy(id) || !File.Exists(Marker(id)))) throw new IOException("작업 공간을 찾을 수 없습니다.");
        WorkshopFiles.WriteAtomic(SelectionFile,id);
        activeId=id;
    }

    internal static List<Entry> List()
    {
        var list=new List<Entry> { new() {id="",title="기본 작업 공간"} };
        if (!Directory.Exists(Copies)) return list;
        foreach (var folder in Directory.EnumerateDirectories(Copies).OrderByDescending(Directory.GetCreationTimeUtc))
        {
            string id=Path.GetFileName(folder);
            if (!IsCopy(id) || !File.Exists(Marker(id))) continue;
            var entry=JsonUtility.FromJson<Entry>(File.ReadAllText(Marker(id)));
            if (entry!=null && entry.id==id) list.Add(entry);
        }
        return list;
    }

    internal static List<WorkshopPackage.Revision> Ancestors()
    {
        if (ActiveId=="") return new List<WorkshopPackage.Revision>();
        return JsonUtility.FromJson<Entry>(File.ReadAllText(Marker(ActiveId))).parents ?? new List<WorkshopPackage.Revision>();
    }

    internal static Entry Publish(string title,WorkshopFloor floor,WorkshopVillage village,IEnumerable<WorkshopRoom> rooms,List<WorkshopPackage.Revision> parents)
    {
        var entry=new Entry {id=Guid.NewGuid().ToString("N"),title=title,parents=parents};
        string path=Path.Combine(Copies,entry.id);
        Directory.CreateDirectory(Path.Combine(path,"Rooms"));
        // Readers only discover a workspace after the last atomic write publishes its marker.
        var written=new List<string>();
        void Write(string file,string json)
        {
            WorkshopFiles.WriteAtomic(file,json);
            written.Add(file);
        }
        try
        {
            foreach (var room in rooms)
                Write(Path.Combine(path,"Rooms",room.id+".json"),room.Serialize());
            Write(Path.Combine(path,"floor.json"),JsonUtility.ToJson(floor,true));
            Write(Path.Combine(path,"village.json"),JsonUtility.ToJson(village,true));
            Write(Marker(entry.id),JsonUtility.ToJson(entry,true));
            return entry;
        }
        catch
        {
            foreach (var file in written) File.Delete(file);
            Directory.Delete(Path.Combine(path,"Rooms"));
            Directory.Delete(path);
            throw;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { home=null; activeId=null; }
#if LEVEL_WORKSHOP_QA
    internal static void UseTestHome(string path) { home=path; activeId=null; }
#endif
}

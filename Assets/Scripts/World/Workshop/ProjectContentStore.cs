using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// One lock and recovery record per document; game saves and local autosaves use their own roots.
public static class ProjectContentStore
{
#if UNITY_EDITOR
    internal static Action<string> AfterReplacementForCheck;
#endif
    public static string Root => Path.Combine(ProjectDataConnection.Checkout ?? Application.streamingAssetsPath,"DATA");
    internal static bool Authoring => ProjectDataConnection.Checkout!=null && WorkshopWorkspace.ActiveId=="";
    private static string State => Path.Combine(ProjectDataConnection.Checkout,"Library","ProjectContent");
    public static void RecoverPending()
    {
        if(ProjectDataConnection.Checkout==null || !Directory.Exists(State))return;
        var journals=Directory.GetFiles(State,"pending.json",SearchOption.AllDirectories);
        foreach(string path in journals)
        {
            string document=Path.GetDirectoryName(path).Substring(State.Length+1).Replace('\\','/');
            WithDocument(document,(_,journal)=>true);
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void RecoverOnStartup()=>TryReadPublished("저장 복구",RecoverPending);
    internal static string Json(JToken value) => value.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
    private static readonly HashSet<string> reported=new();
    internal static bool TryReadPublished(string document,Action read)
    {
        try{read();reported.Remove(document);return true;}
        catch(Exception error) when(error is IOException or InvalidDataException or JsonException or ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            if(reported.Add(document)) Debug.LogWarning("[DATA] "+document+" · "+error.Message+" · 게임 기본값을 사용합니다.");
            return false;
        }
    }
    internal static string PathFor(string folder, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidDataException("잘못된 DATA 경로: " + relative);
        return Path.Combine(folder, relative);
    }

    internal static void ReplaceFile(string path, string value)
    {
        if (value == null) { File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            { writer.Write(value); writer.Flush(); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void Finish(string folder, string journal, JArray writes)
    {
        foreach (JObject entry in writes)
        {
            string path = PathFor(folder, (string)entry["file"]);
            string actual = File.Exists(path) ? File.ReadAllText(path) : null;
            string after = (string)entry["after"];
            if (actual == after) continue;
            if (actual != (string)entry["before"]) throw new IOException("저장 복구 중 외부 변경을 발견했습니다. " + path);
            ReplaceFile(path, after);
#if UNITY_EDITOR
            AfterReplacementForCheck?.Invoke(path);
#endif
        }
        File.Delete(journal);
    }

    internal static T WithDocument<T>(string document, Func<string,string,T> action)
    {
        string folder = PathFor(Root, document);
        if (ProjectDataConnection.Checkout==null) return action(folder, null);
        string state = PathFor(State, document);
        Directory.CreateDirectory(state);
        using var guard = new FileStream(Path.Combine(state, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string journal = Path.Combine(state, "pending.json");
        if (File.Exists(journal)) Finish(folder, journal, JArray.Parse(File.ReadAllText(journal)));
        return action(folder, journal);
    }

    private static string Value(Dictionary<string,string> map, string key) => map.TryGetValue(key, out var v) ? v : null;
    private static JToken Parse(string json) => json == null ? null : JToken.Parse(json);
    internal sealed class Conflict : IOException
    {
        internal readonly string field;
        internal readonly JToken basis, mine, disk;
        internal bool useMine;
        internal Conflict(string field,JToken basis,JToken mine,JToken disk) : base("서로 다르게 바뀐 항목: "+field)
        { this.field=field; this.basis=basis; this.mine=mine; this.disk=disk; }
    }
    internal static JToken Merge(JToken basis, JToken mine, JToken disk, string path, Dictionary<string,Conflict> choices)
    {
        if (JToken.DeepEquals(mine, basis)) return disk;
        if (JToken.DeepEquals(disk, basis) || JToken.DeepEquals(mine, disk)) return mine;
        if ((basis == null || basis is JObject) && mine is JObject m && disk is JObject d)
        {
            var result = new JObject();
            foreach (string key in m.Properties().Concat(d.Properties()).Select(p=>p.Name).Distinct().OrderBy(k=>k,StringComparer.Ordinal))
            {
                var next = Merge(basis?[key], m[key], d[key], path + "/" + key,choices);
                if (next != null) result.Add(key, next.DeepClone());
            }
            return result;
        }
        if(choices!=null && choices.TryGetValue(path,out var choice) && JToken.DeepEquals(choice.basis,basis) &&
            JToken.DeepEquals(choice.mine,mine) && JToken.DeepEquals(choice.disk,disk)) return choice.useMine ? mine : disk;
        throw new Conflict(path,basis,mine,disk);
    }

    internal static Dictionary<string,string> Read(string folder)
    {
        var files = new Dictionary<string,string>(StringComparer.Ordinal);
        if (Directory.Exists(folder))
            foreach (string path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
                files.Add(path.Substring(folder.Length + 1).Replace('\\','/'), File.ReadAllText(path));
        return files;
    }

    internal static Dictionary<string,string> Save(string document, Dictionary<string,string> basis,
        Dictionary<string,string> mine, Action<Dictionary<string,string>> validate, Dictionary<string,Conflict> choices=null)
    {
        if (!Authoring) throw new InvalidOperationException("프로젝트 데이터 저장은 연결된 제작실에서 사용하세요.");
        return WithDocument(document, (folder,journal) =>
        {
            var disk = Read(folder);
            var merged = new Dictionary<string,string>(disk, StringComparer.Ordinal);
            var changes = new JArray();
            foreach (var key in basis.Keys.Union(mine.Keys).OrderBy(k=>k,StringComparer.Ordinal))
            {
                var previous = Parse(Value(basis,key)); var desired = Parse(Value(mine,key));
                if (JToken.DeepEquals(previous,desired)) continue;
                var current = Parse(Value(disk,key));
                var next = Merge(previous,desired,current,document+"/"+key,choices);
                if (JToken.DeepEquals(current,next)) continue;
                string value = next == null ? null : Json(next);
                if (value == null) merged.Remove(key); else merged[key] = value;
                changes.Add(new JObject { ["file"]=key, ["before"]=Value(disk,key), ["after"]=value });
            }
            validate(merged);
            if (changes.Count > 0)
            {
                ReplaceFile(journal, Json(changes));
                Finish(folder, journal, changes);
            }
            return merged;
        });
    }
}

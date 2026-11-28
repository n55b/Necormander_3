using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

// 굽기 왕복 검사의 굽기 단계(-Suite BakeRoundTrip·ProjectContent, 편집 모드). 플레이 단계는 Assets/Scripts/World/Workshop/ProjectBakeRoundTrip.cs.
// author가 제작실 화면으로 저장한 DATA와 원래 DATA를 ProjectDataBake·ProjectRoomBake로 전부 굽고, DATA에 남은 파일이
// 굽기가 남기는 것(삭제 표시, 원본 방의 room.json, 굽기 대상이 아닌 대사·번역·플레이어·층·마을)뿐인지 본다. 격리 복사본에는 Git이 없어 Git 차단은 끈다.
public static class ProjectBakeRoundTripCheck
{
    public static void Bake()
    {
        Require(Application.isBatchMode && Application.companyName == "NecromanderVerification", "Run in the isolated verification project.");
        var args = Environment.GetCommandLineArgs();
        string folder = args[Array.IndexOf(args, "-bakeRoundTrip") + 2];
        ProjectRooms.Reset();
        var values = ProjectDataBake.Plan();
        var rooms = ProjectRoomBake.Plan();
        var plan = values.Concat(rooms).Select(i => (i.error == null ? "굽기 " : "제외 ") + i.document + " " + i.title + (i.error != null ? " · " + i.error : "")).ToList();
        foreach (string line in plan) Debug.Log("[BakeRoundTrip] " + line);
        var refused = values.Concat(rooms).Where(i => i.error != null && !i.error.Contains("삭제 표시")).ToArray();
        Require(refused.Length == 0, "every document except removal marks can be baked: " + string.Join(" / ", refused.Select(i => i.document + " · " + i.error)));

        // 새 방 → 구울 프리팹 경로(미리보기와 굽기가 같은 경로를 쓴다)와 원본. after 단계가 후보에서 빼고 내용·등록을 비교한다.
        var newRooms = new JObject();
        const string prefix = "새 프리팹 변형 ";
        foreach (var item in rooms.Where(i => i.variant && i.error == null))
        {
            string id = item.document.Substring(ProjectRooms.DirectoryName.Length + 1);
            string header = Path.Combine(ProjectContentStore.Root, item.document, "room.json");
            newRooms[id] = new JObject {
                ["path"] = PrefabPath(item.changes.First(c => c.StartsWith(prefix, StringComparison.Ordinal))),
                ["template"] = (string)JObject.Parse(File.ReadAllText(header))["template"] };
        }
        var summary = new[] {
            ProjectDataBake.Execute(values.Where(i => i.error == null).Select(i => i.document).ToArray(), requireCleanGit: false),
            ProjectRoomBake.Execute(rooms.Where(i => i.error == null).Select(i => i.document).ToArray(), requireCleanGit: false) };
        foreach (string line in summary) Debug.Log("[BakeRoundTrip] " + line);

        var (left, wrong) = Leftovers();
        File.WriteAllText(Path.Combine(folder, "bake.json"), new JObject {
            ["rooms"] = newRooms, ["plan"] = new JArray(plan), ["summary"] = new JArray(summary),
            ["left"] = JObject.FromObject(left), ["wrong"] = new JArray(wrong) }.ToString());
        foreach (var pair in left) Debug.Log("[BakeRoundTrip] DATA에 남음 · " + pair.Key + " " + pair.Value + "개");
        Require(wrong.Count == 0, wrong.Count + " baked documents are still in DATA: " + string.Join(" / ", wrong.Take(20)));
        Require(!ProjectDataBake.Plan().Concat(ProjectRoomBake.Plan()).Any(i => i.error == null), "nothing left to bake");
        Debug.Log("[BakeRoundTrip] bake PASS · " + values.Count(i => i.error == null) + " value documents, " + rooms.Count(i => i.error == null) + " rooms");
    }

    // "새 프리팹 변형 <경로> · 원본 <제목>". 새 방 제목(기본 "<원본> · 복사본")에도 " · "가 들어 있어 뒤에서 자른다.
    private static string PrefabPath(string change) => change.Substring(0, change.LastIndexOf(" · 원본 ", StringComparison.Ordinal)).Substring("새 프리팹 변형 ".Length);

    // DATA에 남은 파일을 남는 이유별로 센다. 굽기 대상인데 남은 파일은 wrong.
    private static (SortedDictionary<string, int> left, List<string> wrong) Leftovers()
    {
        string root = ProjectContentStore.Root;
        bool Removed(params string[] document) => File.Exists(Path.Combine(root, Path.Combine(document), "removed.json"));
        var left = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var wrong = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string path = Path.GetRelativePath(root, file).Replace('\\', '/');
            var parts = path.Split('/');
            string name = parts[^1], why;
            bool structure = name == "parameters.json" || parts.Contains("LISTS") || parts.Contains("MEMBERS");
            if (path.StartsWith(ProjectDataBakeFixture.Variants + "/", StringComparison.Ordinal))
                why = Removed(parts[0], parts[1], parts[2]) ? "삭제 표시한 새 콘텐츠" : null;
            else if (path.StartsWith(ProjectRooms.DirectoryName + "/", StringComparison.Ordinal))
                why = Removed(parts[0], parts[1], parts[2]) ? "삭제 표시한 새 방" :
                    name == "room.json" && parts.Length == 4 && (string)JObject.Parse(File.ReadAllText(file))["template"] == parts[2] ? "원본 방 기준(room.json)" : null;
            else if (path.StartsWith("WORLD/POOLS/", StringComparison.Ordinal))
                why = Removed("WORLD", "ROOMS", Path.GetFileNameWithoutExtension(name)) ? "삭제 표시한 새 방" : null;
            else if (name == "removed.json") why = "삭제 표시(원본)";
            else if (structure) why = null;
            else why = "굽기 대상 아님 " + (parts.Length > 2 ? parts[0] + "/" + parts[1] : parts[0]);
            if (why == null) { wrong.Add(path); continue; }
            left.TryGetValue(why, out int count);
            left[why] = count + 1;
        }
        return (left, wrong);
    }
    private static void Require(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException("[BakeRoundTrip] " + what);
    }
}

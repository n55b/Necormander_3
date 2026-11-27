using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Tools > DATA 굽기. 본체는 ProjectDataBake(Assets/Scripts/World/Workshop). 마일스톤 직전에 한 사람이 실행하고 한 커밋으로 올린다.
public sealed class ProjectDataBakeWindow : EditorWindow
{
    private List<ProjectDataBake.Item> items = new();
    private readonly HashSet<string> open = new(StringComparer.Ordinal);
    private string blocker, result;
    private Vector2 scroll;

    [MenuItem("Tools/DATA 굽기")]
    private static void Open() => GetWindow<ProjectDataBakeWindow>("DATA 굽기").Reload();

    private void OnEnable() => Reload();

    private void Reload()
    {
        blocker = ProjectDataBake.Blocker();
        try { items = blocker == null ? ProjectDataBake.Plan() : new List<ProjectDataBake.Item>(); }
        catch (Exception e) { blocker = e.Message; items = new List<ProjectDataBake.Item>(); }
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("DATA 문서의 값을 원본 에셋에 쓰고 그 문서를 지웁니다. 새 콘텐츠는 원본 옆에 새 에셋으로 만들고 옛 세이브 키 content_<id>를 남깁니다.\n" +
            "마일스톤 직전에 한 사람이 실행하고, 바뀐 DATA와 에셋을 한 커밋으로 올립니다.", MessageType.Info);
        if (GUILayout.Button("다시 읽기")) { result = null; Reload(); }
        if (blocker != null) { EditorGUILayout.HelpBox(blocker, MessageType.Warning); return; }
        if (items.Count == 0) EditorGUILayout.LabelField("구울 문서가 없습니다.");
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var item in items)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(item.error != null))
                    item.selected = EditorGUILayout.ToggleLeft((item.variant ? "새 콘텐츠  " : "원본 수치  ") + item.title, item.selected && item.error == null);
                bool shown = open.Contains(item.document);
                if (GUILayout.Button(shown ? "접기" : "바뀔 값 " + item.changes.Count, GUILayout.Width(90)))
                    if (shown) open.Remove(item.document); else open.Add(item.document);
            }
            EditorGUILayout.LabelField(item.document, EditorStyles.miniLabel);
            if (item.error != null) EditorGUILayout.HelpBox(item.error, MessageType.Error);
            if (open.Contains(item.document))
                using (new EditorGUI.IndentLevelScope())
                    foreach (string change in item.changes) EditorGUILayout.LabelField(change, EditorStyles.wordWrappedMiniLabel);
        }
        EditorGUILayout.EndScrollView();
        if (result != null) EditorGUILayout.HelpBox(result, MessageType.None);
        var chosen = items.Where(i => i.selected && i.error == null).Select(i => i.document).ToArray();
        using (new EditorGUI.DisabledScope(chosen.Length == 0))
            if (GUILayout.Button($"선택한 {chosen.Length}개 굽기", GUILayout.Height(28)) &&
                EditorUtility.DisplayDialog("DATA 굽기", $"{chosen.Length}개 문서를 에셋에 쓰고 DATA에서 지웁니다. 되돌릴 때는 Git을 사용합니다.", "굽기", "취소"))
            {
                try { result = ProjectDataBake.Execute(chosen); }
                catch (Exception e) { result = "굽기 실패: " + e.Message; Debug.LogException(e); }
                Reload();
            }
    }
}

// 굽기 검사가 만든 DATA 문서와 에셋을 지우고 바꾼 에셋 파일을 되돌린다. 같은 실행의 다음 검사가 원래 상태를 본다.
internal sealed class ProjectDataBakeFixture : IDisposable
{
    internal const string Variants = "CATALOG/VARIANTS";
    private readonly Dictionary<string, byte[]> files = new();
    private readonly List<string> folders = new();
    private readonly HashSet<string> before = Assets();

    private static HashSet<string> Assets() => new(AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/SOData", "Assets/Resources" }));
    internal static string Guid(UnityEngine.Object asset) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

    internal void Keep(params UnityEngine.Object[] assets)
    {
        foreach (var asset in assets) files[AssetDatabase.GetAssetPath(asset)] = File.ReadAllBytes(AssetDatabase.GetAssetPath(asset));
    }
    internal string Variant(ScriptableObject template, string title, JObject parameters = null)
    {
        string id = System.Guid.NewGuid().ToString("N");
        Write(Variants + "/" + id, "definition.json", new JObject { ["template"] = Guid(template), ["title"] = title });
        if (parameters != null) Write(Variants + "/" + id, "parameters.json", parameters);
        return id;
    }
    internal string Parameters(ScriptableObject target, JObject values)
    {
        string document = ProjectParameterBindings.Group(AssetDatabase.GetAssetPath(target)) + "/" + Guid(target);
        Write(document, "parameters.json", values);
        return document;
    }
    private void Write(string document, string name, JObject json)
    {
        string folder = Path.Combine(ProjectContentStore.Root, document);
        Directory.CreateDirectory(folder);
        if (!folders.Contains(folder)) folders.Add(folder);
        File.WriteAllText(Path.Combine(folder, name), json.ToString().Replace("\r\n", "\n") + "\n");
    }
    public void Dispose()
    {
        foreach (string guid in Assets().Except(before)) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
        foreach (var file in files) { File.WriteAllBytes(file.Key, file.Value); AssetDatabase.ImportAsset(file.Key, ImportAssetOptions.ForceUpdate); }
        string root = Path.GetFullPath(ProjectContentStore.Root);
        foreach (string folder in folders)
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            for (var up = Directory.GetParent(folder); up != null && up.FullName.Length > root.Length && up.Exists && !up.EnumerateFileSystemInfos().Any(); up = up.Parent)
                up.Delete();
        }
    }
}

// -Suite ProjectContent: 임시 원본 수치와 새 콘텐츠를 만들어 굽고, 미리보기·이름·목록 가입·참조 교체·세이브 키·Git 차단을 확인한다.
public static class ProjectDataBakeCheck
{
    public static void Run()
    {
        Require(Application.isBatchMode && Application.companyName == "NecromanderVerification", "Run in the isolated verification project.");
        GitGuard();
        using (var fixture = new ProjectDataBakeFixture())
        {
            var growth = AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>("Assets/SOData/Registry/Growth Reward Registry.asset");
            var shop = AssetDatabase.LoadAssetAtPath<ShopRegistrySO>("Assets/SOData/Registry/Shop Registry.asset");
            var item = growth.items.First(i => i != null && shop.itemPool.Contains(i));
            fixture.Keep(growth, shop, item);
            int raw = item.maxStack;
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(item)).Replace('\\', '/');
            const string title = "QA 굽기 아이템";
            string itemDoc = fixture.Parameters(item, new JObject { ["maxStack"] = (raw + 5).ToString() });
            string first = fixture.Variant(item, title, new JObject { ["maxStack"] = "7" });
            string second = fixture.Variant(item, title);
            string registryDoc = fixture.Parameters(growth, new JObject { ["startingItem"] = first });
            string[] documents = { itemDoc, ProjectDataBakeFixture.Variants + "/" + first, ProjectDataBakeFixture.Variants + "/" + second, registryDoc };

            var plan = ProjectDataBake.Plan();
            foreach (string document in documents)
                Require(plan.Any(i => i.document == document && i.error == null), "preview lists " + document);
            Require(plan.First(i => i.document == itemDoc).changes.Any(c => c.EndsWith(": " + raw + " → " + (raw + 5))), "preview shows the asset value and the DATA value");
            // 번호는 변형 폴더 순서(무작위 id)로 붙는다. 어느 쪽이 번호를 받든 미리보기 경로에 그대로 구워져야 한다.
            string PreviewPath(string document) => plan.First(i => i.document == document).changes
                .First(c => c.StartsWith("새 에셋 ", StringComparison.Ordinal)).Substring(5).Split(" · ")[0];
            string pathA = PreviewPath(documents[1]), pathB = PreviewPath(documents[2]);
            Require(new HashSet<string> { pathA, pathB }.SetEquals(new[] { folder + "/" + title + ".asset", folder + "/" + title + " 2.asset" }),
                "readable asset names, numbered on a clash");
            Require(plan.First(i => i.document == registryDoc).changes.Any(c => c.Contains("새 콘텐츠 " + title)), "preview names the referenced new content");
            Require(Throws(() => ProjectDataBake.Execute(new[] { registryDoc }, requireCleanGit: false), "함께 고르세요"),
                "a document that references an unbaked variant is refused");
            Require(Throws(() => ProjectDataBake.Execute(new[] { itemDoc }, requireCleanGit: false), "함께 고르세요") && item.maxStack == raw,
                "baking an original alone is refused while its new content without that value would inherit it");
            Require(File.Exists(Path.Combine(ProjectContentStore.Root, registryDoc, "parameters.json")) && growth.startingItem != null &&
                Path.GetFileName(AssetDatabase.GetAssetPath(growth.startingItem)) != title + ".asset", "refused bake changes nothing");

            Debug.Log("[ProjectDataBakeCheck] " + ProjectDataBake.Execute(documents, requireCleanGit: false));
            var a = AssetDatabase.LoadAssetAtPath<ItemSO>(pathA);
            var b = AssetDatabase.LoadAssetAtPath<ItemSO>(pathB);
            Require(item.maxStack == raw + 5 && File.ReadAllText(AssetDatabase.GetAssetPath(item)).Contains("maxStack: " + (raw + 5)), "original value written to its .asset");
            Require(a != null && a.maxStack == 7 && a.itemName == title && a.formerAssetNames.SequenceEqual(new[] { "content_" + first }), "variant baked with its values, title and save alias");
            Require(b != null && b.maxStack == raw && b.formerAssetNames.SequenceEqual(new[] { "content_" + second }), "variant without values copies the unbaked original");
            Require(growth.items.Contains(a) && growth.items.Contains(b) && shop.itemPool.Contains(a) && shop.itemPool.Contains(b), "new assets join the original's registries");
            Require(growth.startingItem == a, "DATA reference to the variant now points at the new asset");
            Require(growth.ResolveSavedItem("content_" + first) == a && growth.ResolveSavedItem(item.name) == item, "old and current save keys resolve");
            foreach (string document in documents)
                Require(!Directory.Exists(Path.Combine(ProjectContentStore.Root, document)), "baked document removed " + document);
            Require(!ProjectDataBake.Plan().Any(i => documents.Contains(i.document)), "nothing left to bake");
        }
        Debug.Log("[ProjectDataBakeCheck] PASS");
    }

    // 커밋하지 않은 DATA 변경이 있으면 막고, 커밋하면 연다.
    private static void GitGuard()
    {
        string repo = Path.Combine(Path.GetTempPath(), "NecromanderBakeGuard-" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Path.Combine(repo, "DATA"));
        try
        {
            Git(repo, "init -q");
            Require(ProjectDataBake.GitBlocker(repo) == null, "empty DATA is not blocked");
            File.WriteAllText(Path.Combine(repo, "DATA", "a.json"), "{}\n");
            Require(ProjectDataBake.GitBlocker(repo)?.Contains("커밋하지 않은") == true, "uncommitted DATA blocks the bake");
            Git(repo, "add -A");
            Git(repo, "-c user.name=qa -c user.email=qa@example.invalid commit -q -m qa");
            Require(ProjectDataBake.GitBlocker(repo) == null, "committed DATA is not blocked");
        }
        finally
        {
            foreach (var f in new DirectoryInfo(repo).GetFiles("*", SearchOption.AllDirectories)) f.Attributes = FileAttributes.Normal;
            Directory.Delete(repo, true);
        }
    }
    private static void Git(string directory, string arguments)
    {
        using var git = Process.Start(new ProcessStartInfo("git", arguments) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true });
        string error = git.StandardError.ReadToEnd();
        git.WaitForExit();
        Require(git.ExitCode == 0, "git " + arguments + ": " + error);
    }
    private static bool Throws(Action action, string message)
    {
        try { action(); return false; }
        catch (Exception e) { return e.Message.Contains(message); }
    }
    private static void Require(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException("[ProjectDataBakeCheck] " + what);
    }
}

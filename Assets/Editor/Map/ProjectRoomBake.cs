using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > DATA 굽기의 방 항목. 원본 방 문서(WORLD/ROOMS/<원본 GUID>)는 그 프리팹에 쓰고 room.json만 남긴다.
// 새 방(WORLD/ROOMS/<새 id>)은 원본의 프리팹 변형으로 만들고, 던전 등록(WORLD/POOLS)은 RoomPrefabData 후보로 옮긴 뒤 문서를 지운다.
// 구운 프리팹이 굽기 전 "원본 + 문서" 결과와 같은지, 새 방이 후보에 들지 않았으면 같은 시드의 던전 배치까지 같은지 비교한다.
public static class ProjectRoomBake
{
    private const string Header = "room.json";
    private const string Removed = "removed.json";
    private sealed class Room
    {
        public ProjectDataBake.Item item;
        public WorkshopRoom doc;
        public WorkshopCatalog.Template template;
        public string path;       // 새 방의 새 프리팹 경로. 미리보기와 굽기가 같은 값을 쓴다.
        public bool registered;
    }

    public static bool Owns(string document) => document.StartsWith(ProjectRooms.DirectoryName + "/", StringComparison.Ordinal);
    private static WorkshopCatalog Catalog() => AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath);
    private static bool DataError(Exception e) => e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException;

    public static List<ProjectDataBake.Item> Plan() => Rooms(Catalog()).Select(r => r.item).ToList();

    private static List<Room> Rooms(WorkshopCatalog catalog)
    {
        var rooms = new List<Room>();
        if (catalog == null) return rooms;
        var headers = new Dictionary<string, (string template, string title)>(StringComparer.Ordinal);
        foreach (string id in ProjectRooms.Ids())
        {
            try
            {
                var json = JObject.Parse(File.ReadAllText(Path.Combine(ProjectContentStore.Root, ProjectRooms.DirectoryName, id, Header)));
                headers[id] = ((string)json["template"], (string)json["title"]);
            }
            catch (Exception e) when (DataError(e)) { headers[id] = (null, id); }
        }
        var slots = Slots();
        var taken = new HashSet<string>(AssetDatabase.FindAssets("t:Prefab", new[] { WorkshopBuild.Rooms })
            .Select(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))), StringComparer.OrdinalIgnoreCase);
        foreach (var (id, header) in headers)
        {
            Dictionary<string, string> files = null;
            try { files = ProjectRooms.Snapshot(id); } catch (Exception e) when (DataError(e)) { }
            // 원본 방의 기준 문서(바꾼 것 없이 room.json만 있음)는 구울 것이 없다.
            if (files?.Count == 1 && files.ContainsKey(Header) && header.template == id) continue;
            var room = new Room { item = new ProjectDataBake.Item { document = ProjectRooms.DirectoryName + "/" + id, title = header.title ?? id, variant = header.template != id } };
            rooms.Add(room);
            // 삭제 표시한 방은 DATA에 남겨 두고 굽지 않는다(새 방은 구우면 프리팹·후보로 되살아난다).
            if (files?.ContainsKey(Removed) == true) { room.item.error = "삭제 표시는 굽지 않습니다"; room.item.selected = false; continue; }
            try { Describe(room, id, files ?? ProjectRooms.Snapshot(id), catalog, headers, slots, taken); }
            catch (Exception e) when (DataError(e)) { room.item.error = e.Message; room.item.selected = false; }
        }
        return rooms;
    }

    private static void Describe(Room room, string id, Dictionary<string, string> files, WorkshopCatalog catalog,
        Dictionary<string, (string template, string title)> headers, List<(string file, string document)> slots, HashSet<string> taken)
    {
        var item = room.item;
        var doc = room.doc = ProjectRooms.Decode(id, files, catalog);   // 원본 버전이 어긋난 문서는 여기서 막힌다(자동 재기준 대상)
        var template = room.template = catalog.FindRoom(doc.template);
        string source = AssetDatabase.GetAssetPath(template.room.gameObject);
        item.title = doc.title;
        string counts = $"타일 {doc.cells.Count}칸 · 배치 {doc.pieces.Count}개 · 출구 {doc.exits.Count}개 · 웨이브 {doc.waves.Count}개";
        if (!item.variant)
        {
            // 새 방은 원본 덮어쓰기 없이 원본 프리팹 위에 그려져 있다. 원본에 쓰면 새 방(과 그 프리팹 변형)이 덮어쓰기를 물려받는다.
            var based = headers.Where(h => h.Key != id && h.Value.template == id).Select(h => h.Value.title).ToArray();
            if (based.Length > 0)
                throw new InvalidDataException("이 원본을 바탕으로 한 새 방(" + string.Join(", ", based) + ")이 덮어쓰기를 물려받아 바뀝니다. 새 방이 있는 원본 방은 Unity에서 프리팹을 직접 고치세요.");
            var dependents = catalog.rooms.Where(t => t.id != id && AssetDatabase.GetDependencies(AssetDatabase.GUIDToAssetPath(t.id)).Contains(source))
                .Select(t => t.title).ToArray();
            if (dependents.Length > 0)
                throw new InvalidDataException("이 프리팹을 바탕으로 한 방 프리팹(" + string.Join(", ", dependents) + ")도 바뀝니다. Unity에서 프리팹을 직접 고치세요.");
            item.changes.Add("원본 프리팹 " + source + "에 쓴다 · " + counts);
            item.changes.Add("방 문서는 room.json만 남기고 새 원본 기준으로 옮긴다");
            return;
        }
        // 층 연결은 "이 시드의 이 칸에서 원본 대신 이 문서"라서 프리팹 후보로 옮기면 뜻이 바뀐다.
        var slot = slots.FirstOrDefault(s => s.document == id).file;
        if (slot != null) throw new InvalidDataException("층 연결 " + slot + " 이(가) 이 방을 가리킵니다. 층 화면에서 연결을 바꾼 뒤 구우세요.");
        room.registered = ProjectRooms.IsRegistered(doc);
        var entry = catalog.roomPool.roomEntries.FirstOrDefault(e => e?.prefabs != null && e.prefabs.Contains(template.room.gameObject));
        if (room.registered && entry == null) throw new InvalidDataException("던전 후보 목록에서 원본 방 " + template.title + " 을(를) 찾을 수 없습니다.");
        room.path = NewPath(Path.GetDirectoryName(source), doc.title, taken);
        item.changes.Add("새 프리팹 변형 " + room.path + " · 원본 " + template.title);
        item.changes.Add(counts);
        item.changes.Add(room.registered
            ? "던전 후보 " + catalog.roomPool.name + "/" + entry.roomType + "에 추가 · WORLD/POOLS/" + doc.template + "/" + id + ".json 삭제 · 원본과 나눠 쓰던 자리에서 따로 뽑히는 후보가 된다"
            : "던전에 등록하지 않은 시험용 방 · 프리팹만 만든다");
        item.changes.Add("방 문서 " + item.document + " 삭제 · 옛 .levelpack·로컬 저장본의 이 방 id는 프리팹을 가리키지 않는다");
        item.selected = room.registered;
    }

    private static List<(string file, string document)> Slots()
    {
        string floors = Path.Combine(ProjectContentStore.Root, "WORLD", "FLOORS");
        var slots = new List<(string, string)>();
        if (!Directory.Exists(floors)) return slots;
        foreach (string file in Directory.EnumerateFiles(floors, "*.json", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(Path.GetDirectoryName(file)) != "SLOTS") continue;
            try { slots.Add((Path.GetRelativePath(ProjectContentStore.Root, file).Replace('\\', '/'), (string)JObject.Parse(File.ReadAllText(file))["document"])); }
            catch (Exception e) when (DataError(e)) { }
        }
        return slots;
    }

    private static string NewPath(string folder, string title, HashSet<string> taken)
    {
        folder = folder.Replace('\\', '/');
        var invalid = Path.GetInvalidFileNameChars();
        string stem = new string(title.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (stem.Length == 0) stem = "Room";
        string name = stem;
        for (int n = 2; taken.Contains(name) || File.Exists(folder + "/" + name + ".prefab"); n++) name = stem + " " + n;
        taken.Add(name);
        return folder + "/" + name + ".prefab";
    }

    // 방 결과 비교용 표기: 레이어별 타일·모양·색, 하위 오브젝트의 경로·활성·위치, 출구, 전투 웨이브. 루트 이름은 뺀다.
    private static List<string> Signature(GameObject prefab, WorkshopRoom doc, WorkshopCatalog catalog)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var root = clone.transform;
            if (doc != null)
                WorkshopGeometry.ApplyToGame(doc, clone.GetComponent<RoomInstance>(), catalog, (p, parent) => (GameObject)PrefabUtility.InstantiatePrefab(p, parent));
            var lines = new List<string>();
            for (int layer = 0; layer < WorkshopCatalog.Layers.Length; layer++)
            {
                var map = WorkshopCatalog.Map(root, layer);
                if (map == null) continue;
                foreach (var at in map.cellBounds.allPositionsWithin)
                    if (map.GetTile(at) is { } tile)
                        lines.Add($"{WorkshopCatalog.Layers[layer]} {at.x},{at.y} {AssetDatabase.GetAssetPath(tile)}:{tile.name} {map.GetTransformMatrix(at).ToString().Replace("\n", " ")} {(Color32)map.GetColor(at)}");
            }
            string PathOf(Transform t) => t == root ? "" : PathOf(t.parent) + "/" + t.name;
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                if (t != root) lines.Add($"{PathOf(t)} {t.gameObject.activeSelf} {t.localPosition.x:F3},{t.localPosition.y:F3},{t.localPosition.z:F3}");
            foreach (var anchor in clone.GetComponentsInChildren<RoomAnchor>())
                lines.Add($"출구 {anchor.direction} {PathOf(anchor.transform)}");
            if (clone.TryGetComponent<NormalRoomEvent>(out var combat))
                lines.Add("웨이브 " + string.Join(",", combat.AuthoredWaves.Select(w => w != null ? w.name : "")) + " 무작위 적 " + combat.UsesRandomEnemies);
            lines.Sort(StringComparer.Ordinal);
            return lines;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    // WorkshopRoomImport.VerifyLayout·DungeonLayoutCheck와 같은 시드와 층. 문서가 바꾸는 문 방향(AvailableDoors)도 반영된다.
    private static Dictionary<string, string> Layouts(WorkshopCatalog catalog)
    {
        ProjectRooms.Reset();
        int boss = catalog.generation.floorBosses.FirstOrDefault(e => e != null && e.boss != null)?.floor ?? 0;
        var cases = new List<(DungeonLayoutMode mode, int floor, int seed)> { (DungeonLayoutMode.Tutorial, 0, 907), (DungeonLayoutMode.Normal, 1, 907), (DungeonLayoutMode.EliteArena, 2, 613) };
        if (boss > 0) cases.Add((DungeonLayoutMode.Boss, boss, 2718));
        var layouts = new Dictionary<string, string>();
        foreach (var c in cases)
        {
            var random = new System.Random(c.seed);
            var elite = catalog.elites.Length > 0 ? catalog.elites[random.Next(catalog.elites.Length)] : null;
            var planner = new DungeonLayoutPlanner(catalog.generation, catalog.roomPool);
            string signature = null, error = null;
            for (int i = 0; i < 15 && signature == null; i++)
                if (planner.TryPlan(c.mode, c.floor, elite != null ? elite.dedicatedRoomPrefab : null, random, "DATA 굽기", out var plan, out error))
                    signature = string.Join("|", plan.Rooms.Select(r => $"{r.Cell}:{r.Type}:{r.Prefab.name}").Concat(plan.Doors.Select(d => $"{d.From}-{d.To}")));
            layouts[$"{c.mode} {c.floor}층 시드 {c.seed}"] = signature ?? "생성 실패 " + error;
        }
        return layouts;
    }

    // documents: Plan()의 Item.document. 계획을 다시 읽어 디스크의 현재 문서를 굽는다.
    public static string Execute(ICollection<string> documents, bool requireCleanGit = true)
    {
        string blocked = ProjectDataBake.Blocker(requireCleanGit);
        if (blocked != null) throw new InvalidOperationException(blocked);
        ProjectContentStore.RecoverPending();
        var catalog = Catalog();
        var rooms = Rooms(catalog).Where(r => documents.Contains(r.item.document)).ToList();
        if (rooms.Count == 0) return "구울 방을 고르지 않았습니다.";
        var broken = rooms.Where(r => r.item.error != null).ToArray();
        if (broken.Length > 0) throw new InvalidDataException("오류가 있는 방은 구울 수 없습니다.\n" + string.Join("\n", broken.Select(r => r.item.document + " · " + r.item.error)));

        var expected = rooms.ToDictionary(r => r.item.document, r => Signature(r.template.room.gameObject, r.doc, catalog));
        var layouts = Layouts(catalog);
        var pool = catalog.roomPool;
        var made = new Dictionary<string, string>();   // 문서 → 구운 프리팹 경로
        foreach (var r in rooms.Where(r => !r.item.variant))
        {
            WorkshopRoomImport.Overwrite(r.doc, catalog);
            made[r.item.document] = AssetDatabase.GetAssetPath(r.template.room.gameObject);
        }
        foreach (var r in rooms.Where(r => r.item.variant))
        {
            var prefab = WorkshopRoomImport.SaveVariant(r.doc, catalog, r.path);
            made[r.item.document] = r.path;
            if (!r.registered) continue;
            pool.roomEntries.First(e => e?.prefabs != null && e.prefabs.Contains(r.template.room.gameObject)).prefabs.Add(prefab);
            EditorUtility.SetDirty(pool);
        }
        AssetDatabase.SaveAssetIfDirty(pool);
        foreach (var r in rooms)
        {
            if (!r.item.variant) { ProjectDataBake.Rewrite(r.item.document, files => new Dictionary<string, string> { [Header] = files[Header] }); continue; }
            ProjectDataBake.Rewrite(r.item.document, _ => new Dictionary<string, string>());
            if (r.registered)
                ProjectDataBake.Rewrite("WORLD/POOLS/" + r.doc.template, files => { var next = new Dictionary<string, string>(files); next.Remove(r.doc.id + ".json"); return next; });
        }
        // 카탈로그를 다시 굽고(새 프리팹이 원본 방이 된다) 원본 방 문서의 baseRevision을 옮긴다.
        string warning = null;
        try { ProjectRoomRebase.Run(); }
        catch (InvalidDataException e) { warning = e.Message; }
        ProjectRooms.Reset();
        catalog = Catalog();

        var differences = new List<string>();
        foreach (var r in rooms)
        {
            string path = made[r.item.document];
            var before = expected[r.item.document];
            var after = Signature(AssetDatabase.LoadAssetAtPath<GameObject>(path), null, catalog);
            if (!before.SequenceEqual(after))
                differences.Add(r.item.title + ": " + string.Join(" / ", before.Except(after).Take(3).Select(l => "전 " + l).Concat(after.Except(before).Take(3).Select(l => "후 " + l))));
            var baked = catalog.FindRoom(AssetDatabase.AssetPathToGUID(path));
            if (baked == null || baked.inPool != (r.item.variant ? r.registered : r.template.inPool))
                differences.Add(r.item.title + " · 원본 방 목록이나 던전 후보 등록이 굽기 전과 다릅니다.");
        }
        // 새 방이 후보에 들면 같은 시드라도 후보 수가 달라져 배치가 바뀐다. 그때는 생성되는지만 본다.
        bool joined = rooms.Any(r => r.registered);
        foreach (var pair in Layouts(catalog))
            if (pair.Value.StartsWith("생성 실패", StringComparison.Ordinal) || (!joined && pair.Value != layouts[pair.Key]))
                differences.Add(pair.Key + " · 굽기 전 " + layouts[pair.Key] + " · 구운 뒤 " + pair.Value);
        if (differences.Count > 0)
            throw new InvalidDataException("구운 방이 굽기 전 플레이 결과와 다릅니다. Git으로 DATA와 에셋을 되돌리세요.\n" + string.Join("\n", differences.Take(20)));
        string summary = $"원본 방 {rooms.Count(r => !r.item.variant)}개, 새 방 {rooms.Count(r => r.item.variant)}개를 구웠습니다. 구운 프리팹이 굽기 전 방과 같습니다. " +
            (joined ? "새 방이 따로 뽑히는 후보가 되어 같은 시드의 던전 배치는 달라질 수 있습니다." : "같은 시드의 던전 배치도 같습니다.");
        foreach (var r in rooms.Where(r => r.item.variant)) summary += "\n새 프리팹 " + r.path;
        if (warning != null) summary += "\n재기준하지 못한 방 문서: " + warning;
        Debug.Log("[DATA 굽기] " + summary);
        return summary;
    }
}

// -Suite ProjectContent: 원본 방 1개에 덮어쓰기, 다른 원본에 새 방 1개를 만들어 굽고 프리팹·후보·문서·시드 배치를 확인한 뒤 모두 되돌린다.
public static class ProjectRoomBakeCheck
{
    public static void Run()
    {
        Require(Application.isBatchMode && Application.companyName == "NecromanderVerification", "Run in the isolated verification project.");
        var catalog = AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath);
        string data = ProjectContentStore.Root;
        string Folder(string document) => Path.Combine(data, document);
        var headers = ProjectRooms.Ids().ToDictionary(id => id, id => (string)JObject.Parse(File.ReadAllText(Path.Combine(Folder(ProjectRooms.DirectoryName + "/" + id), "room.json")))["template"]);
        string PathOf(WorkshopCatalog.Template t) => AssetDatabase.GUIDToAssetPath(t.id);
        // 덮어쓰기도 새 방도 없고 다른 방 프리팹이 기반으로 쓰지 않는 일반 전투 방 두 개.
        var free = catalog.rooms.Where(t => t.inPool && t.type == RoomType.Normal && t.room.GetComponent<NormalRoomEvent>() != null &&
                !headers.Any(h => h.Value == t.id && h.Key != t.id) && ProjectRooms.Snapshot(t.id).Keys.All(k => k == "room.json") &&
                !catalog.rooms.Any(o => o != t && AssetDatabase.GetDependencies(PathOf(o)).Contains(PathOf(t))))
            .OrderBy(t => t.id, StringComparer.Ordinal).ToArray();
        Require(free.Length >= 2, "two free normal rooms");
        var (first, second) = (free[0], free[1]);
        var pool = catalog.roomPool;
        var keep = new[] { PathOf(first), AssetDatabase.GetAssetPath(pool), ProjectRooms.CatalogPath }.ToDictionary(p => p, File.ReadAllBytes);
        var documents = new[] { ProjectRooms.DirectoryName + "/" + first.id, "WORLD/POOLS/" + second.id }
            .ToDictionary(d => d, d => Directory.Exists(Folder(d)) ? Directory.EnumerateFiles(Folder(d), "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(Folder(d), f), File.ReadAllText) : new Dictionary<string, string>());
        string room = null, blockerRoom = null, slot = Path.Combine(data, "WORLD", "FLOORS", "Dungeon", "1", "SLOTS", "907_0_0.json");
        string madePath = null;
        try
        {
            Debug.Log($"[ProjectRoomBakeCheck] original {first.title}, new room on {second.title}");
            var tile = catalog.tiles.First(t => t.tile is UnityEngine.Tilemaps.Tile);
            var flip = Matrix4x4.Scale(new Vector3(-1, 1, 1));
            var tint = new Color32(96, 144, 184, 255);
            Vector2Int[] Cells(WorkshopCatalog.Template t)
            {
                var map = WorkshopCatalog.Map(t.room.transform, 0);
                var cells = new List<Vector2Int>();
                foreach (var c in map.cellBounds.allPositionsWithin)
                    if (cells.Count < 2 && map.HasTile(c)) cells.Add((Vector2Int)c);
                return cells.ToArray();
            }

            // 원본 방 덮어쓰기: 칠하기(모양·색)·지우기·출구 이동·웨이브·소품 추가·기존 배치 이동
            var original = ProjectRooms.Template(first);
            var a = Cells(first);
            original.Paint(0, a[0], tile.id, original.StyleFor(flip, tint));
            original.Paint(0, a[1], "");
            var anchor = first.room.GetComponentsInChildren<RoomAnchor>().First();
            original.SetDoor(anchor.direction, first.room.transform.InverseTransformPoint(anchor.transform.position) + Vector3.right * .25f);
            original.waves.Add(catalog.clusters[0].id);
            original.pieces.Add(new WorkshopRoom.Piece { id = Guid.NewGuid().ToString("N"), catalog = catalog.pieces.First(p => !p.enemy).id, local = Vector3.zero });
            var placed = first.room.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => WorkshopGeometry.IsEditablePiece(first.room.transform, t));
            if (placed != null)
            {
                string key = WorkshopCatalog.PathOf(first.room.transform, placed);
                original.pieces.Add(new WorkshopRoom.Piece { id = key, source = key, local = first.room.transform.InverseTransformPoint(placed.position) + Vector3.up * .5f });
            }
            ProjectRooms.Publish(original, catalog);

            // 새 방(던전 등록): 칠하기·적 배치·웨이브
            var made = WorkshopRoom.Create(second);
            made.title = "QA 굽기 방";
            var b = Cells(second);
            made.Paint(0, b[0], tile.id, made.StyleFor(flip, tint));
            made.pieces.Add(new WorkshopRoom.Piece { id = Guid.NewGuid().ToString("N"), catalog = catalog.pieces.First(p => p.enemy).id, local = Vector3.zero });
            made.waves.Add(catalog.clusters[0].id);
            ProjectRooms.Publish(made, catalog);
            ProjectRooms.Register(made);
            room = ProjectRooms.DirectoryName + "/" + made.id;

            var plan = ProjectRoomBake.Plan();
            var originalItem = plan.FirstOrDefault(i => i.document == documents.Keys.First());
            var madeItem = plan.FirstOrDefault(i => i.document == room);
            Require(originalItem != null && originalItem.error == null && !originalItem.variant && originalItem.selected, "preview lists the original room overwrite");
            Require(madeItem != null && madeItem.error == null && madeItem.variant && madeItem.selected, "preview lists the registered new room");
            Require(!plan.Any(i => i.document == ProjectRooms.DirectoryName + "/" + second.id), "unchanged original room documents are not listed");
            const string prefix = "새 프리팹 변형 ";
            madePath = madeItem.changes.First(c => c.StartsWith(prefix, StringComparison.Ordinal)).Substring(prefix.Length).Split(" · ")[0];
            Require(madePath == Path.GetDirectoryName(PathOf(second)).Replace('\\', '/') + "/QA 굽기 방.prefab", "readable new prefab name " + madePath);

            // 거부: 원본에 새 방이 있으면 원본 덮어쓰기를, 층 연결이 가리키면 새 방을 굽지 않는다.
            var blocker = WorkshopRoom.Create(first);
            blocker.title = "QA 굽기 차단";
            ProjectRooms.Publish(blocker, catalog);
            blockerRoom = ProjectRooms.DirectoryName + "/" + blocker.id;
            Directory.CreateDirectory(Path.GetDirectoryName(slot));
            File.WriteAllText(slot, new JObject { ["scenario"] = 0, ["floor"] = 1, ["seed"] = 907, ["cell"] = new JObject { ["x"] = 0, ["y"] = 0 },
                ["template"] = second.id, ["document"] = made.id }.ToString() + "\n");
            plan = ProjectRoomBake.Plan();
            Require(plan.First(i => i.document == documents.Keys.First()).error?.Contains("QA 굽기 차단") == true, "a new room on the template blocks its overwrite");
            Require(plan.First(i => i.document == room).error?.Contains("층 연결") == true, "a floor slot blocks the new room");
            Require(Throws(() => ProjectRoomBake.Execute(new[] { room }, requireCleanGit: false), "오류가 있는 방"), "blocked room is refused");
            Directory.Delete(Folder(blockerRoom), true); blockerRoom = null;
            DeleteUp(slot); slot = null;
            ProjectRooms.Reset();

            // 1회: 원본 방만. 시드 배치까지 같아야 한다.
            string summary = ProjectRoomBake.Execute(new[] { documents.Keys.First() }, requireCleanGit: false);
            Debug.Log("[ProjectRoomBakeCheck] " + summary);
            Require(summary.Contains("같은 시드의 던전 배치도 같습니다"), "same-seed layouts compared");
            var baked = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(first));
            var ground = WorkshopCatalog.Map(baked.transform, 0);
            Require(ground.GetTile((Vector3Int)a[0]) == tile.tile && ground.GetTransformMatrix((Vector3Int)a[0]) == flip && !ground.HasTile((Vector3Int)a[1]),
                "painted, styled and erased cells written to the original prefab");
            Require(baked.GetComponent<NormalRoomEvent>().AuthoredWaves.SequenceEqual(new[] { catalog.clusters[0].asset }), "waves written to the original prefab");
            var header = JObject.Parse(File.ReadAllText(Path.Combine(Folder(documents.Keys.First()), "room.json")));
            Require(ProjectRooms.Snapshot(first.id).Count == 1 && (string)header["baseRevision"] == WorkshopBuild.Revision(PathOf(first)) &&
                (string)header["baseRevision"] == AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath).FindRoom(first.id).revision,
                "original room document keeps only room.json on the new revision");

            // 2회: 새 방. 프리팹 변형·후보 등록·문서 삭제.
            summary = ProjectRoomBake.Execute(new[] { room }, requireCleanGit: false);
            Debug.Log("[ProjectRoomBakeCheck] " + summary);
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(madePath);
            catalog = AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath);
            Require(variant != null && PrefabUtility.GetPrefabAssetType(variant) == PrefabAssetType.Variant &&
                PrefabUtility.GetCorrespondingObjectFromSource(variant) == second.room.gameObject, "new room saved as a variant of its original");
            Require(WorkshopCatalog.Map(variant.transform, 0).GetTile((Vector3Int)b[0]) == tile.tile && !variant.GetComponent<NormalRoomEvent>().UsesRandomEnemies &&
                variant.GetComponent<NormalRoomEvent>().AuthoredWaves.Count == 1, "new room cells, fixed enemies and waves in the variant");
            Require(pool.roomEntries.Any(e => e.prefabs.Contains(variant) && e.prefabs.Contains(second.room.gameObject)), "variant joins its original's dungeon candidates");
            Require(catalog.FindRoom(AssetDatabase.AssetPathToGUID(madePath))?.inPool == true, "catalog lists the new prefab as a pool room");
            Require(!Directory.Exists(Folder(room)) && !File.Exists(Path.Combine(Folder("WORLD/POOLS/" + second.id), made.id + ".json")), "new room document and pool link removed");
            Require(!ProjectRoomBake.Plan().Any(i => i.document == room || i.document == documents.Keys.First()), "nothing left to bake");
        }
        finally
        {
            if (madePath != null && File.Exists(madePath)) AssetDatabase.DeleteAsset(madePath);
            foreach (var file in keep) { File.WriteAllBytes(file.Key, file.Value); AssetDatabase.ImportAsset(file.Key, ImportAssetOptions.ForceUpdate); }
            foreach (string document in new[] { room, blockerRoom }.Where(d => d != null).Concat(documents.Keys))
                if (Directory.Exists(Folder(document))) Directory.Delete(Folder(document), true);
            foreach (var (document, files) in documents)
                foreach (var file in files)
                {
                    string target = Path.Combine(Folder(document), file.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllText(target, file.Value);
                }
            if (slot != null) DeleteUp(slot);
            ProjectRooms.Reset();
        }
        Debug.Log("[ProjectRoomBakeCheck] PASS");
    }

    // 파일과 비게 된 상위 폴더(DATA 아래)를 지운다.
    private static void DeleteUp(string file)
    {
        if (File.Exists(file)) File.Delete(file);
        string root = Path.GetFullPath(ProjectContentStore.Root);
        for (var up = Directory.GetParent(file); up != null && up.FullName.Length > root.Length && up.Exists && !up.EnumerateFileSystemInfos().Any(); up = up.Parent)
            up.Delete();
    }
    private static bool Throws(Action action, string message)
    {
        try { action(); return false; }
        catch (Exception e) { return e.Message.Contains(message); }
    }
    private static void Require(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException("[ProjectRoomBakeCheck] " + what);
    }
}

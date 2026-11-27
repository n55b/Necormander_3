#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// What kind of room a prefab is. The tag decides the folder a saved room goes to.
internal enum RoomTag { Normal, Elite, Boss, Reward, Shop, Augment, Spawn, Tutorial }

// Saves workshop rooms as real room prefabs under "Room Map Prefabs/<Tag>/Variants".
// Registering a saved prefab in RoomPrefabData stays a manual step in the inspector.
public static class WorkshopPrefabs
{
    internal const string Root = "Assets/Prefabs/Map/Room Map Prefabs";
    internal static readonly RoomTag[] Tags = (RoomTag[])Enum.GetValues(typeof(RoomTag));
    // Set by the editor assembly; rebuilding the catalog needs editor-only code.
    public static Action RebuildCatalog;

#if UNITY_EDITOR
    internal static bool Available => true;
#else
    internal static bool Available => false;
#endif

    internal static string Label(RoomTag tag) => tag switch
    {
        RoomTag.Normal => "일반", RoomTag.Elite => "엘리트", RoomTag.Boss => "보스", RoomTag.Reward => "보상",
        RoomTag.Shop => "상점", RoomTag.Augment => "증강", RoomTag.Spawn => "시작", _ => "튜토리얼"
    };
    internal static string ShortLabel(RoomTag tag) => tag == RoomTag.Tutorial ? "튜토" : Label(tag);
    internal static string Folder(RoomTag tag) => tag == RoomTag.Tutorial ? Root + "/Tutorial" : Root + "/" + tag + "/Variants";
    private static string Prefix(RoomTag tag) => tag == RoomTag.Tutorial ? "Tutorial_" : "Room_" + tag;
    internal static string PathFor(RoomTag tag, string name) => Folder(tag) + "/" + name + ".prefab";
    private static RoomType? TypeFor(RoomTag tag) => tag switch
    {
        RoomTag.Normal => RoomType.Normal, RoomTag.Elite => RoomType.Elite, RoomTag.Boss => RoomType.Boss,
        RoomTag.Reward => RoomType.Reward, RoomTag.Shop => RoomType.Shop, RoomTag.Augment => RoomType.Augment,
        RoomTag.Spawn => RoomType.Spawn, _ => null
    };

    internal static string PathOf(WorkshopCatalog.Template template)
    {
#if UNITY_EDITOR
        return template?.room != null ? AssetDatabase.GetAssetPath(template.room.gameObject) : null;
#else
        return null;
#endif
    }

    internal static RoomTag TagOf(WorkshopCatalog.Template template)
    {
        if (template.tutorialOrder > 0) return RoomTag.Tutorial;
        string path = PathOf(template);
        if (!string.IsNullOrEmpty(path))
        {
            if (path.StartsWith(Root + "/Tutorial/", StringComparison.Ordinal)) return RoomTag.Tutorial;
            foreach (var tag in Tags)
                if (path.StartsWith(Root + "/" + tag + "/", StringComparison.Ordinal)) return tag;
        }
        return template.type switch
        {
            RoomType.Elite => RoomTag.Elite, RoomType.Boss => RoomTag.Boss, RoomType.Reward => RoomTag.Reward,
            RoomType.Shop or RoomType.EnhanceShop => RoomTag.Shop, RoomType.Augment => RoomTag.Augment,
            RoomType.Spawn => RoomTag.Spawn, _ => RoomTag.Normal
        };
    }

    internal static bool IsRegistered(WorkshopCatalog catalog, WorkshopCatalog.Template template) =>
        catalog.roomPool != null && template.room != null &&
        catalog.roomPool.roomEntries.Any(entry => entry.prefabs != null && entry.prefabs.Contains(template.room.gameObject));

    // The base a new empty room borrows its doors, tilemap layers and room events from.
    internal static WorkshopCatalog.Template BaseFor(WorkshopCatalog catalog, RoomTag tag) =>
        catalog.rooms.Where(t => TagOf(t) == tag).OrderByDescending(t => t.inPool)
            .ThenBy(t => t.title, NaturalOrder.Instance).FirstOrDefault();

    // "Room_Normal 9" → "Room_Normal 10"; "Tutorial_07" → "Tutorial_08". A bare "Room_Elite" counts as 1.
    internal static string SuggestName(RoomTag tag, WorkshopCatalog catalog)
    {
        string prefix = Prefix(tag);
        var names = catalog.rooms.Select(t => t.title).ToList();
#if UNITY_EDITOR
        if (AssetDatabase.IsValidFolder(Folder(tag)))
            names.AddRange(AssetDatabase.FindAssets("t:Prefab", new[] { Folder(tag) })
                .Select(guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid))));
#endif
        int highest = 0;
        var pattern = new Regex("^" + Regex.Escape(prefix) + (tag == RoomTag.Tutorial ? @"(\d+)$" : @"(?: (\d+))?$"));
        foreach (string name in names)
        {
            var match = pattern.Match(name);
            if (!match.Success) continue;
            highest = Math.Max(highest, match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 1);
        }
        return tag == RoomTag.Tutorial ? prefix + (highest + 1).ToString("D2") : prefix + " " + (highest + 1);
    }

    internal static string CheckName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "방 이름을 입력하세요.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.StartsWith("."))
            return "이름에 쓸 수 없는 문자가 있습니다.";
        return null;
    }

    // Half the distance between opposite exits, in cells. Medium matches the average normal room.
    internal static readonly (string label, int x, int y)[] Sizes = { ("작게", 22, 15), ("보통", 30, 22), ("크게", 40, 28) };

    // A fresh document over a base template: every authored tile erased, every editable object removed,
    // and one exit per side laid out as an exact cross around cell (0, 0).
    internal static WorkshopRoom Empty(WorkshopCatalog.Template template, string title, int size = 1)
    {
        var doc = WorkshopRoom.Create(template);
        doc.title = title;
        var root = template.room.transform;
        doc.clearTiles = true;
        doc.singleDoors = true;
        var ground = WorkshopCatalog.Map(root, 0);
        if (ground != null)
        {
            // New rooms are centred on cell (0, 0) so the cursor reads X 0 Y 0 at the middle of the cross.
            var center = Vector2Int.zero;
            var cellSize = ground.GetComponentInParent<Grid>()?.cellSize ?? Vector3.one;
            Vector3 Local(Vector2Int cell) => root.InverseTransformPoint(ground.transform.TransformPoint(
                new Vector3((cell.x + .5f) * cellSize.x, (cell.y + .5f) * cellSize.y, 0)));
            var reach = Sizes[Mathf.Clamp(size, 0, Sizes.Length - 1)];
            var existing = new HashSet<Vector2Int>(root.GetComponentsInChildren<RoomAnchor>(true).Select(a => a.direction));
            foreach (var direction in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
            {
                // Tutorial rooms are chained in a fixed order; they keep only the sides their base already uses.
                if (template.tutorialOrder > 0 && !existing.Contains(direction)) continue;
                doc.SetDoor(direction, Local(center + new Vector2Int(direction.x * reach.x, direction.y * reach.y)));
            }
        }
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
        {
            if (!WorkshopGeometry.IsEditablePiece(root, node)) continue;
            string key = WorkshopCatalog.PathOf(root, node);
            doc.pieces.Add(new WorkshopRoom.Piece { id = key, source = key, local = root.InverseTransformPoint(node.position), removed = true });
        }
        return doc;
    }

    internal static string Save(WorkshopRoom doc, WorkshopCatalog catalog, RoomTag tag, string name)
    {
#if UNITY_EDITOR
        string problem = CheckName(name);
        if (problem != null) throw new IOException(problem);
        name = name.Trim();
        var template = catalog.FindRoom(doc.template) ?? throw new IOException("원본 방을 찾을 수 없습니다.");
        string source = PathOf(template);
        string target = PathFor(tag, name);
        if (target != source && File.Exists(target)) throw new IOException("같은 이름의 방이 이미 있습니다 · " + target);
        doc.Validate(catalog);
        EnsureFolder(Folder(tag));
        var root = PrefabUtility.LoadPrefabContents(source);
        try
        {
            Apply(doc, root, catalog, tag);
            PrefabUtility.SaveAsPrefabAsset(root, target, out bool saved);
            if (!saved) throw new IOException("프리팹을 저장하지 못했습니다 · " + target);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        RebuildCatalog?.Invoke();
        return target;
#else
        throw new InvalidOperationException("프리팹 저장은 Unity 에디터에서 사용할 수 있습니다.");
#endif
    }

    internal static void Delete(WorkshopCatalog.Template template)
    {
#if UNITY_EDITOR
        string path = PathOf(template) ?? throw new IOException("삭제할 프리팹을 찾을 수 없습니다.");
        if (!AssetDatabase.MoveAssetToTrash(path)) throw new IOException("프리팹을 삭제하지 못했습니다 · " + path);
        WorkshopDrafts.Delete(template.id);
        RebuildCatalog?.Invoke();
#else
        throw new InvalidOperationException("프리팹 삭제는 Unity 에디터에서 사용할 수 있습니다.");
#endif
    }

    internal static void Reveal(string path)
    {
#if UNITY_EDITOR
        var asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null) return;
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
#endif
    }

#if UNITY_EDITOR
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    private static void Apply(WorkshopRoom doc, GameObject root, WorkshopCatalog catalog, RoomTag tag)
    {
        var room = root.GetComponent<RoomInstance>();
        var origin = root.transform;
        WorkshopGeometry.ApplyTiles(doc, origin, catalog);
        foreach (var exit in doc.exits)
        {
            var anchor = root.GetComponentsInChildren<RoomAnchor>().FirstOrDefault(a => a.direction == exit.direction);
            if (anchor == null)
            {
                anchor = new GameObject("Workshop door").AddComponent<RoomAnchor>();
                anchor.transform.SetParent(origin, false);
                anchor.direction = exit.direction;
            }
            anchor.transform.position = origin.TransformPoint(exit.local);
        }
        // Resolve every authored object before anything is added or removed: ids are sibling paths.
        var removed = new List<GameObject>();
        foreach (var piece in doc.pieces.Where(p => !string.IsNullOrEmpty(p.source)))
        {
            var target = WorkshopCatalog.Resolve(origin, piece.source);
            if (target == null) continue;
            if (piece.removed) removed.Add(target.gameObject);
            else target.position = origin.TransformPoint(piece.local);
        }
        Transform Group(string name)
        {
            var group = origin.Find(name);
            if (group != null) return group;
            group = new GameObject(name).transform;
            group.SetParent(origin, false);
            return group;
        }
        var combat = root.GetComponent<NormalRoomEvent>();
        Transform encounter = null;
        bool createdEncounter = false;
        foreach (var piece in doc.pieces.Where(p => string.IsNullOrEmpty(p.source) && !p.removed))
        {
            var item = catalog.FindPiece(piece.catalog);
            if (item?.prefab == null) continue;
            Transform parent;
            if (item.enemy)
            {
                if (encounter == null)
                {
                    string name = tag == RoomTag.Tutorial ? "Tutorial Enemies" : "Workshop encounter";
                    createdEncounter = origin.Find(name) == null;
                    encounter = Group(name);
                }
                parent = encounter;
            }
            else
            {
                bool trap = AssetDatabase.GetAssetPath(item.prefab).StartsWith("Assets/Prefabs/Traps/", StringComparison.Ordinal);
                parent = Group(trap ? "TrapObject" : "DecorateObject");
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(item.prefab, parent);
            instance.transform.position = origin.TransformPoint(piece.local);
        }
        // Sibling paths are resolved above, so objects can now be removed safely.
        if (doc.singleDoors) removed.AddRange(WorkshopGeometry.ExtraAnchors(origin).Select(anchor => anchor.gameObject));
        foreach (var gone in removed)
        {
            if (gone == null) continue;
            // An object inside a nested prefab cannot be destroyed from the outer prefab; hiding keeps the edit.
            try { UnityEngine.Object.DestroyImmediate(gone); }
            catch (InvalidOperationException) { if (gone != null) gone.SetActive(false); }
        }
        if (doc.reward > 0) room.SetAuthoredReward((RoomInstance.NormalRewardType)(doc.reward - 1));
        if (doc.waves.Count > 0 && combat != null)
            combat.ConfigureWaves(doc.waves.Select(id => catalog.FindCluster(id).asset).ToArray());
        if (createdEncounter && combat != null && tag != RoomTag.Tutorial) combat.SetWorkshopEnemies(encounter);
        var type = TypeFor(tag);
        if (type.HasValue) room.roomType = type.Value;
    }
#endif
}

// One recovery slot per room, overwritten while editing and removed by a real save.
// Kept under the project's Library folder so it never reaches Git and never piles up.
internal static class WorkshopDrafts
{
    internal sealed class Entry
    {
        internal WorkshopRoom doc;
        internal DateTime time;
        internal string error;
    }
    internal static string Folder => TestFolder ?? Path.Combine(ProjectDataConnection.Checkout ?? Application.persistentDataPath, "Library", "LevelWorkshop", "Drafts");
    // Set only by the editor self check, so its throwaway room never lands next to the user's drafts.
    internal static string TestFolder;
    internal static string PathFor(string id) =>
        Guid.TryParseExact(id, "N", out _) ? Path.Combine(Folder, id + ".json") : throw new InvalidDataException("방 ID가 유효하지 않습니다.");
    internal static bool Exists(string id) => File.Exists(PathFor(id));
    internal static void Write(string id, string json) => ProjectContentStore.ReplaceFile(PathFor(id), json);
    internal static void Delete(string id)
    {
        string path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }

    internal static List<Entry> List(WorkshopCatalog catalog)
    {
        var list = new List<Entry>();
        if (!Directory.Exists(Folder)) return list;
        foreach (string path in Directory.GetFiles(Folder, "*.json"))
        {
            var entry = new Entry { time = File.GetLastWriteTimeUtc(path) };
            try
            {
                var doc = JsonUtility.FromJson<WorkshopRoom>(File.ReadAllText(path)) ?? throw new InvalidDataException("임시 저장을 읽을 수 없습니다.");
                // A small change to the base prefab should not lock the work away; the checks below still apply.
                var template = catalog.FindRoom(doc.template) ?? throw new InvalidDataException("원본 방이 삭제되었습니다.");
                doc.baseRevision = template.revision;
                doc.Validate(catalog);
                entry.doc = doc;
            }
            catch (Exception error)
            {
                entry.error = error.Message;
                entry.doc = new WorkshopRoom { id = Path.GetFileNameWithoutExtension(path), title = Path.GetFileNameWithoutExtension(path) };
            }
            list.Add(entry);
        }
        return list.OrderByDescending(e => e.time).ToList();
    }

    internal static string NextNewName(IEnumerable<Entry> drafts)
    {
        int highest = 0;
        foreach (var entry in drafts)
        {
            var match = Regex.Match(entry.doc?.title ?? "", @"^NewMap(\d+)$");
            if (match.Success) highest = Math.Max(highest, int.Parse(match.Groups[1].Value));
        }
        return "NewMap" + (highest + 1).ToString("D2");
    }
}

// "Room_Normal 2" sorts before "Room_Normal 10".
internal sealed class NaturalOrder : IComparer<string>
{
    internal static readonly NaturalOrder Instance = new();
    public int Compare(string a, string b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                int byValue = long.Parse(a.Substring(si, Math.Min(18, i - si))).CompareTo(long.Parse(b.Substring(sj, Math.Min(18, j - sj))));
                if (byValue != 0) return byValue;
            }
            else
            {
                int byChar = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (byChar != 0) return byChar;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
#endif

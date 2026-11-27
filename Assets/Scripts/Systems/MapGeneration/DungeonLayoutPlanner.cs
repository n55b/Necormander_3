using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

/// <summary>Reads authored room anchors and chooses a layout before any scene objects are created.</summary>
public sealed class DungeonLayoutPlanner
{
    private static readonly Vector2Int[] Cardinal = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
    private readonly MapGenerationDataSO settings;
    private readonly RoomPrefabDataSO catalog;
    private readonly Dictionary<GameObject, Vector2Int[]> directionsByPrefab = new();

    public DungeonLayoutPlanner(MapGenerationDataSO settings, RoomPrefabDataSO catalog)
        => (this.settings, this.catalog) = (settings, catalog);

    public bool TryPlan(DungeonLayoutMode mode, int floor, GameObject elitePrefab, Random random,
        string diagnosticContext, out DungeonLayout layout, out string error)
    {
        var rooms = new List<DungeonLayout.Room>();
        var occupied = new HashSet<Vector2Int>();
        layout = null;
        if (settings == null || (mode != DungeonLayoutMode.Tutorial && catalog == null))
        {
            error = "Map generation settings or the room catalog are missing.";
            return false;
        }
        error = mode == DungeonLayoutMode.Tutorial
            ? PlaceTutorial(rooms, occupied)
            : PlaceFloor(mode, floor, elitePrefab, random, diagnosticContext, rooms, occupied);
        if (error != null) return false;

        var doors = new List<DungeonLayout.Door>();
        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                var direction = rooms[j].Cell - rooms[i].Cell;
                if (RoomConnectivity.IsCardinal(direction) && rooms[i].Doors.Contains(direction) &&
                    rooms[j].Doors.Contains(-direction))
                    doors.Add(new DungeonLayout.Door(i, j));
            }
        }

        if (!HasAccess(rooms, doors, stopAtEncounters: mode is DungeonLayoutMode.Normal or DungeonLayoutMode.Boss))
        {
            error = "A planned room cannot be reached from the spawn through paired doors.";
            return false;
        }
        layout = new DungeonLayout(mode, rooms, doors);
        return true;
    }

    private Vector2Int[] Directions(GameObject prefab)
    {
        if (!directionsByPrefab.TryGetValue(prefab, out var directions))
        {
            directions = prefab.GetComponentsInChildren<RoomAnchor>().Select(anchor => anchor.direction)
                .Where(RoomConnectivity.IsCardinal).Distinct().ToArray();
            directionsByPrefab.Add(prefab, directions);
        }
        return directions;
    }

    private string PlaceTutorial(List<DungeonLayout.Room> rooms, HashSet<Vector2Int> occupied)
    {
        var tutorial = settings.tutorialRooms;
        if (tutorial == null || tutorial.Count == 0) return "The tutorial has no authored rooms.";
        var cell = Vector2Int.zero;
        var entrance = Vector2Int.zero;
        for (int i = 0; i < tutorial.Count; i++)
        {
            var prefab = tutorial[i];
            if (prefab == null) return $"Tutorial room {i + 1} has no prefab.";
            var directions = Directions(prefab);
            if (i > 0 && !directions.Contains(entrance))
                return $"Tutorial room '{prefab.name}' needs an entrance facing {entrance}.";
            if (!occupied.Add(cell)) return $"Tutorial room '{prefab.name}' overlaps cell {cell}.";
            var authored = prefab.GetComponent<RoomInstance>();
            var type = i == 0 ? RoomType.Spawn : authored != null ? authored.roomType : RoomType.Normal;
            rooms.Add(new DungeonLayout.Room(cell, type, prefab, directions));
            if (i == tutorial.Count - 1) break;

            var exit = directions.FirstOrDefault(direction => direction != entrance);
            if (exit == default) return $"Tutorial room '{prefab.name}' needs an exit.";
            cell += exit;
            entrance = -exit;
        }
        return null;
    }

    private string PlaceFloor(DungeonLayoutMode mode, int floor, GameObject elitePrefab, Random random, string context,
        List<DungeonLayout.Room> rooms, HashSet<Vector2Int> occupied)
    {
        var spawns = catalog.GetValidPrefabs(RoomType.Spawn, context);
        if (mode == DungeonLayoutMode.EliteArena)
            spawns.RemoveAll(prefab => !Directions(prefab).Contains(Vector2Int.up));
        if (spawns.Count == 0) return "No compatible spawn prefab.";
        AddRoom(rooms, occupied, Vector2Int.zero, RoomType.Spawn, spawns[random.Next(spawns.Count)]);

        if (mode == DungeonLayoutMode.EliteArena)
        {
            var elite = elitePrefab;
            if (elite == null || !Directions(elite).Contains(Vector2Int.down))
            {
                var candidates = catalog.GetValidPrefabs(RoomType.Elite, context);
                candidates.RemoveAll(prefab => !Directions(prefab).Contains(Vector2Int.down));
                if (candidates.Count == 0) return "No elite prefab with a south door for the arena.";
                elite = candidates[random.Next(candidates.Count)];
            }
            AddRoom(rooms, occupied, Vector2Int.up, RoomType.Elite, elite);
            return null;
        }

        var counts = settings.GetRoomCountsForFloor(floor);
        var special = GetSpecialRoomTypes(counts, mode == DungeonLayoutMode.Boss);
        int normalCount = Mathf.Max(counts.minNormal, counts.total - special.Count - 1);
        var used = new HashSet<GameObject>();
        if (normalCount > 0)
        {
            var candidates = catalog.GetValidPrefabs(RoomType.Normal, context);
            candidates.RemoveAll(prefab => prefab.GetComponent<RoomInstance>() == null);
            for (int i = 0; i < normalCount; i++)
            {
                if (!TryAddRoom(RoomType.Normal, candidates, null, rooms, occupied, used, random))
                    return $"Normal room {i + 1} of {normalCount} has no compatible open door.";
            }
        }
        foreach (var type in special)
        {
            var candidates = catalog.GetValidPrefabs(type, context);
            var preferred = type == RoomType.Elite ? elitePrefab : null;
            if (!TryAddRoom(type, candidates, preferred, rooms, occupied, used, random))
                return $"No compatible open door for {type}.";
        }
        return null;
    }

    internal static List<RoomType> GetSpecialRoomTypes(MapGenerationDataSO.RoomCounts counts, bool boss)
    {
        var types = new List<RoomType>();
        for (int i = 0; i < counts.shop; i++) types.Add(RoomType.Shop);
        for (int i = 0; i < counts.enhanceShop; i++) types.Add(RoomType.EnhanceShop);
        for (int i = 0; i < counts.elite - (boss ? 1 : 0); i++) types.Add(RoomType.Elite);
        if (boss) types.Add(RoomType.Boss);
        for (int i = 0; i < counts.reward; i++) types.Add(RoomType.Reward);
        for (int i = 0; i < counts.augment; i++) types.Add(RoomType.Augment);
        return types;
    }

    private bool TryAddRoom(RoomType type, List<GameObject> prefabs, GameObject preferred,
        List<DungeonLayout.Room> rooms, HashSet<Vector2Int> occupied, HashSet<GameObject> used, Random random)
    {
        var sites = new List<(Vector2Int cell, Vector2Int entrance, bool deadEnd)>();
        foreach (var parent in rooms)
        {
            if (parent.Type is not (RoomType.Spawn or RoomType.Normal)) continue;
            foreach (var direction in parent.Doors)
            {
                var cell = parent.Cell + direction;
                if (!occupied.Contains(cell))
                    sites.Add((cell, -direction, Cardinal.Count(d => occupied.Contains(cell + d)) == 1));
            }
        }
        bool special = type != RoomType.Normal;
        // Retain dead-end preference and prefer the outermost matching cell for the boss.
        var ordered = sites.OrderByDescending(site => special && site.deadEnd)
            .ThenBy(site => type == RoomType.Boss ? -Mathf.Abs(site.cell.x) - Mathf.Abs(site.cell.y) : random.Next())
            .ToArray();
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var site in ordered)
            {
                var selected = preferred;
                if (selected == null || (pass == 0 && used.Contains(selected)) || !Directions(selected).Contains(site.entrance))
                {
                    var matches = prefabs.Where(prefab => (pass != 0 || !used.Contains(prefab)) &&
                        Directions(prefab).Contains(site.entrance)).ToArray();
                    if (matches.Length == 0) continue;
                    selected = matches[random.Next(matches.Length)];
                }
                AddRoom(rooms, occupied, site.cell, type, selected);
                if (!used.Add(selected) && type == RoomType.Normal)
                    Debug.LogWarning($"[MapGenerator] 같은 층에 '{selected.name}' 맵을 다시 사용합니다. 안 쓴 맵 중 문 방향이 맞는 후보가 없습니다.");
                return true;
            }
        }
        return false;
    }

    private void AddRoom(List<DungeonLayout.Room> rooms, HashSet<Vector2Int> occupied, Vector2Int cell, RoomType type, GameObject prefab)
    {
        occupied.Add(cell);
        rooms.Add(new DungeonLayout.Room(cell, type, prefab, Directions(prefab)));
    }

    private static bool HasAccess(List<DungeonLayout.Room> rooms, List<DungeonLayout.Door> doors, bool stopAtEncounters)
    {
        var neighbors = rooms.Select(_ => new List<int>()).ToArray();
        foreach (var door in doors)
        {
            neighbors[door.From].Add(door.To);
            neighbors[door.To].Add(door.From);
        }
        var visited = new HashSet<int> { 0 };
        var queue = new Queue<int>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            if (stopAtEncounters && RoomConnectivity.IsTerminalEncounter(rooms[index].Type)) continue;
            foreach (int next in neighbors[index])
                if (visited.Add(next)) queue.Enqueue(next);
        }
        return visited.Count == rooms.Count;
    }
}

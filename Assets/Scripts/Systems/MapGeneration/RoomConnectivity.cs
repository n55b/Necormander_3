using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class RoomConnectivity
{
    internal static bool IsCardinal(Vector2Int direction) => Mathf.Abs(direction.x) + Mathf.Abs(direction.y) == 1;
    internal static bool IsTerminalEncounter(RoomType type) => type is RoomType.Elite or RoomType.Boss;

    public static bool TryGetFacingAnchors(RoomInstance a, RoomInstance b, bool requireUnused,
        out RoomAnchor anchorA, out RoomAnchor anchorB)
    {
        anchorA = anchorB = null;
        if (a == null || b == null) return false;

        Vector2Int direction = b.gridPosition - a.gridPosition;
        if (!IsCardinal(direction)) return false;

        anchorA = a.anchors.FirstOrDefault(anchor => anchor != null &&
            anchor.direction == direction && (!requireUnused || !anchor.isUsed));
        anchorB = b.anchors.FirstOrDefault(anchor => anchor != null &&
            anchor.direction == -direction && (!requireUnused || !anchor.isUsed));
        return anchorA != null && anchorB != null;
    }

    public static Dictionary<RoomInstance, List<RoomInstance>> PreviewDoorGraph(IReadOnlyList<RoomInstance> rooms)
    {
        var graph = new Dictionary<RoomInstance, List<RoomInstance>>();
        foreach (var room in rooms)
            if (room != null) graph[room] = new List<RoomInstance>();

        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                if (!TryGetFacingAnchors(rooms[i], rooms[j], requireUnused: false, out _, out _)) continue;
                graph[rooms[i]].Add(rooms[j]);
                graph[rooms[j]].Add(rooms[i]);
            }
        }
        return graph;
    }

    public static bool HasRequiredAccess(IReadOnlyList<RoomInstance> rooms,
        IReadOnlyDictionary<RoomInstance, List<RoomInstance>> graph, bool requiresBoss)
    {
        var spawn = rooms.FirstOrDefault(room => room != null && room.roomType == RoomType.Spawn);
        if (spawn == null) return false;

        var visited = new HashSet<RoomInstance> { spawn };
        var queue = new Queue<RoomInstance>();
        queue.Enqueue(spawn);
        while (queue.Count > 0)
        {
            if (!graph.TryGetValue(queue.Dequeue(), out var neighbors)) continue;
            foreach (var neighbor in neighbors)
            {
                if (neighbor == null || !visited.Add(neighbor)) continue;
                // Elite and boss rooms can be reached. Required routes cannot pass through them.
                if (IsTerminalEncounter(neighbor.roomType)) continue;
                queue.Enqueue(neighbor);
            }
        }

        int bossCount = 0;
        foreach (var room in rooms)
        {
            if (room == null) continue;
            if (room.roomType == RoomType.Boss) bossCount++;
            if ((requiresBoss || room.roomType is RoomType.Shop or RoomType.EnhanceShop or RoomType.Reward) &&
                !visited.Contains(room)) return false;
        }
        return !requiresBoss || bossCount == 1;
    }
}

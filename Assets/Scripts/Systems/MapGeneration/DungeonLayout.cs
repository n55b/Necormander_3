using System.Collections.Generic;
using UnityEngine;

public enum DungeonLayoutMode { Tutorial, Normal, EliteArena, Boss }

public sealed class DungeonLayout
{
    public readonly struct Room
    {
        public readonly Vector2Int Cell;
        public readonly RoomType Type;
        public readonly GameObject Prefab;
        internal readonly IReadOnlyList<Vector2Int> Doors;

        internal Room(Vector2Int cell, RoomType type, GameObject prefab, IReadOnlyList<Vector2Int> doors)
            => (Cell, Type, Prefab, Doors) = (cell, type, prefab, doors);
    }

    public readonly struct Door
    {
        public readonly int From;
        public readonly int To;

        internal Door(int from, int to) => (From, To) = (from, to);
    }

    public DungeonLayoutMode Mode { get; }
    public IReadOnlyList<Room> Rooms { get; }
    public IReadOnlyList<Door> Doors { get; }

    internal DungeonLayout(DungeonLayoutMode mode, List<Room> rooms, List<Door> doors)
        => (Mode, Rooms, Doors) = (mode, rooms.AsReadOnly(), doors.AsReadOnly());
}

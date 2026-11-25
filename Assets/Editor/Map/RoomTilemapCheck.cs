using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>원본 방 정렬 및 복구한 타일의 로드/병합 회귀 검사. 에셋/현재 씬을 저장하지 않는다.</summary>
public static class RoomTilemapCheck
{
    private const string Rooms = "Assets/Prefabs/Map/Room Map Prefabs";

    [MenuItem("Tools/Diagnostics/Verify Room Tilemaps")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        int rooms = 0, renderers = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Rooms }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab.GetComponent<RoomInstance>() == null) continue;
            rooms++;
            foreach (var renderer in prefab.GetComponentsInChildren<TilemapRenderer>(true))
            {
                string layer; int order;
                switch (renderer.name)
                {
                    case "Ground": layer = "Ground"; order = 0; break;
                    case "Wall": layer = "Ground"; order = 2; break;
                    case "Shadow": layer = "Shadow"; order = 0; break;
                    case "Unsteppable": layer = "Ground"; order = -1; break;
                    default: continue; // Object 등 비병합 맵에는 역할별 규칙을 강요하지 않는다.
                }
                Check(renderer.sortingLayerName == layer && renderer.sortingOrder == order,
                    prefab.name + "/" + renderer.name + ": sorting");
                renderers++;
            }
        }

        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var shop = Spawn(Rooms + "/Shop/Variants/Room_ShopEnhance.prefab", preview);
            VerifyMerge(shop.GetComponent<RoomInstance>(), new[] { 1487, 752, 383, 0 }, preview);
            var normal = Spawn(Rooms + "/Normal/Variants/Room_Normal.prefab", preview).GetComponent<RoomInstance>();
            var shadows = normal.shadowTilemap;
            var decoratedCell = OccupiedCells(shadows).First(cell => shadows.GetTile(cell) is Tile);
            shadows.SetTile(new TileChangeData(decoratedCell, shadows.GetTile(decoratedCell), Color.magenta,
                Matrix4x4.Scale(new Vector3(-1, 1, 1))), ignoreLockFlags: true);
            VerifyMerge(normal, null, preview, requireTint: true);
            var tutorial = Spawn(Rooms + "/Tutorial/Tutorial_04.prefab", preview);
            var room = tutorial.GetComponent<RoomInstance>();
            Check(room.shadowTilemap == null, "Tutorial_04: no Object-as-Shadow reference");
            var objectMap = tutorial.GetComponentsInChildren<Tilemap>(true).Single(t => t.name == "Object");
            Check(objectMap.gameObject.activeSelf && objectMap.GetComponent<Collider2D>() != null,
                "Tutorial_04: Object and collider preserved");
            VerifyMerge(room, null, preview);
            Check(objectMap.gameObject.activeSelf, "Tutorial_04: merge must not disable Object");
            VerifyDoorGraph(preview);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        Debug.Log($"[RoomTilemapCheck] PASS: {rooms} room prefabs / {renderers} terrain renderers; " +
            "ShopEnhance 1487 Ground / 752 Wall / 383 Shadow cells with persistent tiles/sprites; " +
            "ShopEnhance, Normal and Tutorial_04 stamping and erasure; authored tint, objects and door rules preserved.");
    }

    private static GameObject Spawn(string path, Scene scene) =>
        (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);

    private static void VerifyMerge(RoomInstance room, int[] expectedCounts, Scene preview, bool requireTint = false)
    {
        var all = room.GetComponentsInChildren<Tilemap>(true);
        // Initialize도 null일 때만 동일 이름으로 자동 연결한다. 런타임 전투/콜라이더 초기화는 실행하지 않는다.
        if (room.unsteppableTilemap == null)
            room.unsteppableTilemap = all.FirstOrDefault(t => t.name == "Unsteppable");
        var sources = new[] { room.groundTilemap, room.wallTilemap, room.shadowTilemap, room.unsteppableTilemap };
        var targets = new Tilemap[4];
        var positions = new Vector3Int[4][];
        for (int i = 0; i < sources.Length; i++)
        {
            var gridRoot = new GameObject("CheckGrid", typeof(Grid));
            SceneManager.MoveGameObjectToScene(gridRoot, preview);
            var source = sources[i];
            if (source != null)
            {
                var sourceGrid = source.layoutGrid;
                gridRoot.transform.SetPositionAndRotation(sourceGrid.transform.position, sourceGrid.transform.rotation);
                gridRoot.transform.localScale = sourceGrid.transform.lossyScale;
                var grid = gridRoot.GetComponent<Grid>();
                grid.cellSize = sourceGrid.cellSize;
                grid.cellGap = sourceGrid.cellGap;
                grid.cellLayout = sourceGrid.cellLayout;
                grid.cellSwizzle = sourceGrid.cellSwizzle;
            }
            var target = new GameObject("CheckTarget", typeof(Tilemap)).GetComponent<Tilemap>();
            SceneManager.MoveGameObjectToScene(target.gameObject, preview);
            target.transform.SetParent(gridRoot.transform, false);
            targets[i] = target;
            positions[i] = source == null ? Array.Empty<Vector3Int>() :
                OccupiedCells(source).ToArray();
            if (expectedCounts != null) Check(positions[i].Length == expectedCounts[i], room.name + ": cell count " + i);
            foreach (var pos in positions[i])
            {
                var tile = source.GetTile(pos);
                Check(tile != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tile)), room.name + ": missing tile at " + pos);
                var sprite = source.GetSprite(pos);
                Check(sprite != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(sprite)), room.name + ": missing sprite at " + pos);
            }
        }
        bool foundTint = false;
        room.MergeTilesToGlobal(targets[0], targets[1], targets[2], targets[3]);
        for (int i = 0; i < sources.Length; i++)
        {
            foreach (var pos in positions[i])
            {
                var destination = targets[i].WorldToCell(sources[i].CellToWorld(pos));
                var tile = sources[i].GetTile(pos);
                Check(targets[i].GetTile(destination) == tile, room.name + ": stamp mismatch at " + pos);
                var color = sources[i].GetColor(pos);
                foundTint |= color != Color.white;
                Check(targets[i].GetColor(destination) == color, room.name + ": cell tint mismatch at " + pos);
                if (tile is Tile)
                {
                    Check(targets[i].GetTransformMatrix(destination) == sources[i].GetTransformMatrix(pos),
                        room.name + ": cell transform mismatch at " + pos);
                }
            }
            Check(OccupiedCells(targets[i]).Count() == positions[i].Length,
                room.name + ": stamp must not collapse cells " + i);
        }
        if (requireTint) Check(foundTint, room.name + ": expected at least one authored cell tint");

        int populatedLayer = Array.FindIndex(positions, cells => cells.Length > 0);
        Check(populatedLayer >= 0, room.name + ": at least one terrain layer to erase");
        var populatedMap = targets[populatedLayer];
        var edge = populatedMap.cellBounds.max;
        var sentinel = new Vector3Int(edge.x + 2, edge.y + 2, positions[populatedLayer][0].z);
        var sentinelTile = populatedMap.GetTile(positions[populatedLayer][0]);
        populatedMap.SetTile(sentinel, sentinelTile);
        room.EraseTilesFromGlobal(targets[0], targets[1], targets[2], targets[3]);
        Check(OccupiedCells(populatedMap).SequenceEqual(new[] { sentinel }) && populatedMap.GetTile(sentinel) == sentinelTile,
            room.name + ": erase preserves cells outside the room");
        for (int i = 0; i < targets.Length; i++)
            if (i != populatedLayer)
                Check(!OccupiedCells(targets[i]).Any(), room.name + ": erase removes stamped tiles " + i);
    }

    private static void VerifyDoorGraph(Scene preview)
    {
        RoomInstance CreateRoom(RoomType type, int x, params Vector2Int[] directions)
        {
            var root = new GameObject(type.ToString());
            SceneManager.MoveGameObjectToScene(root, preview);
            var room = root.AddComponent<RoomInstance>();
            room.roomType = type;
            room.gridPosition = new Vector2Int(x, 0);
            foreach (var direction in directions)
            {
                var child = new GameObject("Door Anchor");
                child.transform.SetParent(root.transform, false);
                var anchor = child.AddComponent<RoomAnchor>();
                anchor.direction = direction;
                room.anchors.Add(anchor);
            }
            return room;
        }

        var spawn = CreateRoom(RoomType.Spawn, 0, Vector2Int.right);
        var middle = CreateRoom(RoomType.Normal, 1, Vector2Int.left, Vector2Int.right);
        var exit = CreateRoom(RoomType.Shop, 2, Vector2Int.up);
        var rooms = new[] { spawn, middle, exit };
        Check(!RoomConnectivity.HasRequiredAccess(rooms, RoomConnectivity.PreviewDoorGraph(rooms), false),
            "A neighboring shop requires a facing door anchor");
        exit.anchors[0].direction = Vector2Int.left;
        var graph = RoomConnectivity.PreviewDoorGraph(rooms);
        Check(RoomConnectivity.HasRequiredAccess(rooms, graph, false), "The shop is reachable through a normal room");
        exit.roomType = RoomType.Boss;
        Check(RoomConnectivity.HasRequiredAccess(rooms, graph, true), "A boss floor has one reachable boss");
        exit.roomType = RoomType.Shop;
        middle.roomType = RoomType.Elite;
        Check(!RoomConnectivity.HasRequiredAccess(rooms, graph, false), "A required route cannot cross an elite room");
        middle.anchors[1].isUsed = true;
        Check(!RoomConnectivity.TryGetFacingAnchors(middle, exit, true, out _, out _),
            "Live doors cannot reuse a consumed anchor");
        Check(RoomConnectivity.PreviewDoorGraph(rooms)[middle].Contains(exit),
            "The preview is independent of temporary anchor usage");
    }

    private static IEnumerable<Vector3Int> OccupiedCells(Tilemap map)
    {
        foreach (var cell in map.cellBounds.allPositionsWithin)
            if (map.HasTile(cell)) yield return cell;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[RoomTilemapCheck] FAIL: " + message);
    }
}

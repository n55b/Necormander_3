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
            var shop = Spawn(Rooms + "/Shop/Test/Room_ShopEnhance.prefab", preview);
            VerifyMerge(shop.GetComponent<RoomInstance>(), new[] { 1487, 752, 383, 0 }, preview);
            var tutorial = Spawn(Rooms + "/TestTutorial/Tutorial_04.prefab", preview);
            var room = tutorial.GetComponent<RoomInstance>();
            Check(room.shadowTilemap == null, "Tutorial_04: no Object-as-Shadow reference");
            var objectMap = tutorial.GetComponentsInChildren<Tilemap>(true).Single(t => t.name == "Object");
            Check(objectMap.gameObject.activeSelf && objectMap.GetComponent<Collider2D>() != null,
                "Tutorial_04: Object and collider preserved");
            VerifyMerge(room, null, preview);
            Check(objectMap.gameObject.activeSelf, "Tutorial_04: merge must not disable Object");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        Debug.Log($"[RoomTilemapCheck] PASS: {rooms} room prefabs / {renderers} terrain renderers; " +
            "ShopEnhance 1487 Ground / 752 Wall / 383 Shadow cells with persistent tiles/sprites; " +
            "ShopEnhance + Tutorial_04 global stamping; Object preserved. No assets/scenes saved.");
    }

    private static GameObject Spawn(string path, Scene scene) =>
        (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);

    private static void VerifyMerge(RoomInstance room, int[] expectedCounts, Scene preview)
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
        room.MergeTilesToGlobal(targets[0], targets[1], targets[2], targets[3]);
        for (int i = 0; i < sources.Length; i++)
        {
            foreach (var pos in positions[i])
            {
                var destination = targets[i].WorldToCell(sources[i].CellToWorld(pos));
                Check(targets[i].GetTile(destination) == sources[i].GetTile(pos), room.name + ": stamp mismatch at " + pos);
            }
            Check(OccupiedCells(targets[i]).Count() == positions[i].Length,
                room.name + ": stamp must not collapse cells " + i);
        }
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

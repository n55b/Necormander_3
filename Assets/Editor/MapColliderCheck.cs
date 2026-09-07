using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;

/// <summary>실제 Wall 타일로 이전/현재 충돌 경계와 생성 시간을 비교한다. 씬/에셋은 저장하지 않는다.</summary>
public static class MapColliderCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Map/Verify Wall Collider Build")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        var scene = EditorSceneManager.NewPreviewScene();
        var previousInstance = MapGenerator.Instance;
        try
        {
            var host = new GameObject("MapColliderCheck");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.SetActive(false);
            var generator = host.AddComponent<MapGenerator>();
            typeof(MapGenerator).GetField("logGenerationTimings", Private).SetValue(generator, false);
            MapGenerator.Instance = generator;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Map/Room Map Prefabs/Normal/Test/Room_Normal 1.prefab");
            var source = prefab.GetComponentsInChildren<Tilemap>(true).First(t => t.name == "Wall");
            var oldMap = MakeMap(scene, "Old Wall", source, 1);
            var newMap = MakeMap(scene, "New Wall", source, 1);

            var watch = Stopwatch.StartNew();
            var oldCollider = BuildOld(oldMap);
            double oldMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var newCollider = BuildNew(generator, newMap);
            double newMs = watch.Elapsed.TotalMilliseconds;
            Check(oldCollider.pathCount > 0, "비어 있지 않은 기준 충돌 형상");
            Check(Edges(oldCollider).SetEquals(Edges(newCollider)), "기존/현재 Wall 충돌 경계 일치");
            Check(newMap.gameObject.activeSelf && !newCollider.isTrigger, "활성 상태/물리 벽 유지");

            // 이미 콜라이더가 있는 재생성 경로와 타일 제거(문 구멍)도 검사한다.
            Vector3Int cell = newMap.cellBounds.min;
            foreach (var candidate in newMap.cellBounds.allPositionsWithin)
                if (newMap.HasTile(candidate)) { cell = candidate; break; }
            oldMap.SetTile(cell, null);
            newMap.SetTile(cell, null);
            oldMap.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
            oldCollider.GenerateGeometry();
            BuildNew(generator, newMap);
            Check(Edges(oldCollider).SetEquals(Edges(newCollider)), "타일 제거 후 재생성 충돌 경계 일치");
            Check(newMap.GetComponents<TilemapCollider2D>().Length == 1 &&
                newMap.GetComponents<CompositeCollider2D>().Length == 1, "중복 콜라이더 없음");

            Debug.Log($"[MapColliderCheck] PASS geometry/rebuild: paths={newCollider.pathCount}, " +
                $"points={newCollider.pointCount}; same room old={oldMs:F2}ms new={newMs:F2}ms");

            // 일반 맵과 비슷한 타일 수. 11개 방을 160유닛 간격으로 놓되 NPC/보상/저장 로직은 실행하지 않는다.
            var largeMap = MakeMap(scene, "11 Room Walls", source, 11);
            watch.Restart();
            var largeCollider = BuildNew(generator, largeMap);
            double largeMs = watch.Elapsed.TotalMilliseconds;
            Check(largeCollider.pathCount > 0, "대형 Wall 충돌 생성");
            var bounds = largeMap.cellBounds;
            int tiles = largeMap.GetTilesRangeCount(bounds.min, bounds.max - Vector3Int.one);
            Check(tiles > 10000, "대형 테스트에 실제 벽 타일이 복사됨");
            Debug.Log($"[MapColliderCheck] PASS large: tiles={tiles}, paths={largeCollider.pathCount}, " +
                $"points={largeCollider.pointCount}, colliderBuild={largeMs:F2}ms. Preview scene only; no saved changes.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            MapGenerator.Instance = previousInstance;
        }
    }

    private static Tilemap MakeMap(Scene scene, string name, Tilemap source, int copies)
    {
        var root = new GameObject(name, typeof(Grid));
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.localScale = new Vector3(0.5f, 0.5f, 1f); // BattleScene의 글로벌 Grid와 동일
        root.GetComponent<Grid>().cellSize = new Vector3(1f, 1f, 0f);
        var child = new GameObject("Wall", typeof(Tilemap));
        child.transform.SetParent(root.transform, false);
        var map = child.GetComponent<Tilemap>();
        var bounds = source.cellBounds;
        int count = source.GetTilesRangeCount(bounds.min, bounds.max - Vector3Int.one);
        var cells = new Vector3Int[count];
        var tiles = new TileBase[count];
        source.GetTilesRangeNonAlloc(bounds.min, bounds.max - Vector3Int.one, cells, tiles);
        var positions = new Vector3Int[count];
        for (int copy = 0; copy < copies; copy++)
        {
            for (int i = 0; i < count; i++)
                // 이 검사는 충돌 생성만 비교한다. 미리보기 씬의 Grid 변환에 의존하지 않고 같은 셀을 복제한다.
                positions[i] = cells[i] + new Vector3Int(320 * copy, 0, 0);
            map.SetTiles(positions, tiles);
        }
        Check(map.GetTilesRangeCount(map.cellBounds.min, map.cellBounds.max - Vector3Int.one) > 1000,
            "실제 방의 타일 좌표가 한 칸으로 뭉치지 않음");
        return map;
    }

    private static CompositeCollider2D BuildNew(MapGenerator generator, Tilemap map)
    {
        typeof(MapGenerator).GetField("globalWallTilemap", Private).SetValue(generator, map);
        typeof(MapGenerator).GetMethod("SetupFinalColliders", Private).Invoke(generator, null);
        return map.GetComponent<CompositeCollider2D>();
    }

    private static CompositeCollider2D BuildOld(Tilemap map)
    {
        var go = map.gameObject;
        go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        go.AddComponent<TilemapCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge;
        var composite = go.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        composite.generationType = CompositeCollider2D.GenerationType.Manual;
        composite.GenerateGeometry();
        Physics2D.SyncTransforms();
        return composite;
    }

    private static HashSet<string> Edges(CompositeCollider2D collider)
    {
        var edges = new HashSet<string>();
        string Key(Vector2 v) => $"{Mathf.RoundToInt(v.x * 1000f)},{Mathf.RoundToInt(v.y * 1000f)}";
        for (int path = 0; path < collider.pathCount; path++)
        {
            var points = new Vector2[collider.GetPathPointCount(path)];
            collider.GetPath(path, points);
            for (int i = 0; i < points.Length; i++)
            {
                string a = Key(points[i]), b = Key(points[(i + 1) % points.Length]);
                edges.Add(string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a);
            }
        }
        return edges;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[MapColliderCheck] FAIL: " + message);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>무효 프리팹 회귀 검사. 임시 데이터/미리보기 씬만 사용하고 원본 에셋은 저장하지 않는다.</summary>
public static class RoomPrefabCheck
{
    [MenuItem("Tools/Map/Verify Room Prefab References")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        var scene = EditorSceneManager.NewPreviewScene();
        var previousInstance = MapGenerator.Instance;
        var randomState = Random.state;
        var existingMaterials = new HashSet<PhysicsMaterial2D>(Resources.FindObjectsOfTypeAll<PhysicsMaterial2D>());
        var data = ScriptableObject.CreateInstance<RoomPrefabDataSO>();
        var tuning = ScriptableObject.CreateInstance<MapGenerationDataSO>();
        var diagnostics = new List<string>();
        void OnLog(string message, string stack, LogType type)
        {
            if (message.StartsWith("[MapPrefab]")) diagnostics.Add(message);
        }
        Application.logMessageReceived += OnLog;
        try
        {
            data.name = "RoomPrefabCheck (temporary)";
            var valid = new GameObject("Valid room", typeof(RoomInstance));
            SceneManager.MoveGameObjectToScene(valid, scene);
            foreach (var direction in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                var anchor = new GameObject("Anchor", typeof(RoomAnchor));
                anchor.transform.SetParent(valid.transform, false);
                anchor.GetComponent<RoomAnchor>().direction = direction;
            }
            var destroyed = new GameObject("Destroyed room");
            Object.DestroyImmediate(destroyed);
            Check(destroyed == null && !ReferenceEquals(destroyed, null), "Unity fake-null 재현");

            var source = new List<GameObject> { null, destroyed, valid };
            data.roomEntries = new List<RoomPrefabEntry>
            {
                null,
                new RoomPrefabEntry { roomType = RoomType.Spawn, prefabs = source },
                new RoomPrefabEntry { roomType = RoomType.Normal, prefabs = source },
                new RoomPrefabEntry { roomType = RoomType.Shop, prefabs = source },
                new RoomPrefabEntry { roomType = RoomType.Augment, prefabs = new List<GameObject> { destroyed } },
                new RoomPrefabEntry { roomType = RoomType.EnhanceShop, prefabs = new List<GameObject> { null } },
            };
            foreach (var type in new[] { RoomType.Spawn, RoomType.Normal, RoomType.Shop, RoomType.Augment, RoomType.EnhanceShop })
            {
                var candidates = data.GetValidPrefabs(type);
                Check(candidates.Count == 1 && candidates[0] == valid, $"{type}: 필터/폴백");
                candidates.Clear();
                Check(data.GetRandomPrefab(type) == valid, $"{type}: 무작위 선택");
            }
            Check(source.Count == 3 && ReferenceEquals(source[1], destroyed), "원본 등록 목록 보존");

            tuning.totalRoomCount = 5;
            tuning.minNormalRooms = 2;
            tuning.shopCount = 1;
            tuning.augmentRoomCount = 1;
            tuning.enhanceShopCount = tuning.eliteCount = tuning.rewardCount = 0;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var place = typeof(MapGenerator).GetMethod("PlaceIsaacRoomsNormalFloor", flags);
            for (int seed = 0; seed < 16; seed++)
            {
                Random.InitState(seed);
                var host = new GameObject("Placement check");
                SceneManager.MoveGameObjectToScene(host, scene);
                var generator = host.AddComponent<MapGenerator>();
                generator.SetMapData(tuning, data);
                typeof(MapGenerator).GetField("logGenerationTimings", flags).SetValue(generator, false);
                try
                {
                    var grid = new Dictionary<Vector2Int, RoomInstance>();
                    bool placed = (bool)place.Invoke(generator, new object[] { grid });
                    Check(placed && grid.Count == 5 && grid.Values.All(r => r.debugDepth >= 0),
                        $"seed={seed}: 스폰/일반/특수 방 배치와 연결 완료");
                    if (seed == 15)
                    {
                        data.roomEntries[2].prefabs = new List<GameObject> { null, destroyed };
                        typeof(MapGenerator).GetMethod("ClearExistingMap", flags).Invoke(generator, null);
                        grid.Clear();
                        Check(!(bool)place.Invoke(generator, new object[] { grid }), "일반 후보 전부 무효: 예외 대신 실패 반환");
                    }
                }
                finally { Object.DestroyImmediate(host); }
            }
            data.roomEntries = null;
            Check(data.GetRandomPrefab(RoomType.Spawn) == null, "등록 목록 null");
            data.roomEntries = new List<RoomPrefabEntry> { new RoomPrefabEntry { roomType = RoomType.Spawn } };
            Check(data.GetRandomPrefab(RoomType.Spawn) == null, "프리팹 목록 null");
            Check(diagnostics.Any(s => s.Contains("prefabs[0] state=Null")) &&
                diagnostics.Any(s => s.Contains("prefabs[1] state=Missing/Destroyed")) &&
                diagnostics.Any(s => s.Contains("floor=") && s.Contains("attempt=") && s.Contains("frame=")),
                "무효 슬롯/상태/생성 상황 로그");
            Debug.Log("[MapPrefabCheck] PASS: null/destroyed filtering, fallback, source preservation, " +
                "16 placement seeds, all-invalid failure and diagnostics. No assets/scenes saved.");
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(tuning);
            foreach (var material in Resources.FindObjectsOfTypeAll<PhysicsMaterial2D>())
                if (!existingMaterials.Contains(material) && !EditorUtility.IsPersistent(material) && material.name == "Slippery")
                    Object.DestroyImmediate(material);
            MapGenerator.Instance = previousInstance;
            Random.state = randomState;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[MapPrefabCheck] FAIL: " + message);
    }
}

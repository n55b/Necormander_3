using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>원본을 저장하지 않고 배경 배선/대비/큰 지도와 전투 HUD 배경 크기를 검사한다.</summary>
public static class MiniMapCheck
{
    [MenuItem("Tools/Map/Verify MiniMap Appearance")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/MiniMap.prefab");
        try
        {
            var settings = new SerializedObject(root.GetComponent<UIBasedMiniMap>());
            Color visited = settings.FindProperty("visitedRoomColor").colorValue;
            Color unvisited = settings.FindProperty("unvisitedRoomColor").colorValue;
            Color current = settings.FindProperty("currentRoomColor").colorValue;
            Check(visited.a == 1f && unvisited.a == 1f && current.a == 1f, "방 색 불투명");
            Check(visited.grayscale > unvisited.grayscale && current != visited, "방문/미방문/현재 구분");

            var fit = typeof(UIBasedMiniMap).GetMethod("FitBackground", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (string field in new[] { "fullMapBackground", "hudMapBackground" })
            {
                var background = settings.FindProperty(field).objectReferenceValue as RectTransform;
                Check(background != null && background.gameObject.activeSelf, field + " 배선/활성");
                var image = background.GetComponent<Image>();
                Check(image != null && image.color.a > 0f && image.color.a < 1f && !image.raycastTarget,
                    field + " 반투명/클릭 통과");
                Check(image.color.grayscale < unvisited.grayscale, field + " 방과 대비");
                var container = (RectTransform)background.parent;
                if (field == "hudMapBackground")
                {
                    Check(container.sizeDelta == new Vector2(120, 100) && background.sizeDelta == Vector2.zero, "HUD 고정 프레임");
                    Check(container.GetComponent<RectMask2D>() != null, "HUD 프레임 밖 클리핑");
                    var map = root.GetComponent<UIBasedMiniMap>();
                    var cache = (Dictionary<int, List<Vector2>>)typeof(UIBasedMiniMap).GetField("_roomFloorCache", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(map);
                    var testRoom = new GameObject("Room dimensions test"); testRoom.SetActive(false);
                    var roomComponent = testRoom.AddComponent<RoomInstance>();
                    try
                    {
                        foreach (var span in new[] { new Vector2(10, 10), new Vector2(200, 70), new Vector2(35, 180) })
                        {
                            cache[roomComponent.GetInstanceID()] = new List<Vector2> { Vector2.zero, span - Vector2.one };
                            var size = (Vector2)typeof(UIBasedMiniMap).GetMethod("GetFocusRoomUiSize", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(map, new object[] { roomComponent });
                            Check(size.x <= 106.01f && size.y <= 86.01f && Mathf.Abs(size.x / size.y - span.x / span.y) < .001f, "작은/큰/세로 방 비율 유지 및 프레임 안쪽 맞춤");
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(testRoom); }
                    continue;
                }
                var room = new GameObject("Large room check", typeof(RectTransform));
                room.transform.SetParent(container, false);
                var rect = (RectTransform)room.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
                rect.anchoredPosition = new Vector2(-360f, 0f);
                rect.sizeDelta = new Vector2(300f, 240f);
                var drawn = new List<GameObject> { room };
                room.transform.SetAsFirstSibling(); // 복도가 맨 뒤로 들어가는 순서도 재현.
                fit.Invoke(null, new object[] { container, background, drawn });
                Check(background.GetSiblingIndex() == 0, field + " 복도보다 뒤");
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                foreach (var corner in corners)
                    Check(background.rect.Contains(background.InverseTransformPoint(corner)), field + " 큰 방 포함");
                drawn.Clear();
                fit.Invoke(null, new object[] { container, background, drawn });
                Check(Vector2.Distance(background.sizeDelta, container.rect.size + Vector2.one * 24f) < 0.01f,
                    field + " 이전 큰 방 배경 크기 초기화");
                UnityEngine.Object.DestroyImmediate(room);
            }
            Debug.Log("[MiniMapCheck] PASS: 색상, 배경 배선/순서/크기");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[MiniMapCheck] " + message);
    }
}

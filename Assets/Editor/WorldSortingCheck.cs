using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>실제 소환수/포탈 프리팹의 정렬 배선 및 Y 이동 회귀 검사. 원본/씬을 저장하지 않는다.</summary>
public static class WorldSortingCheck
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string PortalPath = "Assets/Prefabs/Interactables/FloorProceedPortalPrefab.prefab";

    [MenuItem("Tools/Diagnostics/Verify Minion and Portal Sorting")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        var scene = EditorSceneManager.NewPreviewScene();
        var visuals = new HashSet<GameObject>();
        int references = 0;
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MainMinionDataSO"))
            {
                var data = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var visual in new[] { data.basicAnim.visual, data.skillAnim.visual, data.dashAnim.visual })
                {
                    Check(visual != null, data.name + ": missing visual");
                    Check(AssetDatabase.GetAssetPath(visual).EndsWith(".prefab"), data.name + ": editable prefab");
                    references++;
                    if (!visuals.Add(visual)) continue;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(visual, scene);
                    try
                    {
                        var animator = instance.GetComponentInChildren<Animator>(true);
                        Check(animator != null && animator.runtimeAnimatorController != null, visual.name + ": animator preserved");
                        Check(instance.GetComponentInChildren<SpriteRenderer>(true).transform.localPosition == Vector3.zero,
                            visual.name + ": no extra imported sprite offset");
                        VerifySorting(instance, true);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(instance); }
                }
            }
            Check(references > 0, "No minion data found");
            foreach (var entry in new[] { ("DashDoll", "DashDoll_Minion"), ("IceMage", "IceMage_Minion") })
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Sprites/Minion/{entry.Item2}.aseprite");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Minion/{entry.Item1}.prefab");
                Check(prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController ==
                    source.GetComponentInChildren<Animator>(true).runtimeAnimatorController, entry.Item1 + ": original clips/events retained");
            }
            var portal = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PortalPath), scene);
            VerifySorting(portal, false);
            int order = portal.GetComponent<SpriteRenderer>().sortingOrder;
            typeof(FloorProceedPortal).GetMethod("Start", PrivateInstance).Invoke(portal.GetComponent<FloorProceedPortal>(), null);
            Check(portal.GetComponent<SpriteRenderer>().sortingOrder == order, "Portal Start must not overwrite Y order with 5");
            foreach (string path in new[] { "Assets/Prefabs/Map/Room Map Prefabs/Elite/Room_Elite.prefab",
                "Assets/Prefabs/Map/Room Map Prefabs/Elite/Test/Room_Elite.prefab" })
            {
                var nestedPortal = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<FloorProceedPortal>(true);
                Check(nestedPortal != null && nestedPortal.GetComponent<YSortableObject>() != null, path + ": inherits portal sorting");
            }
            Debug.Log($"[WorldSortingCheck] PASS: {references} minion references / {visuals.Count} visuals, dynamic Y movement, " +
                "visual-only offset, original animation controllers, portal Start, 2 nested portal instances. No assets/scenes saved.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void VerifySorting(GameObject root, bool dynamic)
    {
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        Check(renderers.Length == 1, root.name + ": single body renderer");
        var sr = renderers[0];
        var sorts = sr.GetComponents<YSortableObject>();
        Check(sorts.Length == 1, root.name + ": exactly one authored sorter");
        var sort = sorts[0];
        var so = new SerializedObject(sort);
        Check(so.FindProperty("isDynamic").boolValue == dynamic && so.FindProperty("precision").intValue == 100 &&
            so.FindProperty("baseOffset").intValue == 0, root.name + ": shared Y-sorting settings");
        Check(so.FindProperty("pivotOverride").objectReferenceValue == root.transform, root.name + ": ground anchor");
        Check(sr.sortingLayerName == "Default", root.name + ": Default sorting layer");
        typeof(YSortableObject).GetMethod("Awake", PrivateInstance).Invoke(sort, null);
        var tick = typeof(YSortableObject).GetMethod(dynamic ? "LateUpdate" : "Start", PrivateInstance);
        foreach (var sample in new[] { (10f, -1000), (0.25f, -25), (-10f, 1000) })
        {
            root.transform.position = new Vector3(2, sample.Item1, 0);
            tick.Invoke(sort, null);
            Check(sr.sortingOrder == sample.Item2, root.name + ": Y order at " + sample.Item1);
        }
        if (sr.transform != root.transform)
        {
            sr.transform.localPosition += Vector3.up * 3;
            tick.Invoke(sort, null);
            Check(sr.sortingOrder == 1000, root.name + ": sprite offset must not replace ground anchor");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[WorldSortingCheck] FAIL: " + message);
    }
}

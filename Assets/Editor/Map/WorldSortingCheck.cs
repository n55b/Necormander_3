using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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
                        VerifySorting(instance);
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
            VerifySorting(portal);
            var portalGroup = portal.GetComponent<SortingGroup>();
            Check(portalGroup != null, "Portal rings are grouped with the portal body");
            int order = portalGroup.sortingOrder;
            typeof(FloorProceedPortal).GetMethod("Start", PrivateInstance).Invoke(portal.GetComponent<FloorProceedPortal>(), null);
            Check(portalGroup.sortingOrder == order, "Portal Start preserves its world order");
            VerifyActor("Assets/Prefabs/Player Melee.prefab", "Hand");
            VerifyActor("Assets/Prefabs/Enemy/Enemy.prefab", "DirectionIndicator");
            foreach (string path in new[] { "Grave.prefab", "Stone/Stone_1.prefab", "StoneLanterrn/StoneLantern_On.prefab", "Grass/Bush.prefab" })
            {
                var prop = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/Objects/" + path);
                Check(prop.GetComponent<YSortableObject>() != null && prop.GetComponent<SpriteRenderer>().sortingLayerID == 0,
                    path + ": interleaves with actors");
            }
            foreach (string path in new[] { "Assets/Prefabs/Map/Room Map Prefabs/Elite/Room_Elite.prefab",
                "Assets/Prefabs/Map/Room Map Prefabs/Elite/Variants/Room_Elite.prefab" })
            {
                var nestedPortal = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<FloorProceedPortal>(true);
                Check(nestedPortal != null && nestedPortal.GetComponent<YSortableObject>() != null, path + ": inherits portal sorting");
            }
            Debug.Log($"[WorldSortingCheck] PASS: {references} minion references / {visuals.Count} visuals, dynamic Y movement, " +
                "visual-only offset, original animation controllers, portal Start, 2 nested portal instances. No assets/scenes saved.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void VerifyActor(string path, string childName)
    {
        var actor = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var group = actor.GetComponent<SortingGroup>();
        var sorters = actor.GetComponentsInChildren<YSortableObject>(true);
        var child = actor.GetComponentsInChildren<SpriteRenderer>(true).Single(renderer => renderer.name == childName);
        var childGroup = child.GetComponentInParent<SortingGroup>(true);
        Check(group != null && sorters.Length == 1 && childGroup == group,
            $"{path}: body and attachments share one depth participant (root: {group}, attach: {childGroup}, " +
            $"participants: {string.Join(", ", sorters.Select(sorter => sorter.name))})");
        var shadow = actor.GetComponentsInChildren<SortingGroup>(true).Single(candidate => candidate.name == "Shadow");
        Check(shadow.sortAtRoot && shadow.sortingLayerName == "Ground", path + ": shadow below world bodies");
    }

    private static void VerifySorting(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        Check(renderers.Length == 1, root.name + ": single body renderer");
        var sr = renderers[0];
        var sorts = sr.GetComponents<YSortableObject>();
        Check(sorts.Length == 1, root.name + ": exactly one authored sorter");
        var sort = sorts[0];
        var so = new SerializedObject(sort);
        Check(so.FindProperty("pivotOverride").objectReferenceValue == root.transform, root.name + ": ground anchor");
        Check(sr.sortingLayerName == "Default", root.name + ": Default sorting layer");
        var method = typeof(YSortableObject).Assembly.GetType("WorldDepthOrder").GetMethod("Refresh", BindingFlags.Static | BindingFlags.NonPublic);
        var lower = new GameObject("Ground contact reference", typeof(SpriteRenderer), typeof(YSortableObject));
        SceneManager.MoveGameObjectToScene(lower, root.scene);
        var reference = lower.GetComponent<YSortableObject>();
        var group = so.FindProperty("sortingGroup").objectReferenceValue as SortingGroup;
        int Order() => group != null ? group.sortingOrder : sr.sortingOrder;
        try
        {
            foreach (var participant in new[] { sort, reference })
            {
                typeof(YSortableObject).GetMethod("Awake", PrivateInstance).Invoke(participant, null);
                typeof(YSortableObject).GetMethod("OnEnable", PrivateInstance).Invoke(participant, null);
            }
            lower.transform.position = new Vector3(2, -100000f, 0);
            root.transform.position = new Vector3(2, -99999.75f, 0);
            method.Invoke(null, null);
            Check(Order() < reference.Renderer.sortingOrder, root.name + ": farther unit behind at large coordinates");
            root.transform.position = new Vector3(2, -100000.25f, 0);
            method.Invoke(null, null);
            Check(Order() > reference.Renderer.sortingOrder, root.name + ": nearer unit in front");
            if (sr.transform != root.transform)
            {
                int order = Order();
                sr.transform.localPosition += Vector3.up * 3;
                method.Invoke(null, null);
                Check(Order() == order, root.name + ": animated sprite offset leaves the ground anchor alone");
            }

            root.transform.position = new Vector3(2, .125f, 0);
            lower.transform.position = root.transform.position;
            reference.SetGroundPoint(lower.transform.position, 1);
            method.Invoke(null, null);
            Check(reference.Renderer.sortingOrder > Order(), root.name + ": explicit equal-depth priority is stable");
            lower.transform.position += Vector3.up * .0078125f;
            method.Invoke(null, null);
            Check(reference.Renderer.sortingOrder < Order(), root.name + ": priority cannot overtake a different depth");
        }
        finally
        {
            foreach (var participant in new[] { sort, reference })
                typeof(YSortableObject).GetMethod("OnDisable", PrivateInstance).Invoke(participant, null);
            UnityEngine.Object.DestroyImmediate(lower);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[WorldSortingCheck] FAIL: " + message);
    }
}

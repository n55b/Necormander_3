using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>포탈 콜라이더 중복 회귀 검사. 원본 프리팹이나 씬은 저장하지 않는다.</summary>
public static class PortalColliderSmokeTest
{
    [MenuItem("Tools/Necromander/Test Portal Colliders")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run outside Play Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Portal collider test");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            Check<FloorProceedPortal>(root.transform, "FloorProceedPortalPrefab", 0.5f);
            Check<BossTestPortal>(root.transform, "BossTestPortalPrefab", 0.6f);
            Debug.Log("[PortalColliderSmokeTest] ALL CHECKS PASSED");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void Check<T>(Transform parent, string name, float fallbackRadius) where T : Component
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Interactables/" + name + ".prefab");
        Require(prefab != null, name + ": prefab exists");
        var instance = Object.Instantiate(prefab, parent);
        var box = instance.GetComponent<BoxCollider2D>();
        Require(box != null, name + ": authored box exists");
        var size = box.size;
        var offset = box.offset;
        Awake(instance.GetComponent<T>());
        Require(instance.GetComponents<Collider2D>().Length == 1, name + ": no duplicate collider");
        Require(instance.GetComponent<Collider2D>() == box && box.isTrigger
            && box.size == size && box.offset == offset, name + ": original trigger and range preserved");
        Awake(instance.GetComponent<T>());
        Require(instance.GetComponents<Collider2D>().Length == 1, name + ": repeated initialization is stable");

        // 프리팹 없이 생성하는 보스/엘리트 출구도 기존 기본 감지 범위를 유지해야 한다.
        var fallback = new GameObject(name + " without prefab");
        fallback.transform.SetParent(parent, false);
        var portal = fallback.AddComponent<T>();
        Awake(portal);
        var circle = fallback.GetComponent<CircleCollider2D>();
        Require(circle != null && circle.isTrigger && Mathf.Approximately(circle.radius, fallbackRadius),
            name + ": missing collider fallback preserved");
        circle.radius = 0.8f;
        Awake(portal);
        Require(fallback.GetComponents<Collider2D>().Length == 1 && Mathf.Approximately(circle.radius, 0.8f),
            name + ": existing circle reused without resizing");
        Require(fallback.layer == Layers.Interactable && fallback.GetComponent<Rigidbody2D>().bodyType == RigidbodyType2D.Kinematic,
            name + ": interaction layer and rigidbody preserved");
    }

    private static void Awake(Component component)
        => component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[PortalColliderSmokeTest] FAIL: " + message);
        Debug.Log("[PortalColliderSmokeTest] PASS: " + message);
    }
}

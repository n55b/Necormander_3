using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>현재 UI 프리팹의 참조를 검사하고 640×360 미리보기를 저장한다.</summary>
public static class HudLayoutChecks
{
    private const string Explain = "Assets/Prefabs/UI/SkillExplainUI.prefab";
    private const string Pouch = "Assets/Prefabs/UI/PouchUI.prefab";
    private const string Picker = "Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab";
    private const string Hud = "Assets/Prefabs/UI/Player State/PlayerStateUI.prefab";
    private const string Map = "Assets/Prefabs/UI/MiniMap.prefab";

    private static readonly (string path, string visiblePanel)[] Prefabs =
    {
        (Explain, "LoadoutPanel"), (Pouch, "PouchWindow"), (Picker, "MinionSelection"), (Hud, ""), (Map, "")
    };

    [MenuItem("Tools/UI/Checks/Validate layout")]
    public static void Validate()
    {
        InventoryUISetup.Validate();
        foreach (var (path, panel) in Prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(prefab != null, path);
            Require(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(component => component != null),
                path + "에 누락된 스크립트가 있습니다.");
            Require(string.IsNullOrEmpty(panel) || prefab.transform.Find(panel) != null, path + "의 표시 패널 " + panel);
        }

        var info = AssetDatabase.LoadAssetAtPath<SkillExplainUI>(Explain);
        var details = (RectTransform)new SerializedObject(info).FindProperty("detailsPanel").objectReferenceValue;
        Require(details != null && !details.gameObject.activeSelf, "C 상세 패널은 처음에 접혀 있어야 합니다.");
        var selector = AssetDatabase.LoadAssetAtPath<MainMinionSelectionUI>(Picker);
        Require(Mathf.Approximately(selector.ConfirmHoldSeconds, 1f), "미니언 선택은 1초간 누릅니다.");

        var dash = AssetDatabase.LoadAssetAtPath<GameObject>(Hud).GetComponentInChildren<DashCooldownUI>(true);
        Require(dash != null, "HUD 대시 표시 컴포넌트");
        var serialized = new SerializedObject(dash);
        Require(serialized.FindProperty("worldPips").objectReferenceValue != null, "플레이어 발밑 표시 패널");
        var fills = serialized.FindProperty("worldPipFills");
        Require(fills.arraySize > 0 && Enumerable.Range(0, fills.arraySize)
            .All(index => fills.GetArrayElementAtIndex(index).objectReferenceValue != null), "발밑 대시 충전 표시 연결");
        Debug.Log("[HudLayoutChecks] VALIDATE PASS — 현재 HUD, 인벤토리, 미니언 선택과 미니맵 참조.");
    }

    [MenuItem("Tools/UI/Checks/Render previews")]
    public static void Preview()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var camera = new GameObject("HUD preview camera").AddComponent<Camera>();
        var canvas = new GameObject("HUD preview canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
        SceneManager.MoveGameObjectToScene(canvas.gameObject, scene);
        camera.scene = scene;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.12f, .16f, .16f);
        camera.orthographic = true;
        camera.allowMSAA = false;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 10;
        canvas.pixelPerfect = true;
        var target = new RenderTexture(PixelViewport.Width, PixelViewport.Height, 24) { filterMode = FilterMode.Point };
        camera.targetTexture = target;
        // 메뉴 배치를 저작한 960×540 UI 단위에서 640×360 픽셀 프레임으로 렌더한다.
        canvas.scaleFactor = PixelViewport.Height / 540f;
        var previousTarget = RenderTexture.active;
        try
        {
            Directory.CreateDirectory("Logs/HudLayoutChecks");
            foreach (var (path, panel) in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = Object.Instantiate(prefab, canvas.transform);
                instance.SetActive(true);
                if (!string.IsNullOrEmpty(panel)) instance.transform.Find(panel).gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine("Logs/HudLayoutChecks", prefab.name + ".png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(instance);
            }
            Debug.Log("[HudLayoutChecks] PREVIEW PASS — Logs/HudLayoutChecks/*.png");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[HudLayoutChecks] " + message);
    }
}

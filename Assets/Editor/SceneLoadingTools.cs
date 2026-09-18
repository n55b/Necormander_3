using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityNote;
using Object = UnityEngine.Object;

/// <summary>기존 로딩 캔버스의 프리팹화 및 회귀 검사. 런타임에 UI를 조립하지 않는다.</summary>
public static class SceneLoadingTools
{
    private const string PrefabPath = "Assets/Prefabs/Resources/UI/SceneLoader.prefab";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string TransitionCheck = "LoadingCheck.RunTransitions";

    [InitializeOnLoadMethod]
    private static void RegisterPlayCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(TransitionCheck, false)) return;
            SessionState.SetBool(TransitionCheck, false);
            SceneLoader.Instance.StartCoroutine(CheckTransitions());
        };
    }

    [MenuItem("Tools/Loading/Run Scene Transition Check")]
    public static void RunTransitions()
    {
        Debug.Log($"[LoadingCheck] state: playing={Application.isPlaying}, loading={SceneLoader.IsLoading}, loader={SceneLoader.Instance}, progress={SceneLoader.Instance?.Progress}, ready={GameManager.Instance?.IsPlayerReady}");
        if (Application.isPlaying)
        {
            SceneLoader.Instance.StartCoroutine(CheckTransitions());
            return;
        }
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "플레이를 끈 뒤 실행");
        SessionState.SetBool(TransitionCheck, true);
        EditorApplication.EnterPlaymode();
    }

    private static IEnumerator CheckTransitions()
    {
        // 에디터가 뒤로 가도 검사가 진행되게 한다. 프로젝트 설정은 바꾸지 않는다.
        bool previousBackground = Application.runInBackground;
        Application.runInBackground = true;
        try
        {
            yield return WaitForReady();
            VerifyReadiness();
            foreach (string destination in new[] { "StartScene", "BattleScene", "VillageScene" })
            {
                TutorialFlow.IsRunning = destination == "BattleScene";
                Check(SceneLoader.Load(destination), "씬 전환 시작: " + destination);
                Check(SceneLoader.IsLoading && !SceneLoader.Load(destination), "즉시 가림과 중복 전환 거부");
                yield return WaitForReady();
                Check(SceneManager.GetActiveScene().name == destination, "목적지 도착: " + destination);
                Check(Object.FindObjectsByType<SceneLoader>(FindObjectsSortMode.None).Length == 1, "로더 중복 없음");
                Debug.Log("[LoadingCheck] PASS: " + destination + " 준비 완료 뒤 공개.");
            }
            Debug.Log("[LoadingCheck] PASS: direct play, title, tutorial, village transitions.");
        }
        finally { Application.runInBackground = previousBackground; }
    }

    private static IEnumerator WaitForReady()
    {
        float deadline = Time.realtimeSinceStartup + 180f;
        var loader = SceneLoader.Instance;
        Check(loader != null, "시작 전 로더 생성");
        var data = new SerializedObject(loader);
        var panel = (GameObject)data.FindProperty("loadingScreen").objectReferenceValue;
        while (SceneLoader.IsLoading)
        {
            Check(panel.activeInHierarchy, "초기화 중 화면 유지");
            Check(Time.realtimeSinceStartup < deadline,
                $"검사 제한 시간: scene={SceneManager.GetActiveScene().name}, progress={loader.Progress}, ready={GameManager.Instance?.IsPlayerReady}");
            yield return null;
        }
        Check(GameManager.Instance == null || GameManager.Instance.IsPlayerReady, "GameManager 준비 이전 공개 금지");
        Check(loader.Progress == 1f && !panel.activeSelf, "100% 후에만 가림 해제");
    }

    [MenuItem("Tools/Loading/Apply Existing Loading Canvas")]
    public static void Apply()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "플레이를 끈 뒤 실행");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/StartScene.unity");
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity", OpenSceneMode.Additive);
        Check(!scene.isDirty, "StartScene의 저장하지 않은 변경을 먼저 확인하세요");
        try
        {
            var loader = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SceneLoader>(true)).Single();
            var data = new SerializedObject(loader);
            var panel = (GameObject)data.FindProperty("loadingScreen").objectReferenceValue;
            var canvas = loader.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
            loader.transform.localScale = Vector3.one;
            var opaque = panel.GetComponent<Image>() ?? panel.AddComponent<Image>();
            opaque.color = Color.black;
            opaque.raycastTarget = true; // 배경 아트에 투명 픽셀이 있어도 뒤 씬이 보이지 않는다.
            var label = panel.GetComponentInChildren<TMP_Text>(true);
            label.raycastTarget = false;
            var labelRect = label.rectTransform;
            labelRect.anchoredPosition = new Vector2(0f, 6f);

            var bar = panel.transform.Find("LoadingProgress");
            if (bar == null)
            {
                bar = new GameObject("LoadingProgress", typeof(RectTransform), typeof(Image)).transform;
                bar.SetParent(panel.transform, false);
                bar.gameObject.layer = panel.layer;
                var rect = (RectTransform)bar;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.right;
                rect.anchoredPosition = new Vector2(-10f, 42f);
                rect.sizeDelta = new Vector2(200f, 6f);
                bar.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 1f);
                bar.GetComponent<Image>().raycastTarget = false;
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.layer = panel.layer;
                fill.transform.SetParent(bar, false);
                var fr = (RectTransform)fill.transform;
                fr.anchorMin = Vector2.zero;
                fr.anchorMax = Vector2.one;
                fr.offsetMin = fr.offsetMax = Vector2.zero;
            }
            var progress = bar.GetChild(0).GetComponent<Image>();
            // 새 그림 대신 기존 가드 게이지의 무채색 스프라이트와 파란색을 그대로 쓴다.
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Player State/PlayerStateUI.prefab");
            var hudData = new SerializedObject(hud.GetComponent<PlayerStateUI>());
            progress.sprite = ((Image)hudData.FindProperty("guardGaugeFill").objectReferenceValue).sprite;
            progress.color = hudData.FindProperty("guardReadyColor").colorValue;
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Horizontal;
            progress.fillOrigin = 0;
            progress.fillAmount = 0f;
            progress.raycastTarget = false;
            data.FindProperty("progressFill").objectReferenceValue = progress;
            data.FindProperty("loadingText").objectReferenceValue = label;
            data.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false);
            PrefabUtility.SaveAsPrefabAssetAndConnect(loader.gameObject, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        Verify();
    }

    [MenuItem("Tools/Loading/Verify Loading Prefab")]
    public static void Verify()
    {
        var loader = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<SceneLoader>();
        var data = new SerializedObject(loader);
        var panel = (GameObject)data.FindProperty("loadingScreen").objectReferenceValue;
        var bar = (Image)data.FindProperty("progressFill").objectReferenceValue;
        Check(loader.GetComponent<Canvas>().sortingOrder == short.MaxValue, "로딩 캔버스 최상단");
        Check(panel.GetComponent<Image>().color.a == 1f && panel.GetComponent<Image>().raycastTarget, "불투명 배경과 후면 클릭 차단");
        Check(bar.sprite != null && bar.type == Image.Type.Filled && bar.color.b > bar.color.r, "기존 아트 기반 파란 진행 바");
        Check(data.FindProperty("loadingText").objectReferenceValue != null, "Loading 문구 연결");
        Debug.Log("[LoadingCheck] PASS: prefab, opaque cover, raycast blocking, blue progress bar, label.");
    }

    [MenuItem("Tools/Loading/Verify Readiness In Play Mode")]
    public static void VerifyReadiness()
    {
        Check(Application.isPlaying && !SceneLoader.IsLoading, "실제 씬 로딩이 완료된 플레이 모드에서 실행");
        var loader = SceneLoader.Instance;
        var scene = SceneManager.CreateScene("Loading readiness check");
        var host = new GameObject("Inactive initialization fixture");
        host.SetActive(false);
        SceneManager.MoveGameObjectToScene(host, scene);
        var manager = host.AddComponent<GameManager>(); // Awake/Start/세이브는 실행하지 않는다.
        var previousTime = Time.timeScale;
        var data = new SerializedObject(loader);
        var panel = (GameObject)data.FindProperty("loadingScreen").objectReferenceValue;
        try
        {
            typeof(SceneLoader).GetMethod("Show", Private).Invoke(loader, null);
            var wait = (IEnumerator)typeof(SceneLoader).GetMethod("FinishWhenReady", Private).Invoke(loader, new object[] { scene });
            Check(wait.MoveNext(), "Start 대기");
            typeof(GameManager).GetField("_loadingProgress", Private).SetValue(manager, 0.75f);
            for (int i = 0; i < 1000; i++)
                Check(wait.MoveNext() && panel.activeSelf && SceneLoader.IsLoading && loader.Progress < 1f,
                    "맵 완료만으로 미완성 플레이어/HUD 화면을 드러내지 않음");
            typeof(GameManager).GetProperty("IsPlayerReady").SetValue(manager, true);
            Check(wait.MoveNext() && panel.activeSelf && loader.Progress == 1f, "준비 완료 후에도 최소 한 프레임 덮음");
            Time.timeScale = 0f;
            Check(wait.MoveNext() && wait.Current is WaitForSecondsRealtime, "완료 연출은 일시정지와 무관한 대기");
            Check(!wait.MoveNext() && !panel.activeSelf && !SceneLoader.IsLoading, "준비된 씬만 최종 공개");
            Debug.Log("[LoadingCheck] PASS: target-scene readiness, prolonged initialization, 100% only when ready, unscaled release.");
        }
        finally
        {
            Time.timeScale = previousTime;
            Object.DestroyImmediate(host);
            SceneManager.UnloadSceneAsync(scene);
            panel.SetActive(false);
            typeof(SceneLoader).GetField("_isLoading", Private).SetValue(loader, false);
        }
    }

    [MenuItem("Tools/Loading/Preview Loading Bar")]
    public static void Preview()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var cameraGo = new GameObject("Loading preview camera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraGo, scene);
        var camera = cameraGo.GetComponent<Camera>();
        camera.scene = scene;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.magenta; // 가림막에 빈틈이 있으면 바로 드러난다.
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        SceneManager.MoveGameObjectToScene(root, scene);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 10f;
        var data = new SerializedObject(root.GetComponent<SceneLoader>());
        ((GameObject)data.FindProperty("loadingScreen").objectReferenceValue).SetActive(true);
        ((Image)data.FindProperty("progressFill").objectReferenceValue).fillAmount = 0.55f;
        var target = new RenderTexture(1280, 720, 24);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            pixels.Apply();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/LoadingPreview.png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[LoadingCheck] FAIL: " + message);
    }
}

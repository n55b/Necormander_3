using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityNote;
using Object = UnityEngine.Object;

/// <summary>로딩 캔버스 회귀 검사. 런타임에 UI를 조립하지 않는다.
/// (기존 캔버스를 프리팹화하던 'Apply Existing Loading Canvas' 는 끝난 일회성 메뉴라 제거했다 —
///  다시 돌리면 손으로 꾸민 SceneLoader 프리팹 스타일을 고정값으로 되돌리고 StartScene 을 저장했다.)</summary>
public static class SceneLoadingTools
{
    private const string PrefabPath = "Assets/Resources/UI/SceneLoader.prefab";
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

    [MenuItem("Tools/Loading/Verify Loading Prefab")]
    public static void Verify()
    {
        var loader = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<SceneLoader>();
        var data = new SerializedObject(loader);
        var panel = (GameObject)data.FindProperty("loadingScreen").objectReferenceValue;
        var bar = (Image)data.FindProperty("progressFill").objectReferenceValue;
        // 색/스프라이트는 손으로 꾸민 스타일이라 안 본다. 가림막의 일(맨 위 · 불투명 · 클릭 차단)만 본다.
        var canvas = loader.GetComponent<Canvas>();
        int topUI = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/UI", "Assets/Resources/UI" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => p != PrefabPath)
            .SelectMany(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponentsInChildren<Canvas>(true))
            .Select(c => c.sortingOrder).DefaultIfEmpty(0).Max();
        Check(canvas != null && canvas.sortingOrder > topUI, $"로딩 캔버스가 다른 UI 캔버스(최고 {topUI})보다 위");
        var cover = panel != null ? panel.GetComponent<Graphic>() : null;
        Check(cover != null && cover.color.a >= 1f && cover.raycastTarget, "불투명 가림 화면과 후면 클릭 차단");
        Check(bar != null && bar.type == Image.Type.Filled, "진행 바 연결 (Filled — fillAmount 로 진행률 표시)");
        Check(data.FindProperty("loadingText").objectReferenceValue != null, "Loading 문구 연결");
        Debug.Log("[LoadingCheck] PASS: prefab, loading screen, raycast blocking, filled progress bar, label.");
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

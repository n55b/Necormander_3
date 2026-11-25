using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityNote;
using Object = UnityEngine.Object;

/// <summary>씬 준비, 입력 차단과 일시정지 중 팔레트 페이드를 실제 로더로 검사한다.</summary>
public static class SceneLoadingTools
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string TransitionCheck = "LoadingCheck.RunTransitions";

    [InitializeOnLoadMethod]
    private static void RegisterPlayCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(TransitionCheck, false)) return;
            SessionState.SetBool(TransitionCheck, false);
            SceneLoader.Instance.StartCoroutine(RunChecked(CheckTransitions()));
        };
    }

    [MenuItem("Tools/Loading/Run Scene Transition Check")]
    public static void RunTransitions()
    {
        Debug.Log($"[LoadingCheck] state: playing={Application.isPlaying}, loading={SceneLoader.IsLoading}, loader={SceneLoader.Instance}, progress={SceneLoader.Instance?.Progress}, ready={GameManager.Instance?.IsPlayerReady}");
        if (Application.isPlaying)
        {
            SceneLoader.Instance.StartCoroutine(RunChecked(CheckTransitions()));
            return;
        }
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "플레이를 끈 뒤 실행");
        SessionState.SetBool(TransitionCheck, true);
        EditorApplication.EnterPlaymode();
    }

    private static IEnumerator RunChecked(IEnumerator check)
    {
        var pending = new Stack<IEnumerator>();
        pending.Push(check);
        while (pending.Count > 0)
        {
            var current = pending.Peek();
            object yielded;
            try
            {
                if (!current.MoveNext()) { pending.Pop(); continue; }
                yielded = current.Current;
            }
            catch (Exception exception)
            {
                while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                yield break;
            }
            if (yielded is IEnumerator nested) pending.Push(nested);
            else yield return yielded;
        }
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static IEnumerator CheckTransitions()
    {
        // 에디터가 뒤로 가도 검사가 진행되게 한다. 프로젝트 설정은 바꾸지 않는다.
        bool previousBackground = Application.runInBackground;
        Application.runInBackground = true;
        try
        {
            yield return WaitForReady();
            yield return CheckReadiness();
            foreach (string destination in new[] { "StartScene", "BattleScene", "VillageScene" })
            {
                TutorialFlow.IsRunning = destination == "BattleScene";
                Check(SceneLoader.Load(destination), "씬 전환 시작: " + destination);
                Check(SceneLoader.IsLoading && !SceneLoader.Load(destination), "즉시 가림과 중복 전환 거부");
                yield return WaitForReady();
                Check(SceneManager.GetActiveScene().name == destination, "목적지 도착: " + destination);
                Check(Object.FindObjectsByType<SceneLoader>(FindObjectsSortMode.None).Length == 1, "로더 중복 없음");
                Debug.Log("[LoadingCheck] PASS: " + destination + " 준비 완료 뒤 공개.");
                if (destination == "BattleScene")
                {
                    Check(SceneLoader.Load(destination, FadeSignal.층이동), "층 이동 시작");
                    float deadline = Time.realtimeSinceStartup + 15f;
                    FloorPortalGraphic portal;
                    while ((portal = Object.FindFirstObjectByType<FloorPortalGraphic>()) == null)
                    {
                        Check(Time.realtimeSinceStartup < deadline, "플레이어 포탈 생성");
                        yield return null;
                    }
                    float opened = Time.unscaledTime;
                    bool sawClosingFade = false;
                    while (portal != null)
                    {
                        float fade = Shader.GetGlobalFloat("_ScenePaletteFade");
                        sawClosingFade |= fade > .01f && fade < .99f;
                        Check(Time.realtimeSinceStartup < deadline, "포탈 연출 종료");
                        yield return null;
                    }
                    float shown = Time.unscaledTime - opened;
                    Check(shown >= 2.88f && shown < 4.5f, $"층 이동 출발 연출 {shown:F2}초");
                    Check(sawClosingFade && Shader.GetGlobalFloat("_ScenePaletteFade") >= .99f, "포탈에서 암전까지 점진적으로 전환");
                    bool sawReveal = false;
                    var ready = WaitForReady();
                    while (ready.MoveNext())
                    {
                        float fade = Shader.GetGlobalFloat("_ScenePaletteFade");
                        sawReveal |= fade > .01f && fade < .99f;
                        yield return ready.Current;
                    }
                    Check(sawReveal && Shader.GetGlobalFloat("_ScenePaletteFade") == 0, "다음 층 페이드인 완료");
                    Check(SceneManager.GetActiveScene().name == destination, "다음 층 도착");
                    Debug.Log($"[LoadingCheck] PASS: next-floor portal visible for {shown:F2}s; BattleScene ready.");
                }
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

    [MenuItem("Tools/Loading/Verify Readiness In Play Mode")]
    public static void VerifyReadiness()
    {
        Check(Application.isPlaying, "플레이 모드에서 실행");
        SceneLoader.Instance.StartCoroutine(RunChecked(CheckReadiness()));
    }

    private static IEnumerator CheckReadiness()
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
            var wait = (IEnumerator)typeof(SceneLoader).GetMethod("FinishWhenReady", Private).Invoke(loader, new object[] { scene, false, false });
            Check(wait.MoveNext(), "Start 대기");
            typeof(GameManager).GetField("_loadingProgress", Private).SetValue(manager, 0.75f);
            for (int i = 0; i < 1000; i++)
                Check(wait.MoveNext() && panel.activeSelf && SceneLoader.IsLoading && loader.Progress < 1f,
                    "맵 완료만으로 미완성 플레이어/HUD 화면을 드러내지 않음");
            typeof(GameManager).GetProperty("IsPlayerReady").SetValue(manager, true);
            Check(wait.MoveNext() && panel.activeSelf && loader.Progress == 1f, "준비 완료 후에도 최소 한 프레임 덮음");
            Time.timeScale = 0f;
            Check(wait.MoveNext() && wait.Current is IEnumerator, "준비 완료 후 페이드 해제");
            yield return wait.Current;
            Check(Shader.GetGlobalFloat("_ScenePaletteFade") == 0f && Time.timeScale == 0f,
                "일시정지 중에도 페이드 해제");
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

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[LoadingCheck] FAIL: " + message);
    }
}

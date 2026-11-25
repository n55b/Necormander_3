using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum SceneNames { [UnityEngine.InspectorName("시작 씬")] StartScene, [UnityEngine.InspectorName("전투 씬")] BattleScene }

namespace UnityNote
{
    /// <summary>씬 전환의 공통 입구. 맵 생성만이 아니라 GameManager의 전체 준비 완료까지 화면을 가린다.</summary>
    public class SceneLoader : MonoBehaviour
    {
        public static SceneLoader Instance { get; private set; }
        public static bool IsLoading => Instance != null && Instance._isLoading;
        internal static bool FloorIsCovered { get; private set; }
        private const string PrefabPath = "UI/SceneLoader";

        [SerializeField] private GameObject loadingScreen;
        [SerializeField] private Image loadingBackGround;
        [SerializeField] private Sprite[] loadingSprites;
        [SerializeField] private Image progressFill;
        [SerializeField] private TMP_Text loadingText;
        [Tooltip("모든 초기화가 끝난 뒤 로딩 화면을 유지할 실시간 초. 준비 대기 제한 시간이 아니다.")]
        [SerializeField] private float waitChangeDelay = 0.5f;

        [SerializeField, Range(.06f, .5f)] private float fadeSeconds = .18f;
        private static readonly int FadeId = Shader.PropertyToID("_ScenePaletteFade");
        private float fade;
        private bool _isLoading;
        private bool _loadingScene;
        private readonly HashSet<string> preparedScenes = new();
        public float Progress { get; private set; }
#if NECRO_RENDER_BENCH
        internal static string TravelStage { get; private set; }
#endif
        [System.Diagnostics.Conditional("NECRO_RENDER_BENCH")]
        private static void MarkTravelStage(string stage)
        {
#if NECRO_RENDER_BENCH
            TravelStage = stage;
#endif
        }

        // BattleScene을 직접 실행해도 첫 렌더 전에 같은 저작 프리팹으로 가린다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() => GetOrCreate().Show();

        private static SceneLoader GetOrCreate()
        {
            if (Instance != null) return Instance;
            var prefab = Resources.Load<SceneLoader>(PrefabPath);
            if (prefab == null) throw new System.InvalidOperationException("[SceneLoader] Resources/UI/SceneLoader 프리팹이 없습니다.");
            return Instantiate(prefab);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) { FloorIsCovered = false; Shader.SetGlobalFloat(FadeId, 0); Instance = null; }
        }

        private IEnumerator Start()
        {
            // 최초 실행은 씬 로드 이벤트의 모드/호출 순서에 의존하지 않는다.
            // 이후 전환은 LoadSceneAsync가 같은 완료 대기를 직접 호출한다.
            if (Instance == this && !_loadingScene)
                yield return FinishWhenReady(SceneManager.GetActiveScene());
        }

        public static bool Load(string name, FadeSignal signal = FadeSignal.씬전환)
            => GetOrCreate().BeginLoad(name, signal);

        // 기존 버튼/코드 진입점을 유지한다.
        public void LoadScene(string name) => BeginLoad(name, FadeSignal.씬전환);
        public void LoadScene(SceneNames name) => LoadScene(name.ToString());

        private bool BeginLoad(string name, FadeSignal signal)
        {
            if (_isLoading) return false;
            bool canLoad = !string.IsNullOrWhiteSpace(name) && Application.CanStreamedLevelBeLoaded(name);
#if UNITY_EDITOR
            if (name == WorkshopTrial.SceneName) canLoad = WorkshopTrial.Available;
#endif
            if (!canLoad)
            {
                Debug.LogError($"[SceneLoader] 빌드에 등록되지 않은 씬: {name}");
                return false;
            }

            _loadingScene = true;
            Show(); // LoadSceneAsync보다 먼저 가리고 클릭/플레이어 입력도 막는다.
            if (GameManager.Instance != null)
                GameManager.Instance.PLAYERCONTROLLER?.SetInputBlocked(true);
            Signal.Fire(signal);
            // 씬이 바뀌면 지난 씬 BGM(예: 보스방)을 끈다. 새 씬은 방 입장 시 자기 BGM을 다시 튼다.
            if (SoundManager.Instance != null) SoundManager.Instance.StopBGM();
            StartCoroutine(LoadSceneAsync(name, signal == FadeSignal.층이동));
            return true;
        }

        private void Show()
        {
            _isLoading = true;
            Progress = 0f;
            progressFill.transform.parent.gameObject.SetActive(false);
            loadingText.gameObject.SetActive(false);
            loadingBackGround.gameObject.SetActive(false);
            loadingScreen.GetComponent<Image>().color = Color.clear;
            loadingScreen.SetActive(true);
            SetFade(_loadingScene ? 0f : 1f);

        }

        private void SetProgress(float value)
        {
            Progress = Mathf.Max(Progress, Mathf.Clamp01(value));

        }

        private void SetFade(float value)
        {
            if (value < 1f) FloorIsCovered = false;
            fade = value;
            Shader.SetGlobalFloat(FadeId, value);
        }

        private IEnumerator FadeTo(float target, float duration = 0)
        {
            float start = fade;
            if (start == target) yield break;
            float elapsed = 0;
            duration = Mathf.Max(.01f, duration > 0 ? duration : fadeSeconds) * Mathf.Abs(start - target);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetFade(Mathf.SmoothStep(start, target, elapsed / duration));
                yield return null;
            }
        }

        private IEnumerator LoadSceneAsync(string name, bool nextFloor)
        {
            var previousPriority = Application.backgroundLoadingPriority;
            float previousIntegration = AsyncInstantiateOperation.GetIntegrationTimeMS();
            try
            {
                if (nextFloor)
                {
                    Application.backgroundLoadingPriority = ThreadPriority.Low;
                    AsyncInstantiateOperation.SetIntegrationTimeMS(Mathf.Min(2f, previousIntegration));
                }
                yield return LoadAndReveal(name, nextFloor);
            }
            finally
            {
                if (nextFloor)
                {
                    AsyncInstantiateOperation.SetIntegrationTimeMS(previousIntegration);
                    Application.backgroundLoadingPriority = previousPriority;
                    FloorIsCovered = false;
                }
            }
        }

        private IEnumerator LoadAndReveal(string name, bool nextFloor)
        {
            // 무거운 씬 로드가 첫 프레임을 막아도 UI가 먼저 한 번 그려진 뒤 시작한다.
            MarkTravelStage("portal capture");
            FloorPortalGraphic portal = null;
            if (nextFloor)
                yield return FloorPortalGraphic.CaptureAsync(GameManager.Instance?.PLAYERCONTROLLER, captured => portal = captured);
            yield return null;
            // 처음 쓰는 씬은 셰이더·에셋 통합이 화면을 멈출 수 있어, 페이드가 덮인 다음 읽는다.
            var operation = !nextFloor && preparedScenes.Contains(name) ? StartStreaming(name) : null;
            MarkTravelStage("portal animation");
            if (portal != null)
            {
                float time = 0;
                const float portalDuration = FloorTravel.Seconds;
                while (time < portalDuration)
                {
                    // Keep a slow first frame from skipping the tint or the pose handoff.
                    time += Mathf.Min(Time.unscaledDeltaTime, .05f);
                    float t = time / portalDuration;
                    portal.SetProgress(t);
                    SetFade(FloorTravel.Ease(.90f, 1, t));
                    yield return null;
                }
                SetFade(1);
                Destroy(portal.gameObject);
            }
            else yield return FadeTo(1f);
            yield return null;
            Scene handoff = default;
            if (nextFloor)
            {
                FloorIsCovered = true;
                var oldFloor = SceneManager.GetActiveScene();
                MarkTravelStage("retiring rooms");
                if (MapGenerator.Instance != null) yield return MapGenerator.Instance.RetireFloorRooms();
                if (SceneManager.sceneCount == 1)
                {
                    MarkTravelStage("scene unload");
                    handoff = SceneManager.CreateScene("Floor handoff");
                    SceneManager.SetActiveScene(handoff);
                    yield return SceneManager.UnloadSceneAsync(oldFloor);
                }
            }
            MarkTravelStage("scene streaming");
            bool staged = handoff.IsValid();
            operation ??= StartStreaming(name, staged ? LoadSceneMode.Additive : LoadSceneMode.Single);
            while (operation.progress < 0.9f)
            {
                SetProgress(0.3f * operation.progress / 0.9f);
                yield return null;
            }
            SetProgress(0.3f);
            // 일시정지/사망 화면에서 넘어와도 초기화 코루틴은 정상 진행한다.
            HitStopManager.ResetForScene();
            MarkTravelStage("scene activation");
            void ActivateFloor(Scene scene, LoadSceneMode mode)
            {
                if (mode != LoadSceneMode.Additive || scene.name != name) return;
                SceneManager.SetActiveScene(scene);
                // Unparented objects created by incoming Awake callbacks initially belong to the handoff.
                foreach (var root in handoff.GetRootGameObjects())
                    SceneManager.MoveGameObjectToScene(root, scene);
            }
            if (staged) SceneManager.sceneLoaded += ActivateFloor;
            try
            {
                operation.allowSceneActivation = true;
                while (!operation.isDone) yield return null;
            }
            finally { if (staged) SceneManager.sceneLoaded -= ActivateFloor; }
            if (staged) yield return SceneManager.UnloadSceneAsync(handoff);
            MarkTravelStage("scene startup");
            yield return FinishWhenReady(SceneManager.GetActiveScene(), staged, nextFloor);
        }

        private static AsyncOperation StartStreaming(string name, LoadSceneMode mode = LoadSceneMode.Single)
        {
            AsyncOperation operation;
#if UNITY_EDITOR
            // The authoring scene is intentionally outside the game's build scene list.
            if (name == WorkshopTrial.SceneName)
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    WorkshopTrial.ScenePath, new LoadSceneParameters(mode));
            else
#endif
                operation = SceneManager.LoadSceneAsync(name, mode);
            operation.allowSceneActivation = false;
            return operation;
        }

        private IEnumerator FinishWhenReady(Scene scene, bool releaseUnusedAssets = false, bool floorArrival = false)
        {
            yield return null; // Start까지 실행한 뒤 새 씬의 매니저만 확인한다.
            GameManager manager = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                manager = root.GetComponentInChildren<GameManager>(true);
                if (manager != null) break;
            }

            if (manager != null)
            {
                while (manager != null && !manager.IsPlayerReady)
                {
                    SetProgress(0.3f + 0.7f * manager.LoadingProgress);
                    if (!string.IsNullOrEmpty(manager.InitializationError))
                    {
                        SetFade(0);
                        loadingScreen.GetComponent<Image>().color = Color.black;
                        loadingText.gameObject.SetActive(true);
                        loadingText.text = "씬 준비 실패 · 게임을 다시 시작해 주세요.";
                        Debug.LogError("[SceneLoader] " + manager.InitializationError);
                        yield break; // 실패를 성공처럼 취급해 미완성 씬을 드러내지 않는다.
                    }
                    yield return null;
                }
                if (manager == null) yield break;
            }

            if (releaseUnusedAssets)
            {
                // Single loads sweep all loaded assets during activation. Give the sweep its own covered frame.
                MarkTravelStage("unused asset release");
                yield return null;
                yield return Resources.UnloadUnusedAssets();
                yield return null;
            }
            FloorIsCovered = false;
            preparedScenes.Add(scene.name);
            SetProgress(1f);
            yield return null; // 준비된 HUD와 카메라가 그려진 다음 페이드를 해제한다.
            yield return FadeTo(0, floorArrival ? .29f : fadeSeconds);
            loadingScreen.SetActive(false);
            _isLoading = false;
            _loadingScene = false;
            manager?.PLAYERCONTROLLER?.SetInputBlocked(false);
        }
    }
}

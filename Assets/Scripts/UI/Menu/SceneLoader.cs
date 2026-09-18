using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum SceneNames { StartScene, BattleScene }

namespace UnityNote
{
    /// <summary>씬 전환의 공통 입구. 맵 생성만이 아니라 GameManager의 전체 준비 완료까지 화면을 가린다.</summary>
    public class SceneLoader : MonoBehaviour
    {
        public static SceneLoader Instance { get; private set; }
        public static bool IsLoading => Instance != null && Instance._isLoading;
        private const string PrefabPath = "UI/SceneLoader";

        [SerializeField] private GameObject loadingScreen;
        [SerializeField] private Image loadingBackGround;
        [SerializeField] private Sprite[] loadingSprites;
        [SerializeField] private Image progressFill;
        [SerializeField] private TMP_Text loadingText;
        [Tooltip("모든 초기화가 끝난 뒤 로딩 화면을 유지할 실시간 초. 준비 대기 제한 시간이 아니다.")]
        [SerializeField] private float waitChangeDelay = 0.5f;

        private bool _isLoading;
        private bool _loadingScene;
        public float Progress { get; private set; }

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
            if (Instance == this) Instance = null;
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
            if (string.IsNullOrWhiteSpace(name) || !Application.CanStreamedLevelBeLoaded(name))
            {
                Debug.LogError($"[SceneLoader] 빌드에 등록되지 않은 씬: {name}");
                return false;
            }

            _loadingScene = true;
            Show(); // LoadSceneAsync보다 먼저 가리고 클릭/플레이어 입력도 막는다.
            if (GameManager.Instance != null)
                GameManager.Instance.PLAYERCONTROLLER?.SetInputBlocked(true);
            Signal.Fire(signal);
            StartCoroutine(LoadSceneAsync(name));
            return true;
        }

        private void Show()
        {
            _isLoading = true;
            Progress = 0f;
            progressFill.fillAmount = 0f;
            loadingText.text = "Loading...";
            if (loadingSprites != null && loadingSprites.Length > 0)
                loadingBackGround.sprite = loadingSprites[Random.Range(0, loadingSprites.Length)];
            loadingScreen.SetActive(true);
            Canvas.ForceUpdateCanvases();
        }

        private void SetProgress(float value)
        {
            Progress = Mathf.Max(Progress, Mathf.Clamp01(value));
            progressFill.fillAmount = Progress;
        }

        private IEnumerator LoadSceneAsync(string name)
        {
            // 무거운 씬 로드가 첫 프레임을 막아도 UI가 먼저 한 번 그려진 뒤 시작한다.
            yield return null;
            yield return null;
            var operation = SceneManager.LoadSceneAsync(name);
            operation.allowSceneActivation = false;
            while (operation.progress < 0.9f)
            {
                SetProgress(0.3f * operation.progress / 0.9f);
                yield return null;
            }
            SetProgress(0.3f);
            // 일시정지/사망 화면에서 넘어와도 초기화 코루틴은 정상 진행한다.
            Time.timeScale = 1f;
            operation.allowSceneActivation = true;
            while (!operation.isDone) yield return null;
            yield return FinishWhenReady(SceneManager.GetActiveScene());
        }

        private IEnumerator FinishWhenReady(Scene scene)
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
                        loadingText.text = "Loading failed. Please restart.";
                        Debug.LogError("[SceneLoader] " + manager.InitializationError);
                        yield break; // 실패를 성공처럼 취급해 미완성 씬을 드러내지 않는다.
                    }
                    yield return null;
                }
                if (manager == null) yield break;
            }

            SetProgress(1f);
            yield return null; // HUD/카메라 LateUpdate와 완성된 진행 바를 먼저 반영한다.
            if (waitChangeDelay > 0f) yield return new WaitForSecondsRealtime(waitChangeDelay);
            loadingScreen.SetActive(false);
            _isLoading = false;
            _loadingScene = false;
            manager?.PLAYERCONTROLLER?.SetInputBlocked(false);
        }
    }
}

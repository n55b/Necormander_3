using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button loadButton;
    [SerializeField] private Button exitButton;

    [Header("Scene Settings")]
    [Tooltip("Load 버튼을 눌렀을 때 이동할 메인 게임 씬의 이름입니다.")]
    [SerializeField] private string gameSceneName = "Map"; 
    [SerializeField] private string startSceneName = "VillageScene";
    [Tooltip("최초 1회 거치는 튜토리얼 맵이 지어질 씬. 던전과 같은 씬을 쓴다 — 맵 생성기/문/미니맵을 " +
             "그대로 재사용하고, 방 목록만 MapGenerationData 의 Tutorial Rooms 로 갈아끼운다.")]
    [SerializeField] private string tutorialSceneName = "BattleScene";

    [Header("Debug")]
    [Tooltip("켜면 '새 게임'이 완료 기록을 무시하고 항상 튜토리얼부터 시작한다.\n\n" +
             "튜토리얼로 갈지 마을로 갈지는 오직 이 버튼이 정하므로, 강제 스위치도 여기 있어야 한다 — " +
             "던전 쪽(GameManager)에 두면 이 갈림길엔 닿지도 못하면서, 켜둔 채로 마을 포탈을 타면 " +
             "던전까지 튜토리얼로 지어버린다.\n\n" +
             "진행 자체는 진짜와 똑같다. 끝까지 깨면 완료 기록도 정상적으로 써진다.")]
    [SerializeField] private bool debugForceTutorial = false;

    private UnityNote.SceneLoader sceneLoader;

private void Start()
    {
        // 타이틀로 돌아왔을 때 DontDestroyOnLoad로 살아남은 지난 BGM이 계속 나오는 문제 방지.
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.StopBGM();
        }

        // 타이틀에 있다는 건 어떤 튜토리얼도 진행 중이 아니라는 뜻이다. 튜토리얼 도중 타이틀로
        // 빠져나온 뒤 '이어하기'를 누르면 던전이 튜토리얼 맵으로 지어지던 것을 여기서 끊는다.
        TutorialFlow.IsRunning = false;

        sceneLoader = UnityNote.SceneLoader.Instance;

        InitializeButtons();
    }

    private void InitializeButtons()
    {
        if (startButton == null)
        {
            Debug.LogWarning("[MainMenuManager] Start Button이 연결되지 않았습니다!");
            return;
        }

        // 1. 세이브 파일 존재 여부에 따른 Load Button 활성/비활성 처리
        bool saveExists = SaveSystem.SaveExists();
        if (loadButton != null)
        {
            loadButton.interactable = saveExists;
            
            // 클릭 이벤트 연결
            loadButton.onClick.RemoveAllListeners();
            loadButton.onClick.AddListener(OnLoadButtonClicked);
        }
        else
        {
            Debug.LogWarning("[MainMenuManager] Load Button이 연결되지 않았습니다! 세이브 파일을 로드하려면 인스펙터에서 버튼을 연결해 주세요.");
        }

        if (exitButton != null)
        {
            // 클릭 이벤트 연결
            exitButton.onClick.RemoveAllListeners();
            exitButton.onClick.AddListener(GameExit);
        }
        else
        {
            Debug.LogWarning("[MainMenuManager] Exit Button이 연결되지 않았습니다! 게임을 종료하려면 인스펙터에서 버튼을 연결해 주세요.");
        }

        // 2. Start Button 클릭 이벤트 연결
        startButton.onClick.RemoveAllListeners();
        startButton.onClick.AddListener(OnNewGameButtonClicked);
    }

    private void OnNewGameButtonClicked()
    {
        Debug.Log("<color=cyan>[MainMenuManager]</color> New Game 시작! 기존 세이브 파일을 삭제합니다.");
        
        // 기존 세이브 데이터 안전 삭제.
        // 우클릭 해금은 영구 성장요소라 여기서 안 지운다 — '새 게임'은 런을 새로 시작하는 것이지
        // 그동안 쌓은 영구 해금을 되돌리는 게 아니다.
        SaveSystem.DeleteSave();

        // 튜토리얼을 아직 안 봤으면 마을보다 먼저 거친다. 완료 기록은 세이브가 아니라 PlayerPrefs 라
        // (우클릭 해금과 같은 취급) '새 게임'으로 세이브를 지워도 두 번 보지는 않는다.
        if (debugForceTutorial || !TutorialFlow.Completed)
        {
            TutorialFlow.Begin();
            Debug.Log(debugForceTutorial
                ? "<color=yellow>[MainMenuManager]</color> Debug Force Tutorial — 완료 기록을 무시하고 튜토리얼부터 시작합니다."
                : "<color=cyan>[MainMenuManager]</color> 첫 플레이 — 튜토리얼부터 시작합니다.");
            sceneLoader.LoadScene(tutorialSceneName);
            return;
        }

        // 지정된 게임 씬으로 이동
        sceneLoader.LoadScene(startSceneName);
    }

    private void OnLoadButtonClicked()
    {
        Debug.Log("<color=cyan>[MainMenuManager]</color> Load Game 시작! 기존 세이브 파일을 유지하고 로드합니다.");
        
        // 지정된 게임 씬으로 이동
        sceneLoader.LoadScene(gameSceneName);
    }

    private void GameExit()
    {
        Debug.Log("<color=cyan>[MainMenuManager]</color> 게임 종료!");
        Application.Quit();
    }

    public void OpenOptionSceneDirectly()
    {
        Debug.Log("<color=cyan>[MainMenuManager]</color> 옵션 설정 창으로 바로 이동합니다.");
        // 옵션 씬이 열릴 때 설정 창(Settings)으로 직행하도록 플래그 설정
        OptionManager.OpenDirectlyToSettings = true;
        // 옵션 씬 중첩 로드
        SceneManager.LoadScene("OptionScene", LoadSceneMode.Additive);
    }
}

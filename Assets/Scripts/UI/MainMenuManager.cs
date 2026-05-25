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
    [Tooltip("Start 버튼을 눌렀을 때 이동할 메인 게임 씬의 이름입니다.")]
    [SerializeField] private string gameSceneName = "Map"; 

    private void Awake()
    {
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
        
        // 기존 세이브 데이터 안전 삭제
        SaveSystem.DeleteSave();
        
        // 지정된 게임 씬으로 이동
        SceneManager.LoadScene(gameSceneName);
    }

    private void OnLoadButtonClicked()
    {
        Debug.Log("<color=cyan>[MainMenuManager]</color> Load Game 시작! 기존 세이브 파일을 유지하고 로드합니다.");
        
        // 지정된 게임 씬으로 이동
        SceneManager.LoadScene(gameSceneName);
    }

    private void GameExit()
    {
        Debug.Log("<color=cyan>[MainMenuManager]</color> 게임 종료!");
        Application.Quit();
    }

}

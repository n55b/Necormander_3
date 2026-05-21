using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button startButton;

    [Header("Scene Settings")]
    [Tooltip("Start 버튼을 눌렀을 때 이동할 메인 게임 씬의 이름입니다.")]
    [SerializeField] private string gameSceneName = "Map"; 

    private void Awake()
    {
        // 버튼에 클릭 이벤트 연결
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartButtonClicked);
        }
        else
        {
            Debug.LogWarning("[MainMenuManager] Start Button이 연결되지 않았습니다!");
        }
    }

    private void OnStartButtonClicked()
    {
        Debug.Log($"<color=cyan>[MainMenuManager]</color> 게임 시작! 씬 로드: {gameSceneName}");
        
        // TODO: 향후 페이드 아웃 효과나 사운드 재생 등을 여기에 추가할 수 있습니다.
        
        // 지정된 게임 씬으로 이동
        SceneManager.LoadScene(gameSceneName);
    }
}

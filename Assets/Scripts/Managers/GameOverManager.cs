using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverManager : Singleton<GameOverManager>
{
    public GameObject gameOverPanel; // 게임오버 UI 패널

    // Instance 할당은 Singleton<T> 베이스가 Awake에서 처리(가드 포함). Start 레이스 해소.

    private void Start()
    {
        if(gameOverPanel != null)gameOverPanel.SetActive(false); // 게임 시작 시 게임오버 패널 비활성화
    }

    public void TriggerGameOver()
    {
        gameOverPanel.SetActive(true); // 게임오버 패널 활성화
    }

    public void RestartGame()
    {
        GameManager.Instance.SetTimeStop(false); // 게임 시간 재개
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); // 현재 씬 재로드
    }

    public void GoToMainMenu()
    {
        GameManager.Instance.SetTimeStop(false); // 게임 시간 재개
        SceneManager.LoadScene("StartScene"); // 메인 메뉴 씬으로 이동
    }
}

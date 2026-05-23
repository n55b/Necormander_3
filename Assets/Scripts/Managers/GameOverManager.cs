using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverManager : MonoBehaviour
{
    public static GameOverManager Instance;

    public GameObject gameOverPanel; // 게임오버 UI 패널

    private void Start()
    {
        Instance = this;
        gameOverPanel.SetActive(false); // 게임 시작 시 게임오버 패널 비활성화
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

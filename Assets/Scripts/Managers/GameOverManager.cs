using TMPro;
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

    /// <summary>
    /// 던전 마지막 층(3층) 보스를 클리어했을 때 호출됩니다.
    /// 기존 GameOverUI(gameOverPanel)를 그대로 재사용하되, 자식 Text (TMP)의 문구만
    /// "GameClear!"로 바꿔서 띄웁니다.
    /// </summary>
    public void TriggerGameClear()
    {
        if (gameOverPanel != null)
        {
            var textTransform = gameOverPanel.transform.Find("Text (TMP)");
            var tmp = textTransform != null ? textTransform.GetComponent<TextMeshProUGUI>() : null;
            if (tmp != null) tmp.text = "GameClear!";

            gameOverPanel.SetActive(true);
        }
    }

    public void RestartGame()
    {
        GameManager.Instance.SetTimeStop(false); // 게임 시간 재개
        SceneManager.LoadScene("VillageScene"); // 빌리지 씬 재로드
    }

    public void GoToMainMenu()
    {
        GameManager.Instance.SetTimeStop(false); // 게임 시간 재개
        SceneManager.LoadScene("StartScene"); // 메인 메뉴 씬으로 이동
    }
}

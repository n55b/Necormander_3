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
        ClearCombatWarnings();
        SetTitle(Loc.T("GameOverManager.game_over", "게임 오버"));
        gameOverPanel.SetActive(true); // 게임오버 패널 활성화
        UIEventBus.NotifyOpen("GameOver"); // UI 소리 설정의 창 이름
    }

    /// <summary>
    /// 던전 마지막 층(3층) 보스를 클리어했을 때 호출됩니다.
    /// 기존 GameOverUI(gameOverPanel)를 그대로 재사용하되, 자식 Text (TMP)의 문구만
    /// "게임 클리어!"로 바꿔서 띄웁니다.
    /// </summary>
    public void TriggerGameClear()
    {
        ClearCombatWarnings();
        if (gameOverPanel != null)
        {
            SetTitle(Loc.T("GameOverManager.game_clear", "게임 클리어!"));

            gameOverPanel.SetActive(true);
            UIEventBus.NotifyOpen("GameClear"); // UI 소리 설정의 창 이름
        }
    }

    /// <summary>
    /// 패널 제목(자식 Text (TMP))은 게임오버/클리어가 같이 쓰므로 여기서 매번 정한다.
    /// 프리팹 고정 글자(LocTMP)로 두면 패널이 켜질 때 클리어 문구를 덮어쓴다.
    /// </summary>
    private void SetTitle(string title)
    {
        var textTransform = gameOverPanel != null ? gameOverPanel.transform.Find("Text (TMP)") : null;
        var tmp = textTransform != null ? textTransform.GetComponent<TextMeshProUGUI>() : null;
        if (tmp != null) tmp.text = title;
    }

    private void ClearCombatWarnings()
    {
        FeedbackOverlay.Instance?.SuppressCombatWarnings();
        GameManager.Instance?.ClearDamageScreenFeedback();
    }

    public void RestartGame()
    {
        GameManager.Instance.SetTimeStop(false); // 게임 시간 재개
        UnityNote.SceneLoader.Load("VillageScene");
    }

    public void GoToMainMenu()
    {
        GameManager.Instance.SetTimeStop(false); // 게임 시간 재개
        UnityNote.SceneLoader.Load("StartScene");
    }
}

using UnityEngine;

public class OptionManager : MonoBehaviour
{
    public void CloseOption()
    {
        SceneOptionManager.Instance.CloseOptionScene();
    }

    public void LoadMainMenu()
    {
        GameManager.Instance.SetTimeStop(false); // 게임 시간 재개
        UnityEngine.SceneManagement.SceneManager.LoadScene("StartScene"); // 메인 메뉴 씬으로 이동
    }
}

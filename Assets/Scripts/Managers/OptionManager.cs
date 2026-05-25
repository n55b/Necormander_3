using UnityEngine;

public class OptionManager : MonoBehaviour
{
    public void CloseOption()
    {
        SceneOptionManager.Instance.CloseOptionScene();
    }

    public void LoadMainMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("StartScene"); // 메인 메뉴 씬으로 이동
    }
}

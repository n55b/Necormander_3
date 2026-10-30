using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneOptionManager : MonoBehaviour
{
    public static SceneOptionManager Instance;

    public bool isOptionOpen = false;

    private void Awake()
    {
        Instance = this;
    }

    public void OpenOptionScene()
    {
        // 게임을 멈추고, 옵션 중에는 다른 창이 안 뜨게 한다(키로 여는 창은 거절, 보상 등 이벤트 창은 보류).
        isOptionOpen = UIPopUpManager.Instance == null || UIPopUpManager.Instance.OpenOption();
        if(isOptionOpen)
        {
            SceneManager.LoadScene("OptionScene", LoadSceneMode.Additive);
            UIEventBus.NotifyOpen("Option"); // UI 소리 설정의 창 이름
        }
    }

    public void CloseOptionScene()
    {
        if (isOptionOpen) UIEventBus.NotifyClose("Option");
        isOptionOpen = false;

        UIPopUpManager.Instance?.CloseOption(); // 게임 재개 + 보류된 창 띄우기
        SceneManager.UnloadSceneAsync("OptionScene");
    }
}
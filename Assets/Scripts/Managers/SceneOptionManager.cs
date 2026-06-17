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
        isOptionOpen = UIPopUpManager.Instance.PushOptionKey();
        if(isOptionOpen)
            SceneManager.LoadScene("OptionScene", LoadSceneMode.Additive);
    }

    public void CloseOptionScene()
    {
        isOptionOpen = false;

        UIPopUpManager.Instance.CloseOption();
        SceneManager.UnloadSceneAsync("OptionScene");
    }
}
using UnityEngine;
using UnityEngine.SceneManagement;

public class DungeonTeleporter : MonoBehaviour, IInteractable
{
     public virtual string InteractionPrompt => "F : 상호작용";


    [SerializeField] private string DungeonSceneName = "BattleScene";

public bool Interact(GameObject interactor)
    {
        // 마을에서 장착한 스킬/미니언/골드/보물/보석/체력이 던전(BattleScene)까지 유지되도록,
        // 씬 이동 직전에 현재 상태를 저장한다. (마을 디버그 로드아웃 메뉴로 세팅한 것도 이걸로 넘어감)
        if (GameManager.Instance != null) GameManager.Instance.SaveCurrentState();

        if (ScreenFadeController.Instance != null)
        {
            ScreenFadeController.Instance.FadeOutIn(0.5f, 0.2f, 0.5f, () =>
            {
                SceneManager.LoadScene(DungeonSceneName);
            });
        }
        else
        {
            SceneManager.LoadScene(DungeonSceneName);
        }

        return true;
    }

    public void OnFocused(GameObject interactor){}
    public void OnLostFocus(GameObject intreactor) {}
}

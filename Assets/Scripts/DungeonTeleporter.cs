using UnityEngine;
using UnityEngine.SceneManagement;

public class DungeonTeleporter : MonoBehaviour, IInteractable
{
     public virtual string InteractionPrompt => "F : 상호작용";


    [SerializeField] private string DungeonSceneName = "BattleScene";

public bool Interact(GameObject interactor)
    {
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

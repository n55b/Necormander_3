using UnityEngine;

public class DungeonTeleporter : MonoBehaviour, IInteractable
{
     public virtual string InteractionPrompt => "F : 상호작용";


    [SerializeField] private string DungeonSceneName = "BattleScene";

public bool Interact(GameObject interactor)
    {
        if (UnityNote.SceneLoader.IsLoading) return false;
        if (GameManager.Instance != null && !GameManager.Instance.SaveCurrentState()) return false;

        return UnityNote.SceneLoader.Load(DungeonSceneName);
    }

    public void OnFocused(GameObject interactor){}
    public void OnLostFocus(GameObject intreactor) {}
}

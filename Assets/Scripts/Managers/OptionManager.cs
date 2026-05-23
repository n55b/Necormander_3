using UnityEngine;

public class OptionManager : MonoBehaviour
{
    public void CloseOption()
    {
        SceneOptionManager.Instance.CloseOptionScene();
    }
}

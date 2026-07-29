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

        // 페이드 양(시간/색)은 여기 박지 않는다. '씬전환'이라는 이름만 넘기면
        // ScreenFadeCanvas의 Fader가 그 이름의 줄을 찾아 쓴다 → 기획자가 거기서 조절.
        Fader fader = Fader.FullScreenFader;
        if (fader != null)
        {
            fader.FadeOutIn(FadeSignal.씬전환, () => SceneManager.LoadScene(DungeonSceneName));
        }
        else
        {
            SceneManager.LoadScene(DungeonSceneName); // 페이더를 못 구해도 이동은 반드시 되어야 한다
        }

        return true;
    }

    public void OnFocused(GameObject interactor){}
    public void OnLostFocus(GameObject intreactor) {}
}

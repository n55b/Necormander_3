using UnityEngine;
using UnityEngine.InputSystem;

public class MapUIManager : MonoBehaviour
{
    [SerializeField] private GameObject fullMapUIWindow;
    private InputAction mapToggle;
    public bool IsMapOpen => UIPopUpManager.Instance != null && UIPopUpManager.Instance.IsOpen(fullMapUIWindow); // 열림 여부는 팝업 매니저가 기준

    private void Update()
    {
        if (mapToggle != null) return;
        var game = GameManager.Instance;
        if (game == null || !game.IsPlayerReady || game.PLAYERCONTROLLER == null) return;
        if (!game.PLAYERCONTROLLER.TryGetComponent<PlayerInput>(out var input)) return;
        mapToggle = input.actions["MapToggle"];
        mapToggle.performed += OnMapTogglePressed;
    }

    private void OnDisable()
    {
        // PlayerInput can already be destroyed when the UI scene is torn down.
        if (mapToggle == null) return;
        mapToggle.performed -= OnMapTogglePressed;
        mapToggle = null;
    }

    public void CloseMapUI()
    {
        if (IsMapOpen) ToggleFullMap(false);
    }

    private void OnMapTogglePressed(InputAction.CallbackContext context)
    {
        ToggleFullMap(!IsMapOpen);
    }
    public void ToggleFullMap(bool isOpen)
    {
        var mgr = UIPopUpManager.Instance;
        if (mgr == null) return;

        if (isOpen)
        {
            // 전투 중 / 보상·옵션·대화 등이 떠 있으면 매니저가 거절한다. 주머니·장착 정보는 매니저가 닫는다.
            if (!mgr.Open(fullMapUIWindow, UIPopUpManager.Layer.Window, OnMapClosed)) return;

            UIEventBus.NotifyOpen("Map");
            if (UIBasedMiniMap.Instance != null)
            {
                UIBasedMiniMap.Instance.SetHudVisible(false);
                UIBasedMiniMap.Instance.RefreshMap();
            }
        }
        else
        {
            mgr.Close(fullMapUIWindow); // 정리는 OnMapClosed 에서
        }
    }

    /// <summary>직접 닫든, ESC·보상창 등장·전투 진입으로 매니저가 대신 닫든 여기로 온다.</summary>
    private void OnMapClosed()
    {
        UIEventBus.NotifyClose("Map");
        UIBasedMiniMap.Instance?.SetHudVisible(true);
    }
}

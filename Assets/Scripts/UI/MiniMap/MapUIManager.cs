// ==================== MapUIManager.cs 수정 ====================
using UnityEngine;
using UnityEngine.InputSystem;

public class MapUIManager : MonoBehaviour
{
    [SerializeField] private GameObject fullMapUIWindow;
    private PlayerInput _playerInput;
    private bool _isInitialized = false; // 중복 초기화 방지용 변수
    public bool IsMapOpen => UIPopUpManager.Instance != null && UIPopUpManager.Instance.IsOpen(fullMapUIWindow); // 열림 여부는 팝업 매니저가 기준

    private void Update()
    {
        // 아직 초기화가 안 되었다면, GameManager가 플레이어를 스폰 완료했는지 매 프레임 체크합니다.
        if (!_isInitialized)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsPlayerReady)
            {
                InitializeMapUI();
            }
            return; // 플레이어가 준비될 때까지 조작 및 Update 문 아래쪽 실행을 막습니다.
        }
    }

    private void OnDestroy()
    {
        // 이벤트 해제 안전장치
        if (_playerInput != null)
        {
            _playerInput.actions["MapToggle"].performed -= OnMapTogglePressed;
        }
    }

    private void InitializeMapUI()
    {
        _playerInput = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerInput>();
        if (_playerInput != null)
        {
            _playerInput.actions["MapToggle"].performed += OnMapTogglePressed;
            _isInitialized = true; //  이제 다 찾았으니 Update의 감시를 종료합니다.
            Debug.Log("<color=green>[MapUIManager]</color> 인풋 바인딩 성공!");
        }
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

// ==================== MapUIManager.cs 수정 ====================
using UnityEngine;
using UnityEngine.InputSystem;

public class MapUIManager : MonoBehaviour
{
    [SerializeField] private GameObject fullMapUIWindow;
    private PlayerInput _playerInput;
    private bool _isMapOpen = false;
    private bool _isInitialized = false; // 중복 초기화 방지용 변수
    public bool IsMapOpen => _isMapOpen; // 다른 스크립트에서 맵 상태 확인용 프로퍼티

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
        if (_isMapOpen)
        {
            ToggleFullMap(false);
        }
    }

    private void OnMapTogglePressed(InputAction.CallbackContext context)
    {
        ToggleFullMap(!_isMapOpen);
    }
    public void ToggleFullMap(bool isOpen)
    {
        if (isOpen)
        {
            if (UIPopUpManager.Instance.IsPopUpActive || UIPopUpManager.Instance.IsOnBattle) return;

            _isMapOpen = isOpen;
            UIPopUpManager.Instance.PopUpUI(fullMapUIWindow);
            UIEventBus.NotifyOpen("Map");

            if (UIBasedMiniMap.Instance != null)
            {
                UIBasedMiniMap.Instance.RefreshMap();
            }
        }
        else
        {
            _isMapOpen = isOpen;
            UIPopUpManager.Instance.ClosePopUpUI();
            UIEventBus.NotifyClose("Map");
        }
    }
}
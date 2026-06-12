// ==================== MapUIManager.cs 수정 ====================
using UnityEngine;
using UnityEngine.InputSystem;

public class MapUIManager : MonoBehaviour
{
    [SerializeField] private GameObject fullMapUIWindow;
    [SerializeField] private GameObject panelUIWindow;
    [SerializeField] private MiniMapController miniMapController;
    [SerializeField] private GameObject hudMiniMapToggleObject;

    private PlayerInput _playerInput;
    private bool _isMapOpen = false;
    private bool _isInitialized = false; // 중복 초기화 방지용 변수
    public bool IsMapOpen => _isMapOpen; // 다른 스크립트에서 맵 상태 확인용 프로퍼티

    private void Start()
    {
        if (miniMapController == null)
            miniMapController = Object.FindFirstObjectByType<MiniMapController>();
    }

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
    private void ToggleFullMap(bool isOpen)
    {
        if (isOpen)
        {
            if (UIPopUpManager.Instance.IsPopUpActive || UIPopUpManager.Instance.IsOnBattle) return;

            _isMapOpen = isOpen;
            UIPopUpManager.Instance.PopUpUI(fullMapUIWindow);

            if (miniMapController != null)
            {
                miniMapController.SetMapActive(isOpen);
            }

            // 🌟 전체 지도가 열리면 우측 상단 작은 미니맵은 끕니다.
            if (hudMiniMapToggleObject != null) hudMiniMapToggleObject.SetActive(false);
        }
        else
        {
            _isMapOpen = isOpen;
            UIPopUpManager.Instance.ClosePopUpUI();

            // 전체 지도가 닫히면 우측 상단 작은 미니맵을 다시 켭니다.
            if (hudMiniMapToggleObject != null) hudMiniMapToggleObject.SetActive(true);

            // 지도가 닫힐 때 MiniMapController에게 알림 (다시 플레이어 추적 모드로 돌리기 위함)
            if (miniMapController != null)
            {
                miniMapController.SetMapActive(false);
            }
        }

        if (fullMapUIWindow != null) fullMapUIWindow.SetActive(isOpen);
        if (panelUIWindow != null) panelUIWindow.SetActive(isOpen);
    }
}
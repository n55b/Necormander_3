// ==================== MapUIManager.cs 수정 ====================
using UnityEngine;
using UnityEngine.InputSystem;

public class MapUIManager : MonoBehaviour
{
    [SerializeField] private GameObject fullMapUIWindow;
    [SerializeField] private MiniMapController miniMapController;

    private PlayerInput _playerInput;
    private bool _isMapOpen = false;
    private bool _isInitialized = false; // 중복 초기화 방지용 변수

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

    private void OnMapTogglePressed(InputAction.CallbackContext context)
    {
        _isMapOpen = !_isMapOpen;
        ToggleFullMap(_isMapOpen);
    }

    private void ToggleFullMap(bool isOpen)
    {
        if (fullMapUIWindow != null) fullMapUIWindow.SetActive(isOpen);

        if (miniMapController != null)
        {
            // 🌟 여기서 true를 던지는 순간 위의 FocusOnPlayer()가 발동되어 카메라가 텔레포트합니다!
            miniMapController.SetMapActive(isOpen);
        }
    }
}
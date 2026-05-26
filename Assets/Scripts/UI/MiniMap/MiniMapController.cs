using UnityEngine;
using UnityEngine.InputSystem;

public class MiniMapController : MonoBehaviour
{
    private Camera _miniMapCam;
    private Transform _playerTransform; // 🌟 플레이어 위치를 기억할 변수

    [Header("확대/축소 (Zoom) 설정")]
    [SerializeField] private float zoomSpeed = 5f;
    [SerializeField] private float minZoom = 15f;    // 최대 확대 (숫자가 작을수록 확대)
    [SerializeField] private float maxZoom = 40f;   // 최대 축소 (숫자가 클수록 축소)

    [Header("이동 (Pan) 설정")]
    [SerializeField] private float dragSpeed = 0.5f;

    private PlayerInput _playerInput;
    private InputAction _scrollAction;
    private InputAction _panAction;       // 마우스 드래그 변위량 (Delta)
    private InputAction _panHoldAction;   // 마우스 우클릭을 누르고 있는 상태인지 체크

    private bool _isMappingActive = false; 
    private bool _isInitialized = false;

    private void Awake()
    {
        _miniMapCam = GetComponent<Camera>();
        _miniMapCam.orthographicSize = minZoom; // 초기에는 확대 상태로 시작
    }

    private void Update()
    {
        // 매니저들과 동일한 구조의 안전한 런타임 자동 할당 프로세스
        if (!_isInitialized || _playerTransform == null)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsPlayerReady)
            {
                InitializeMiniMapInput();
            }
            return;
        }

        // 지도 UI가 켜져 있고, 인풋 시스템이 정상 작동할 때만 조작 처리
        if (!_isMappingActive || _playerInput == null) return;

        HandleZoomNew();
        HandlePanNew();
    }

    private void InitializeMiniMapInput()
    {
        if (GameManager.Instance.PLAYERCONTROLLER != null)
        {
            _playerTransform = GameManager.Instance.PLAYERCONTROLLER.transform;
            _playerInput = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerInput>();

            if (_playerInput != null && _playerInput.actions != null)
            {
                // 뉴 인풋 시스템 액션 바인딩
                _scrollAction = _playerInput.actions.FindAction("ScrollWheel") ?? _playerInput.actions.FindAction("Look"); 
                _panAction = _playerInput.actions.FindAction("PanDelta") ?? _playerInput.actions.FindAction("Look");
                _panHoldAction = _playerInput.actions.FindAction("PanHold") ?? _playerInput.actions.FindAction("RightClick");

                _isInitialized = true;
                Debug.Log("<color=green>[MiniMapController]</color> 신형 인풋 조작 시스템 바인딩 완료!");
            }
        }
    }

    /// <summary>
    /// 외부(MapUIManager)에서 지도창을 켜고 끌 때 호출됩니다.
    /// </summary>
    public void SetMapActive(bool isActive)
    {
        _isMappingActive = isActive;

        // 🌟 [핵심 추가] 미니맵이 딱 켜지는 순간, 카메라 위치를 플레이어의 위치로 강제 동기화합니다.
        if (isActive && _playerTransform != null)
        {
            FocusOnPlayer();
        }
    }

    // 🌟 플레이어 중심으로 미니맵 카메라를 강제 리셋하는 함수
    public void FocusOnPlayer()
    {
        if (_playerTransform == null) return;
        
        // 카메라의 X, Y는 플레이어와 일치시키고, Z축은 카메라 고유값(-10 등)을 유지합니다.
        Vector3 targetPos = _playerTransform.position;
        targetPos.z = transform.position.z; 
        
        transform.position = targetPos;
        Debug.Log("<color=cyan>[MiniMapController]</color> 미니맵 카메라가 플레이어 중심으로 정렬되었습니다.");
    }

    // 신형 인풋 기반 확대 / 축소 (Zoom)
    private void HandleZoomNew()
    {
        if (_scrollAction == null) return;

        Vector2 scrollValue = _scrollAction.ReadValue<Vector2>();
        if (Mathf.Abs(scrollValue.y) > 0.01f)
        {
            float targetZoom = _miniMapCam.orthographicSize - (scrollValue.y * 0.001f * zoomSpeed);
            _miniMapCam.orthographicSize = Mathf.Clamp(targetZoom, minZoom, maxZoom);
        }
    }

    // 신형 인풋 기반 지도 드래그 이동 (Pan) - 이제 마우스 우클릭 드래그로 작동합니다!
    private void HandlePanNew()
    {
        if (_panAction == null || _panHoldAction == null) return;

        // 마우스 우클릭(또는 설정된 키)이 유지되고 있는지 체크
        bool isHolding = _panHoldAction.ReadValue<float>() > 0.5f;

        if (isHolding)
        {
            Vector2 mouseDelta = _panAction.ReadValue<Vector2>();

            if (mouseDelta.sqrMagnitude > 0.01f)
            {
                float currentCamSize = _miniMapCam.orthographicSize;
                // 우클릭 드래그 방향과 카메라 이동 방향 매칭
                Vector3 moveDirection = new Vector3(-mouseDelta.x, -mouseDelta.y, 0) * dragSpeed * 0.005f * currentCamSize;
                
                transform.Translate(moveDirection, Space.World);
            }
        }
    }
}
using UnityEngine;
using UnityEngine.InputSystem;

public class MiniMapController : MonoBehaviour
{
    private Camera _miniMapCam;
    private Transform _playerTransform; 

    [Header("확대/축소 (Zoom) 설정")]
    [SerializeField] private float zoomSpeed = 5f;
    [SerializeField] private float minZoom = 15f;    // 최대 확대 (전체지도 기준)
    [SerializeField] private float maxZoom = 40f;   // 최대 축소 (전체지도 기준)
    [SerializeField] private float hudZoom = 8f;     // 평소 우측 상단 미니맵 줌 치수

    [Header("이동 (Pan) 설정")]
    [SerializeField] private float dragSpeed = 0.5f;

    private PlayerInput _playerInput;
    private InputAction _scrollAction;
    private InputAction _panAction;       
    private InputAction _panHoldAction;   

    private bool _isMappingActive = false; 
    private bool _isInitialized = false;

    private void Awake()
    {
        _miniMapCam = GetComponent<Camera>();
        // 초기 시작 시에는 작은 미니맵 배율로 시작합니다.
        _miniMapCam.orthographicSize = hudZoom; 
    }

    private void Update()
    {
        if (!_isInitialized || _playerTransform == null)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsPlayerReady)
            {
                InitializeMiniMapInput();
            }
            return;
        }

        // 🌟 지도가 꺼져 있을 때는 플레이어를 바짝 추적하고 Update를 빠져나갑니다.
        if (!_isMappingActive)
        {
            FollowPlayerSmooth();
            return; 
        }

        // 지도 UI가 켜져 있고, 인풋 시스템이 정상 작동할 때만 조작 처리
        if (_playerInput == null) return;

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

        if (isActive)
        {
            // 🌟 [핵심 수정] 큰 지도가 딱 켜지는 순간!
            // 1. 카메라 배율을 전체 지도 기본 배율(minZoom 등)로 즉시 강제 변경합니다.
            if (_miniMapCam != null)
            {
                _miniMapCam.orthographicSize = minZoom; 
            }

            // 2. 카메라 위치를 플레이어 위치로 즉시 동기화합니다.
            if (_playerTransform != null)
            {
                FocusOnPlayer();
            }
        }
    }

    public void FocusOnPlayer()
    {
        if (_playerTransform == null) return;
        
        // 플레이어의 실제 월드 위치를 160 Spacing으로 나누어 현재 논리 그리드(방) 위치를 계산
        float actualX = _playerTransform.position.x;
        float actualY = _playerTransform.position.y;
        
        int gridX = Mathf.RoundToInt(actualX / 160f);
        int gridY = Mathf.RoundToInt(actualY / 160f);

        // 독립 격리 공간(-1000, -1000) 상의 촘촘한 미니맵 대응 방 중심 좌표 산출
        int minimapSpacing = 12;
        int minimapOffsetX = -1000;
        int minimapOffsetY = -1000;

        Vector3 targetPos = new Vector3(
            minimapOffsetX + gridX * minimapSpacing,
            minimapOffsetY + gridY * minimapSpacing,
            transform.position.z
        );
        
        transform.position = targetPos;
        Debug.Log("<color=cyan>[MiniMapController]</color> 미니맵 카메라가 격리 가상 좌표계의 방 중심으로 정렬되었습니다.");
    }

    private void FollowPlayerSmooth()
    {
        if (_playerTransform == null) return;

        // 매 프레임 플레이어 위치를 직접 졸졸 따라가지 않고, 플레이어가 서 있는 방의 격리 가상 중심에 고정시킵니다.
        float actualX = _playerTransform.position.x;
        float actualY = _playerTransform.position.y;
        
        int gridX = Mathf.RoundToInt(actualX / 160f);
        int gridY = Mathf.RoundToInt(actualY / 160f);

        int minimapSpacing = 12;
        int minimapOffsetX = -1000;
        int minimapOffsetY = -1000;

        Vector3 targetPos = new Vector3(
            minimapOffsetX + gridX * minimapSpacing,
            minimapOffsetY + gridY * minimapSpacing,
            transform.position.z
        );

        transform.position = targetPos; 

        if (_miniMapCam != null)
        {
            _miniMapCam.orthographicSize = hudZoom;
        }
    }

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

    private void HandlePanNew()
    {
        if (_panAction == null || _panHoldAction == null) return;

        bool isHolding = _panHoldAction.ReadValue<float>() > 0.5f;

        if (isHolding)
        {
            Vector2 mouseDelta = _panAction.ReadValue<Vector2>();

            if (mouseDelta.sqrMagnitude > 0.01f)
            {
                float currentCamSize = _miniMapCam.orthographicSize;
                Vector3 moveDirection = new Vector3(-mouseDelta.x, -mouseDelta.y, 0) * dragSpeed * 0.005f * currentCamSize;
                
                transform.Translate(moveDirection, Space.World);
            }
        }
    }
}
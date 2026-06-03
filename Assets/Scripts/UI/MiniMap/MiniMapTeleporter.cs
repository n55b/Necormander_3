using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

public class MiniMapTeleporter : MonoBehaviour
{
    [Header("카메라 및 플레이어 설정 (자동 할당)")]
    [SerializeField] private Camera miniMapCamera;      
    [SerializeField] private Transform playerTransform; 
    [SerializeField] private LayerMask roomLayer;       

    [Header("테두리 하이라이트 설정")]
    [SerializeField] private Sprite highlightBorderSprite;
    [SerializeField] private Color highlightColor = new Color(0f, 1f, 0.5f, 1f); // 테두리 색상 (초록/형광 등)
    [SerializeField] private float borderPadding = 0.5f; // 방 크기보다 약간 더 크게 테두리를 칠할 여유 공간

    [Header("MiniMapTeleporter")]
    [SerializeField] private MapUIManager mapUIManager; // 맵 UI 매니저 참조 (맵이 열려있는지 확인용)

    private PlayerInput _playerInput;
    private RectTransform _rectTransform;
    private Canvas _canvas;
    private Camera _canvasCamera;
    private bool _isInitialized = false;

    // 런타임에 관리할 테두리 게임 오브젝트 및 렌더러
    private GameObject _borderObject;
    private SpriteRenderer _borderRenderer;
    private RoomInstance _lastHoveredRoom; 

    private void Start()
    {
        _rectTransform = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        _canvasCamera = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? _canvas.worldCamera : null;

        if (roomLayer == 0) roomLayer = LayerMask.GetMask("Room");

        // 하이라이트 테두리 오브젝트 동적 생성
        CreateHighlightBorderObject();
    }

    private void Update()
    {
        if (!_isInitialized || playerTransform == null || _playerInput == null || miniMapCamera == null)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsPlayerReady)
            {
                InitializeTeleporter();
            }
            return;
        }

        // 매 프레임 마우스가 위치한 방을 검사해 테두리를 제어합니다.
        HandleMouseHover();
    }

    private void OnDestroy()
    {
        CleanUpInputEvent();
    }

    private void InitializeTeleporter()
    {
        CleanUpInputEvent();

        var controller = Object.FindFirstObjectByType<MiniMapController>();
        if (controller != null)
        {
            miniMapCamera = controller.GetComponent<Camera>();
        }

        _rectTransform = GetComponent<RectTransform>();

        if (GameManager.Instance.PLAYERCONTROLLER != null)
        {
            playerTransform = GameManager.Instance.PLAYERCONTROLLER.transform;
            _playerInput = GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerInput>();

            if (_playerInput != null && _playerInput.actions != null)
            {
                var mapClickAction = _playerInput.actions["MapClick"];
                if (mapClickAction != null)
                {
                    mapClickAction.performed -= OnMapClickPressed;
                    mapClickAction.performed += OnMapClickPressed;
                    
                    _isInitialized = true;
                    Debug.Log("<color=green>[MiniMapTeleporter]</color> 인풋 및 하이라이트 시스템 준비 완료!");
                }
            }
        }
    }

    private void CleanUpInputEvent()
    {
        if (_playerInput != null && _playerInput.actions != null)
        {
            var mapClickAction = _playerInput.actions["MapClick"];
            if (mapClickAction != null)
            {
                mapClickAction.performed -= OnMapClickPressed;
            }
        }
        _isInitialized = false;
    }

    private bool TryGetMiniMapWorldPoint(Vector2 screenPosition, out Vector2 worldPoint2D)
    {
        worldPoint2D = Vector2.zero;
        if (_rectTransform == null || miniMapCamera == null) return false;

        if (!RectTransformUtility.RectangleContainsScreenPoint(_rectTransform, screenPosition, _canvasCamera))
            return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rectTransform, screenPosition, _canvasCamera, out var localPoint))
            return false;

        Vector2 normalizedPoint = new Vector2(
            (localPoint.x - _rectTransform.rect.x) / _rectTransform.rect.width,
            (localPoint.y - _rectTransform.rect.y) / _rectTransform.rect.height
        );

        if (miniMapCamera.targetTexture != null)
        {
            Vector3 worldPoint = miniMapCamera.ViewportToWorldPoint(new Vector3(normalizedPoint.x, normalizedPoint.y, miniMapCamera.nearClipPlane));
            worldPoint2D = new Vector2(worldPoint.x, worldPoint.y);
            return true;
        }

        Rect camRect = miniMapCamera.pixelRect;
        Vector2 cameraScreenPoint = new Vector2(
            camRect.x + normalizedPoint.x * camRect.width,
            camRect.y + normalizedPoint.y * camRect.height
        );

        Vector3 worldPoint2 = miniMapCamera.ScreenToWorldPoint(new Vector3(cameraScreenPoint.x, cameraScreenPoint.y, miniMapCamera.nearClipPlane));
        worldPoint2D = new Vector2(worldPoint2.x, worldPoint2.y);
        return true;
    }

    /// <summary>
    /// 하이라이트용 유령 테두리 오브젝트를 하이어라키에 풀링용으로 생성합니다.
    /// </summary>
    private void CreateHighlightBorderObject()
    {
        if (_borderObject != null) return;

        _borderObject = new GameObject("MiniMap_HoverBorder");
        _borderRenderer = _borderObject.AddComponent<SpriteRenderer>();

        // 중요: 미니맵 카메라만 볼 수 있도록 레이어를 강제 지정합니다.
        int miniMapLayer = LayerMask.NameToLayer("MiniMap");
        if (miniMapLayer != -1) _borderObject.layer = miniMapLayer;

        // 크기가 유연하게 늘어나도록 슬라이싱 모드 채택 (9-Slice 설정 필요)
        _borderRenderer.sprite = highlightBorderSprite;
        _borderRenderer.drawMode = SpriteDrawMode.Sliced;
        _borderRenderer.color = highlightColor;
        _borderRenderer.sortingOrder = 100; // 타일맵보다 위에 렌더링되도록 높게 설정

        _borderObject.SetActive(false); // 평소에는 꺼둡니다.
    }

    private void HandleMouseHover()
    {
        if (Pointer.current == null || _borderObject == null) return;
        Vector2 screenPosition = Pointer.current.position.ReadValue();

        RoomInstance currentRoom = null;
        if (TryGetMiniMapWorldPoint(screenPosition, out Vector2 worldPoint2D))
        {
            Collider2D hitCollider = Physics2D.OverlapPoint(worldPoint2D, roomLayer);

            if (hitCollider != null)
            {
                RoomInstance room = hitCollider.GetComponent<RoomInstance>();
                
                if (room != null && IsRoomRevealed(room))
                {
                    currentRoom = room;
                }
            }
        }

        // 🌟 마우스가 가리키는 방이 바뀌었을 때만 테두리 트랜스폼 및 활성화 조절
        if (currentRoom != _lastHoveredRoom)
        {
            if (currentRoom != null)
            {
                // 테두리 오브젝트 위치 조정 (방 위치 + 오프셋)
                Vector3 targetPos = currentRoom.transform.position + (Vector3)currentRoom.centerOffset;
                targetPos.z = _borderObject.transform.position.z; // 기존 Z값 유지
                _borderObject.transform.position = targetPos;

                // 테두리 오브젝트 크기 조정 (방 크기 + 여유 패딩 적용)
                // Sliced 모드이므로 size 변수를 통해 가로세로 길이를 정밀하게 맞춰줍니다.
                _borderRenderer.size = new Vector2(currentRoom.roomSize.x + borderPadding, currentRoom.roomSize.y + borderPadding);

                // 인스펙터에서 실시간으로 바꾼 속성 반영
                _borderRenderer.sprite = highlightBorderSprite;
                _borderRenderer.color = highlightColor;

                _borderObject.SetActive(true);
            }
            else
            {
                // 공허한 곳에 마우스가 있으면 테두리를 숨깁니다.
                _borderObject.SetActive(false);
            }

            _lastHoveredRoom = currentRoom;
        }
    }

    private void OnMapClickPressed(InputAction.CallbackContext context)
    {
        if (this == null || !_isInitialized || miniMapCamera == null || playerTransform == null || 
        mapUIManager == null) return;

        if (!gameObject.activeInHierarchy) return;

        if (Pointer.current == null) return;
        Vector2 screenPosition = Pointer.current.position.ReadValue();

        if (TryGetMiniMapWorldPoint(screenPosition, out Vector2 worldPoint2D))
        {
            Collider2D hitCollider = Physics2D.OverlapPoint(worldPoint2D, roomLayer);

            if (hitCollider != null)
            {
                RoomInstance clickedRoom = hitCollider.GetComponent<RoomInstance>();
                if (clickedRoom != null)
                {
                    TeleportToRoomCenter(clickedRoom);
                }
            }
        }
    }

    private void TeleportToRoomCenter(RoomInstance room)
    {
        if(mapUIManager.IsMapOpen == false || GameManager.Instance.PLAYERCONTROLLER.GetPlayerState() == PlayerStates.Battle) return;
        if (!IsRoomRevealed(room))
        {
            Debug.LogWarning($"<color=orange>[Teleport]</color> 아직 안 가본 방으로는 갈 수 없습니다.");
            return;
        }

        Vector3 targetPos = room.transform.position + (Vector3)room.centerOffset;
        targetPos.z = playerTransform.position.z; 

        playerTransform.position = targetPos;
        Debug.Log($"<color=green>[Teleport]</color> {room.gameObject.name}의 중심으로 순간이동했습니다.");
    }

    private bool IsRoomRevealed(RoomInstance room)
    {
        if (room == null) return false;
        if (room.roomType == RoomType.Spawn || room.isCleared || room.hasBeenVisited) return true;

        if (MapGenerator.Instance == null || MapGenerator.Instance.FogTilemap == null) return false;

        Vector3 fixedCenter = room.transform.position + (Vector3)room.centerOffset;
        Vector3Int centerCell = MapGenerator.Instance.FogTilemap.WorldToCell(fixedCenter);
        return !MapGenerator.Instance.FogTilemap.HasTile(centerCell);
    }
}
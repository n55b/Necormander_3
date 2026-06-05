using UnityEngine;

/// <summary>
/// 카메라가 실제로 추적할 타겟 오브젝트를 제어합니다.
/// 플레이어를 따라다니되, 조준 중일 때는 마우스 방향으로 일정 범위(박스) 내에서 이동합니다.
/// </summary>
public class CameraTargetController : MonoBehaviour
{
    public static CameraTargetController Instance;

    [Header("Follow Settings")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float smoothTime = 0.2f;

    [Header("Aiming Settings")]
    [SerializeField] private bool isAiming = false;
    [Range(0f, 1f)]
    [SerializeField] private float leanWeight = 0.5f; // 플레이어와 마우스 사이의 거리 중 얼마나 갈지 (0.5 = 중간)
    
    [Header("Box Bounds (Clamp)")]
    [SerializeField] private Vector2 maxOffset = new Vector2(5f, 3f); // 플레이어로부터의 최대 가로/세로 거리

    private Vector3 _currentVelocity;
    private Vector3 _targetOffset;

    public void SetAiming(bool aiming)
    {
        isAiming = aiming;
        if (!isAiming) _targetOffset = Vector3.zero;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (playerTransform == null && GameManager.Instance != null)
        {
            // 플레이어 자동 할당 시도
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }

        if (playerTransform != null)
        {
            transform.position = playerTransform.position;
        }
    }

    private void LateUpdate()
    {
        if (playerTransform == null) return;

        Vector3 targetLocalPos;

        if (isAiming)
        {
            // 1. 마우스 월드 좌표 가져오기
            Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = 0f;

            // 2. 부모(플레이어) 로컬 공간 기준으로 마우스의 위치(상대적 거리) 계산
            Vector3 localMousePos = playerTransform.InverseTransformPoint(mouseWorldPos);
            
            // 3. 가중치 적용 및 박스 범위 제한 (Clamp)
            float targetX = Mathf.Clamp(localMousePos.x * leanWeight, -maxOffset.x, maxOffset.x);
            float targetY = Mathf.Clamp(localMousePos.y * leanWeight, -maxOffset.y, maxOffset.y);
            
            targetLocalPos = new Vector3(targetX, targetY, 0f);
        }
        else
        {
            // 조준 중이 아닐 때는 플레이어 정중앙(0,0,0)으로 복귀
            targetLocalPos = Vector3.zero;
        }

        // 4. 부모 안에서 부드럽게 로컬 위치 이동
        transform.localPosition = Vector3.SmoothDamp(transform.localPosition, targetLocalPos, ref _currentVelocity, smoothTime);
    }

    private void OnDrawGizmos()
    {
        // 자식 오브젝트인 경우 부모 위치를 기준으로 박스 표시
        Gizmos.color = Color.yellow;
        Vector3 center = (playerTransform != null) ? playerTransform.position : transform.parent != null ? transform.parent.position : transform.position;
        Vector3 size = new Vector3(maxOffset.x * 2, maxOffset.y * 2, 0.1f);
        Gizmos.DrawWireCube(center, size);
    }
}

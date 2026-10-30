using UnityEngine;

/// <summary>
/// 방 프리팹에서 문이 생성될 위치와 통로가 뻗어나갈 방향을 정의합니다.
/// </summary>
public class RoomAnchor : MonoBehaviour
{
    [Tooltip("통로가 뻗어나가는 방향 (예: 북쪽 벽이면 0,1 / 남쪽 벽이면 0,-1)")]
    public Vector2Int direction = Vector2Int.up;

    [InspectorName("문을 방 안쪽으로 당길 거리")]
    [Tooltip("벽 구멍과 방 연결 기준은 유지하고, 생성되는 문 오브젝트만 안쪽으로 옮깁니다. 월드 단위입니다.")]
    [Min(0f)] public float doorInset;
    
    [HideInInspector] public bool isUsed = false;

    private void OnDrawGizmos()
    {
        // 에디터 뷰에서 앵커 위치와 방향을 시각적으로 확인
        Gizmos.color = isUsed ? Color.red : Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.3f);
        
        Vector3 dir = new Vector3(direction.x, direction.y, 0);
        Gizmos.DrawRay(transform.position, dir * 1.5f);
        
        // 방향 화살표 끝부분 표시
        Gizmos.DrawWireCube(transform.position + dir * 1.5f, Vector3.one * 0.2f);
    }
}

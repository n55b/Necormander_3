using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

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

    internal const float ArrivalDistance = 2.5f;

    private int InwardSteps(Tilemap map)
    {
        var at = map.WorldToCell(transform.position);
        float step = Vector3.Distance(map.GetCellCenterWorld(at), map.GetCellCenterWorld(at + (Vector3Int)direction));
        return Mathf.CeilToInt(ArrivalDistance / step - .0001f);
    }

    internal Vector3 ArrivalPoint(Tilemap map) =>
        map.GetCellCenterWorld(map.WorldToCell(transform.position) - (Vector3Int)direction * InwardSteps(map));

    internal IEnumerable<Vector3Int> PassageCells(Tilemap map, bool includeOuterStep)
    {
        var mouth = map.WorldToCell(transform.position);
        var side = new Vector3Int(-direction.y, direction.x);
        int depth = InwardSteps(map);
        for (int inward = includeOuterStep ? -1 : 0; inward <= depth; inward++)
            for (int offset = -1; offset <= 1; offset++)
                yield return mouth - (Vector3Int)direction * inward + side * offset;
    }

    internal void OpenPassage(Tilemap ground, Tilemap wall, Tilemap shadow, Tilemap blocked,
        TileBase floorTile, System.Action<Tilemap, Vector3Int> changed = null)
    {
        void Write(Tilemap map, Vector3Int cell, TileBase tile)
        {
            if (map == null || map.GetTile(cell) == tile) return;
            map.SetTile(cell, tile);
            if (map == ground || map == wall) WorkshopTerrainTile.RefreshNeighbors(map, cell);
            changed?.Invoke(map, cell);
        }
        foreach (var cell in PassageCells(wall, false))
        {
            if (wall.HasTile(cell))
            {
                Write(wall, cell, null);
                Write(shadow, cell - (Vector3Int)direction, null);
            }
            Write(blocked, cell, null);
            if (!ground.HasTile(cell)) Write(ground, cell, floorTile);
        }
    }

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

using UnityEngine;
using UnityEngine.Tilemaps;

public class FogDisolver : MonoBehaviour
{
    [Header("복도 시야 반경 (칸수)")]
    [SerializeField] private int viewRadius = 4; // 플레이어 주변 몇 칸까지 실시간으로 지울 것인가

    private Tilemap _fogTilemap;
    private Vector3Int _lastPlayerCell;

    private void Start()
    {
        if (MapGenerator.Instance != null)
            _fogTilemap = MapGenerator.Instance.FogTilemap;
    }

    private void Update()
    {
        if (_fogTilemap == null) return;

        // 플레이어의 현재 격자 좌표 구하기
        Vector3Int currentCell = _fogTilemap.WorldToCell(transform.position);

        // 한 칸 움직였을 때만 연산 처리 (최적화)
        if (currentCell != _lastPlayerCell)
        {
            _lastPlayerCell = currentCell;
            RevealFogCircle(currentCell);
        }
    }

    private void RevealFogCircle(Vector3Int playerCell)
    {
        for (int x = -viewRadius; x <= viewRadius; x++)
        {
            for (int y = -viewRadius; y <= viewRadius; y++)
            {
                // 원형 범위 공식 (피타고라스)
                if (x * x + y * y <= viewRadius * viewRadius)
                {
                    Vector3Int targetCell = new Vector3Int(playerCell.x + x, playerCell.y + y, 0);
                    if (_fogTilemap.HasTile(targetCell))
                    {
                        _fogTilemap.SetTile(targetCell, null); // 복도 길 밝히기
                    }
                }
            }
        }
    }
}
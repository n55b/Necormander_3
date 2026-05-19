using UnityEngine;
using UnityEngine.Tilemaps;

public class RoomInstance : MonoBehaviour
{
    public RoomType roomType;
    public Vector2Int roomSize;
    public Vector2 centerOffset;
    
    [HideInInspector] public Tilemap wallTilemap;
    [HideInInspector] public Tilemap groundTilemap;
    
    private Rigidbody2D _rb;
    private BoxCollider2D _collider;
    
    public void Initialize(RoomType type)
    {
        roomType = type;
        
        Transform wallTransform = transform.Find("Wall");
        if (wallTransform != null) 
        {
            wallTilemap = wallTransform.GetComponent<Tilemap>();
            wallTransform.localPosition = Vector3.zero;
            var childCols = wallTransform.GetComponentsInChildren<Collider2D>();
            foreach (var ccol in childCols) { ccol.enabled = false; Destroy(ccol); }
            var childRbs = wallTransform.GetComponentsInChildren<Rigidbody2D>();
            foreach (var crb in childRbs) { crb.simulated = false; Destroy(crb); }
        }
        
        Transform groundTransform = transform.Find("Ground");
        if (groundTransform != null) 
        {
            groundTilemap = groundTransform.GetComponent<Tilemap>();
            groundTransform.localPosition = Vector3.zero;
        }

        Tilemap mainTM = wallTilemap != null ? wallTilemap : groundTilemap;
        if (mainTM != null)
        {
            mainTM.CompressBounds();
            centerOffset = mainTM.localBounds.center;
            roomSize = new Vector2Int(Mathf.CeilToInt(mainTM.localBounds.size.x), Mathf.CeilToInt(mainTM.localBounds.size.y));
        }

        _rb = gameObject.AddComponent<Rigidbody2D>();
        _rb.gravityScale = 0;
        _rb.interpolation = RigidbodyInterpolation2D.None;
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        
        PhysicsMaterial2D mat = new PhysicsMaterial2D("Slippery");
        mat.friction = 0f;
        mat.bounciness = 0f;
        _rb.sharedMaterial = mat;

        _collider = gameObject.AddComponent<BoxCollider2D>();
        
        // [수정] 방 간의 최소 거리를 확보하기 위해 콜라이더 크기에 패딩(+3) 추가
        _collider.size = new Vector2(roomSize.x + 3.0f, roomSize.y + 3.0f); 
        _collider.offset = centerOffset;
        _collider.sharedMaterial = mat;
        
        gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); 
    }

    public void MergeTilesToGlobal(Tilemap globalGround, Tilemap globalWall)
    {
        StampTilemap(groundTilemap, globalGround);
        StampTilemap(wallTilemap, globalWall);
        if (groundTilemap != null) groundTilemap.gameObject.SetActive(false);
        if (wallTilemap != null) wallTilemap.gameObject.SetActive(false);
    }

    private void StampTilemap(Tilemap source, Tilemap target)
    {
        if (source == null || target == null) return;
        source.CompressBounds();
        BoundsInt bounds = source.cellBounds;
        foreach (var pos in bounds.allPositionsWithin)
        {
            TileBase tile = source.GetTile(pos);
            if (tile != null)
            {
                Vector3 worldPos = source.CellToWorld(pos);
                Vector3Int targetCellPos = target.WorldToCell(worldPos);
                target.SetTile(targetCellPos, tile);
            }
        }
    }

    public void SnapToGrid(float unit)
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Round(pos.x / unit) * unit;
        pos.y = Mathf.Round(pos.y / unit) * unit;
        transform.position = pos;
    }

    public void CleanupPhysics()
    {
        if (_collider != null) _collider.enabled = false;
        if (_rb != null) Destroy(_rb);
        if (_collider != null) Destroy(_collider);
    }
}

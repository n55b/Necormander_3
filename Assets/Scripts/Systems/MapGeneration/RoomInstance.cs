using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class RoomInstance : MonoBehaviour
{
    public RoomType roomType;
    public Vector2Int roomSize;
    public Vector2 centerOffset;
    
    [Header("Anchors")]
    public List<RoomAnchor> anchors = new List<RoomAnchor>();
    
    [HideInInspector] public Tilemap wallTilemap;
    [HideInInspector] public Tilemap groundTilemap;
    [HideInInspector] public Tilemap shadowTilemap;
    
    private Rigidbody2D _rb;
    private BoxCollider2D _collider;
    
    public void Initialize(RoomType type)
    {
        roomType = type;
        
        // 앵커 수집
        anchors.Clear();
        anchors.AddRange(GetComponentsInChildren<RoomAnchor>());

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

        Transform shadowTransform = transform.Find("Shadow");
        if (shadowTransform != null)
        {
            shadowTilemap = shadowTransform.GetComponent<Tilemap>();
            shadowTransform.localPosition = Vector3.zero;
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
        
        // [수정] 방 간의 겹침을 확실히 방지하고 통로 생성 여유 공간을 넓게 확보하기 위한 패딩 조절
        // 사용자 요청: 방들보다 콜라이더가 확실히 크도록 변경 (기본 3.0f -> 9.0f 로 상향)
        float colliderPadding = 9.0f; 
        _collider.size = new Vector2(roomSize.x + colliderPadding, roomSize.y + colliderPadding); 
        _collider.offset = centerOffset;
        _collider.sharedMaterial = mat;
        
        gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); 
    }

    public void MergeTilesToGlobal(Tilemap globalGround, Tilemap globalWall, Tilemap globalShadow)
    {
        StampTilemap(groundTilemap, globalGround);
        StampTilemap(wallTilemap, globalWall);
        StampTilemap(shadowTilemap, globalShadow);
        if (groundTilemap != null) groundTilemap.gameObject.SetActive(false);
        if (wallTilemap != null) wallTilemap.gameObject.SetActive(false);
        if (shadowTilemap != null) shadowTilemap.gameObject.SetActive(false);
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

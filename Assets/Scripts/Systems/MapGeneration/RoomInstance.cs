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

    [Header("Tilemaps (Optional - Auto-assigned if Null)")]
    public Tilemap wallTilemap;
    public Tilemap groundTilemap;
    public Tilemap shadowTilemap;
    public Tilemap unsteppableTilemap;

    [HideInInspector] public int debugDepth = -1; // 맵 생성 시 계산된 깊이 저장용
    [HideInInspector] public int phaseIndex = -1; // 방이 생성된 맵 생성 페이즈 인덱스
    [HideInInspector] public Vector2Int gridPosition = Vector2Int.zero; // [추가] 아이작 스타일 가상 그리드 좌표

    public float GetDiameter()
    {
        return Mathf.Max(roomSize.x, roomSize.y);
    }

    [Header("Combat & Events")]
    public bool isCleared = false;
    public bool hasBeenVisited = false;
    public List<GameObject> doorObjects = new List<GameObject>(); // MapGenerator에서 할당
    [SerializeField] private AudioClip roomBGM; // [추가] 이 방에서 나올 음악

    // [추가] 방에 처음 들어갈 때마다 호출되는 전역 이벤트 (매니저들에서 방 리셋용으로 사용)
    public static System.Action<RoomInstance> OnPlayerEnteredRoom;

    private IRoomEvent _roomEvent;
    private Rigidbody2D _rb;
    private BoxCollider2D _physicsCollider;
    private BoxCollider2D _triggerCollider;

    // ❌ 기존 사각형 스프라이트 안개용 필드들 완전 삭제

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (debugDepth >= 0)
        {
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.yellow;
            style.fontSize = 20;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            UnityEditor.Handles.Label(transform.position, $"[{roomType}]\nDepth: {debugDepth}\nGrid: {gridPosition}", style);
        }
    }
#endif

    public void Initialize(RoomType type)
    {
        doorObjects.Clear();
        roomType = type;
        _roomEvent = GetComponent<IRoomEvent>();

        anchors.Clear();
        anchors.AddRange(GetComponentsInChildren<RoomAnchor>());

        if (wallTilemap == null)
        {
            Transform wallTransform = FindTransformRecursive(transform, "Wall");
            if (wallTransform != null) wallTilemap = wallTransform.GetComponent<Tilemap>();
        }

        if (wallTilemap != null)
        {
            Transform wallTransform = wallTilemap.transform;
            var childCols = wallTransform.GetComponentsInChildren<Collider2D>();
            foreach (var ccol in childCols) { ccol.enabled = false; MapGenerator.SafeDestroy(ccol); }
            var childRbs = wallTransform.GetComponentsInChildren<Rigidbody2D>();
            foreach (var crb in childRbs) { crb.simulated = false; MapGenerator.SafeDestroy(crb); }
        }

        if (groundTilemap == null)
        {
            Transform groundTransform = FindTransformRecursive(transform, "Ground");
            if (groundTransform != null) groundTilemap = groundTransform.GetComponent<Tilemap>();
        }

        if (shadowTilemap == null)
        {
            Transform shadowTransform = FindTransformRecursive(transform, "Shadow");
            if (shadowTransform != null) shadowTilemap = shadowTransform.GetComponent<Tilemap>();
        }

        if (unsteppableTilemap == null)
        {
            Transform unsteppableTransform = FindTransformRecursive(transform, "Unsteppable");
            if (unsteppableTransform != null) unsteppableTilemap = unsteppableTransform.GetComponent<Tilemap>();
        }

        if (unsteppableTilemap != null)
        {
            Transform unsteppableTransform = unsteppableTilemap.transform;
            var childCols = unsteppableTransform.GetComponentsInChildren<Collider2D>();
            foreach (var ccol in childCols) { ccol.enabled = false; MapGenerator.SafeDestroy(ccol); }
            var childRbs = unsteppableTransform.GetComponentsInChildren<Rigidbody2D>();
            foreach (var crb in childRbs) { crb.simulated = false; MapGenerator.SafeDestroy(crb); }
        }

        Tilemap mainTM = wallTilemap != null ? wallTilemap : groundTilemap;
        if (mainTM != null)
        {
            mainTM.CompressBounds();
            Vector2 localPos = Vector2.zero;
            Transform curr = mainTM.transform;
            while (curr != null && curr != transform)
            {
                localPos += (Vector2)curr.localPosition;
                curr = curr.parent;
            }
            centerOffset = (Vector2)mainTM.localBounds.center + localPos;
            roomSize = new Vector2Int(Mathf.CeilToInt(mainTM.localBounds.size.x), Mathf.CeilToInt(mainTM.localBounds.size.y));
        }

        _rb = gameObject.AddComponent<Rigidbody2D>();
        _rb.gravityScale = 0;
        _rb.interpolation = RigidbodyInterpolation2D.None;
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        PhysicsMaterial2D mat = new PhysicsMaterial2D("Slippery") { friction = 0f, bounciness = 0f };
        _rb.sharedMaterial = mat;

        _physicsCollider = gameObject.AddComponent<BoxCollider2D>();
        float colliderPadding = 4.0f;
        _physicsCollider.size = new Vector2(roomSize.x + colliderPadding, roomSize.y + colliderPadding);
        _physicsCollider.offset = centerOffset;
        _physicsCollider.sharedMaterial = mat;

        _triggerCollider = gameObject.AddComponent<BoxCollider2D>();
        _triggerCollider.isTrigger = true;
        _triggerCollider.size = new Vector2(Mathf.Max(1, roomSize.x - 4f), Mathf.Max(1, roomSize.y - 4f));
        _triggerCollider.offset = centerOffset;
    }

    public void ForceEnter()
    {
        hasBeenVisited = true;
        RevealRoom(); // 타일 안개 제거 호출

        // [추가] 강제 진입 시 진입한 방을 촘촘한 미니맵에 실시간으로 그려서 개방
        if (MapGenerator.Instance != null)
        {
            MapGenerator.Instance.DrawRoomOnMinimap(this);
        }

        if (roomBGM != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.ChangeBGM(roomBGM);
        }
        _roomEvent?.OnPlayerEnter(this);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            hasBeenVisited = true;
            RevealRoom(); // 플레이어가 방에 들어가면 해당 방의 안개 타일을 한 번에 지웁니다.
            
            // [추가] 플레이어가 방 진입 시 촘촘한 미니맵에 그려서 실시간으로 미니맵 안개를 걷음
            if (MapGenerator.Instance != null)
            {
                MapGenerator.Instance.DrawRoomOnMinimap(this);
            }

            // [추가] 방 입장 전역 이벤트 발생
            OnPlayerEnteredRoom?.Invoke(this);

            if (isCleared || roomType == RoomType.Spawn) return;

            if (roomBGM != null && SoundManager.Instance != null)
            {
                SoundManager.Instance.ChangeBGM(roomBGM);
            }

            Debug.Log($"<color=yellow>[Room]</color> Player Entered: {gameObject.name}");
            _roomEvent?.OnPlayerEnter(this);
        }
    }

    public void SetDoorsOpen(bool open)
    {
        // 1. 방을 가로막는 문 오브젝트 비활성화 (기존 방식 유지)
        foreach (var door in doorObjects)
        {
            if (door != null)
            {
                door.SetActive(!open); // 열리면(open == true) 문을 끈다!
            }
        }

        // 2. 방 앵커에 달린 텔레포트 트리거들을 활성화 (아이작 방식 순간이동 연동)
        foreach (var anchor in anchors)
        {
            if (anchor != null)
            {
                DoorController doorCtrl = anchor.GetComponent<DoorController>();
                if (doorCtrl != null)
                {
                    doorCtrl.SetTriggerEnabled(open); // 열려 있을 때만 순간이동 텔레포트 활성화
                }
            }
        }
    }

    public void MarkCleared()
    {
        isCleared = true;
        SetDoorsOpen(true);
        _roomEvent?.OnRoomCleared(this);
    }

    public void MergeTilesToGlobal(Tilemap globalGround, Tilemap globalWall, Tilemap globalShadow, Tilemap globalUnsteppable = null)
    {
        _myTiles = new HashSet<Vector2Int>();
        StampTilemap(groundTilemap, globalGround);
        StampTilemap(wallTilemap, globalWall);
        StampTilemap(shadowTilemap, globalShadow);
        if (unsteppableTilemap != null && globalUnsteppable != null)
        {
            StampTilemap(unsteppableTilemap, globalUnsteppable);
        }
        if (groundTilemap != null) groundTilemap.gameObject.SetActive(false);
        if (wallTilemap != null) wallTilemap.gameObject.SetActive(false);
        if (shadowTilemap != null) shadowTilemap.gameObject.SetActive(false);
        if (unsteppableTilemap != null) unsteppableTilemap.gameObject.SetActive(false);
    }

    private HashSet<Vector2Int> _myTiles = null;

    public bool ContainsCell(Vector2Int cellPos)
    {
        if (_myTiles == null)
        {
            _myTiles = new HashSet<Vector2Int>();
            if (groundTilemap != null && MapGenerator.Instance != null && MapGenerator.Instance.GlobalMiniMapTilemap != null)
            {
                groundTilemap.CompressBounds();
                foreach (var pos in groundTilemap.cellBounds.allPositionsWithin)
                {
                    if (groundTilemap.HasTile(pos))
                    {
                        Vector3 worldPos = groundTilemap.CellToWorld(pos);
                        Vector3Int globalCellPos = MapGenerator.Instance.GlobalMiniMapTilemap.WorldToCell(worldPos);
                        _myTiles.Add(new Vector2Int(globalCellPos.x, globalCellPos.y));
                    }
                }
            }
            if (wallTilemap != null && MapGenerator.Instance != null && MapGenerator.Instance.GlobalMiniMapTilemap != null)
            {
                wallTilemap.CompressBounds();
                foreach (var pos in wallTilemap.cellBounds.allPositionsWithin)
                {
                    if (wallTilemap.HasTile(pos))
                    {
                        Vector3 worldPos = wallTilemap.CellToWorld(pos);
                        Vector3Int globalCellPos = MapGenerator.Instance.GlobalMiniMapTilemap.WorldToCell(worldPos);
                        _myTiles.Add(new Vector2Int(globalCellPos.x, globalCellPos.y));
                    }
                }
            }
            if (unsteppableTilemap != null && MapGenerator.Instance != null && MapGenerator.Instance.GlobalMiniMapTilemap != null)
            {
                unsteppableTilemap.CompressBounds();
                foreach (var pos in unsteppableTilemap.cellBounds.allPositionsWithin)
                {
                    if (unsteppableTilemap.HasTile(pos))
                    {
                        Vector3 worldPos = unsteppableTilemap.CellToWorld(pos);
                        Vector3Int globalCellPos = MapGenerator.Instance.GlobalMiniMapTilemap.WorldToCell(worldPos);
                        _myTiles.Add(new Vector2Int(globalCellPos.x, globalCellPos.y));
                    }
                }
            }
        }
        return _myTiles.Contains(cellPos);
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
                if (_myTiles != null) _myTiles.Add(new Vector2Int(targetCellPos.x, targetCellPos.y));
            }
        }
    }

    public void EraseTilesFromGlobal(Tilemap globalGround, Tilemap globalWall, Tilemap globalShadow, Tilemap globalUnsteppable = null)
    {
        UnstampTilemap(groundTilemap, globalGround);
        UnstampTilemap(wallTilemap, globalWall);
        UnstampTilemap(shadowTilemap, globalShadow);
        if (unsteppableTilemap != null && globalUnsteppable != null)
        {
            UnstampTilemap(unsteppableTilemap, globalUnsteppable);
        }
    }

    private void UnstampTilemap(Tilemap source, Tilemap target)
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
                target.SetTile(targetCellPos, null);
            }
        }
    }

    public void SnapToGrid(float unit)
    {
        Vector3 pos = transform.position;
        Tilemap mainTM = wallTilemap != null ? wallTilemap : groundTilemap;
        if (mainTM == null)
        {
            pos.x = Mathf.Round(pos.x / unit) * unit;
            pos.y = Mathf.Round(pos.y / unit) * unit;
            transform.position = pos;
            return;
        }

        Vector3 cellWorldPos = mainTM.CellToWorld(Vector3Int.zero);
        Vector3 targetWorldPos;
        if (MapGenerator.Instance != null && MapGenerator.Instance.GlobalMiniMapTilemap != null)
        {
            Tilemap globalTM = MapGenerator.Instance.GlobalMiniMapTilemap;
            Vector3Int globalCellPos = globalTM.WorldToCell(cellWorldPos);
            targetWorldPos = globalTM.CellToWorld(globalCellPos);
        }
        else
        {
            float targetX = Mathf.Round(cellWorldPos.x / unit) * unit;
            float targetY = Mathf.Round(cellWorldPos.y / unit) * unit;
            targetWorldPos = new Vector3(targetX, targetY, cellWorldPos.z);
        }

        Vector3 offset = targetWorldPos - cellWorldPos;
        pos.x += offset.x;
        pos.y += offset.y;
        transform.position = pos;
    }

    public void CleanupPhysics()
    {
        if (_rb != null)
        {
            _rb.simulated = false;
            _rb.bodyType = RigidbodyType2D.Static;
        }
        if (_physicsCollider != null) MapGenerator.SafeDestroy(_physicsCollider);
        if (_rb != null) MapGenerator.SafeDestroy(_rb);
    }

    // ⭐ [핵심 추가] 방 크기만큼의 FogTilemap 검은 타일을 관통해 싹 지워주는 함수
    public void RevealRoom()
    {
        if (MapGenerator.Instance == null) return;
        
        // 인스펙터에 등록했던 FogTilemap을 MapGenerator 싱글톤을 통해 가져옵니다.
        Tilemap fogTM = MapGenerator.Instance.FogTilemap;
        if (fogTM == null) return;

        // 1. 실제 월드 맵 상의 방 안개 제거
        Vector3 roomCenterWorld = transform.position + (Vector3)centerOffset;
        Vector3Int roomCenterCell = fogTM.WorldToCell(roomCenterWorld);

        // 방 크기(roomSize)의 절반을 돌며 벽 두께까지 지우기 위해 여유 마진(+3)을 줍니다.
        int halfX = (roomSize.x / 2) + 3;
        int halfY = (roomSize.y / 2) + 3;

        for (int x = -halfX; x <= halfX; x++)
        {
            for (int y = -halfY; y <= halfY; y++)
            {
                Vector3Int targetCell = new Vector3Int(roomCenterCell.x + x, roomCenterCell.y + y, 0);
                if (fogTM.HasTile(targetCell))
                {
                    fogTM.SetTile(targetCell, null); // 타일을 없앰으로써 시야 확보
                }
            }
        }

        // 2. 촘촘한 미니맵 영역 상의 안개 제거는 미니맵이 (-1000, -1000) 격리 공간으로 이전되어 겹칠 일 없으므로 생략합니다.
    }

    private Transform FindTransformRecursive(Transform current, string targetName)
    {
        foreach (Transform child in current)
        {
            if (child.name == targetName)
                return child;

            if (child.name.StartsWith("Fog") ||
                child.name.StartsWith("Decorate") ||
                child.name.StartsWith("Light") ||
                child.name.StartsWith("Global") ||
                child.name.StartsWith("DoorAnchor"))
            {
                continue;
            }

            Transform found = FindTransformRecursive(child, targetName);
            if (found != null)
                return found;
        }
        return null;
    }
}
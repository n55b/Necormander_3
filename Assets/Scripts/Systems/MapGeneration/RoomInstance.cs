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

    [HideInInspector] public int debugDepth = -1; // 맵 생성 시 계산된 깊이 저장용
    [HideInInspector] public int phaseIndex = -1; // 방이 생성된 맵 생성 페이즈 인덱스

    public float GetDiameter()
    {
        return Mathf.Max(roomSize.x, roomSize.y);
    }

    [Header("Combat & Events")]
    public bool isCleared = false;
    public List<GameObject> doorObjects = new List<GameObject>(); // MapGenerator에서 할당
    [SerializeField] private AudioClip roomBGM; // [추가] 이 방에서 나올 음악

    private IRoomEvent _roomEvent;
    private Rigidbody2D _rb;
    private BoxCollider2D _physicsCollider;
    private BoxCollider2D _triggerCollider;

    // --- 7단계 안개 가림막용 필드 ---
    private GameObject _fogMaskObj;
    private SpriteRenderer _fogMaskRenderer;
    private Coroutine _fadeCoroutine;

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

            UnityEditor.Handles.Label(transform.position, $"[{roomType}]\nDepth: {debugDepth}", style);
        }
    }
#endif

    public void Initialize(RoomType type)
    {
        doorObjects.Clear();
        roomType = type;
        _roomEvent = GetComponent<IRoomEvent>();

        // 앵커 수집
        anchors.Clear();
        anchors.AddRange(GetComponentsInChildren<RoomAnchor>());

        if (wallTilemap == null)
        {
            Transform wallTransform = FindTransformRecursive(transform, "Wall");
            if (wallTransform != null)
            {
                wallTilemap = wallTransform.GetComponent<Tilemap>();
            }
        }

        if (wallTilemap != null)
        {
            Transform wallTransform = wallTilemap.transform;
            var childCols = wallTransform.GetComponentsInChildren<Collider2D>();
            foreach (var ccol in childCols) { ccol.enabled = false; MapGenerator.SafeDestroy(ccol); }

            // [핵심 복구] 자식 타일맵에 붙어있는 Rigidbody2D 파괴
            // 이게 남아있으면 Static 바디로 취급되어 부모가 밀려날 때 Wall만 제자리에 남습니다!
            var childRbs = wallTransform.GetComponentsInChildren<Rigidbody2D>();
            foreach (var crb in childRbs) { crb.simulated = false; MapGenerator.SafeDestroy(crb); }
        }

        if (groundTilemap == null)
        {
            Transform groundTransform = FindTransformRecursive(transform, "Ground");
            if (groundTransform != null)
            {
                groundTilemap = groundTransform.GetComponent<Tilemap>();
            }
        }

        if (shadowTilemap == null)
        {
            Transform shadowTransform = FindTransformRecursive(transform, "Shadow");
            if (shadowTransform != null)
            {
                shadowTilemap = shadowTransform.GetComponent<Tilemap>();
            }
        }

        Tilemap mainTM = wallTilemap != null ? wallTilemap : groundTilemap;
        if (mainTM != null)
        {
            mainTM.CompressBounds();
            // 자식 타일맵의 localPosition 오프셋을 루트 부모(transform)까지 거슬러 올라가며 누적해서 더해줍니다.
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

        // 물리 연산용 리지드바디 및 콜라이더
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

        // [수정] 플레이어 진입 감지용 트리거 콜라이더 (방 안쪽 영역)
        _triggerCollider = gameObject.AddComponent<BoxCollider2D>();
        _triggerCollider.isTrigger = true;
        // 마진을 더 크게 (4.0f) 주어 통로에서 옆방 트리거를 스치는 현상 방지
        _triggerCollider.size = new Vector2(Mathf.Max(1, roomSize.x - 4f), Mathf.Max(1, roomSize.y - 4f));
        _triggerCollider.offset = centerOffset;

        gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

        // 안개 가림막 동적 생성 (스폰 방 제외)
        CreateFogMask();
    }

    public void ForceEnter()
    {
        RevealRoom();
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
            RevealRoom();
            if (isCleared || roomType == RoomType.Spawn) return;

            // [추가] 방 진입 시 BGM 변경
            if (roomBGM != null && SoundManager.Instance != null)
            {
                SoundManager.Instance.ChangeBGM(roomBGM);
            }

            // [수정] 이제 RoomInstance가 문을 자동으로 닫지 않습니다.
            // 문 제어권은 전적으로 _roomEvent(NormalRoomEvent 등)에게 위임합니다.
            Debug.Log($"<color=yellow>[Room]</color> Player Entered: {gameObject.name}");
            _roomEvent?.OnPlayerEnter(this);
        }
    }

    public void SetDoorsOpen(bool open)
    {
        foreach (var door in doorObjects)
        {
            if (door != null)
            {
                // [수정] 복잡한 컨트롤러 없이 직접 오브젝트를 끄고 켬
                // open == true (문 열림) -> Active(false)
                // open == false (문 닫힘) -> Active(true)
                door.SetActive(!open);
            }
        }
    }

    public void MarkCleared()
    {
        isCleared = true;
        SetDoorsOpen(true);
        _roomEvent?.OnRoomCleared(this);
    }

    public void MergeTilesToGlobal(Tilemap globalGround, Tilemap globalWall, Tilemap globalShadow)
    {
        _myTiles = new HashSet<Vector2Int>();
        StampTilemap(groundTilemap, globalGround);
        StampTilemap(wallTilemap, globalWall);
        StampTilemap(shadowTilemap, globalShadow);
        if (groundTilemap != null) groundTilemap.gameObject.SetActive(false);
        if (wallTilemap != null) wallTilemap.gameObject.SetActive(false);
        if (shadowTilemap != null) shadowTilemap.gameObject.SetActive(false);
    }

    private HashSet<Vector2Int> _myTiles = null;

    public bool ContainsCell(Vector2Int cellPos)
    {
        if (_myTiles == null)
        {
            _myTiles = new HashSet<Vector2Int>();
            if (groundTilemap != null && MapGenerator.Instance != null && MapGenerator.Instance.GlobalGroundTilemap != null)
            {
                groundTilemap.CompressBounds();
                foreach (var pos in groundTilemap.cellBounds.allPositionsWithin)
                {
                    if (groundTilemap.HasTile(pos))
                    {
                        Vector3 worldPos = groundTilemap.CellToWorld(pos);
                        Vector3Int globalCellPos = MapGenerator.Instance.GlobalGroundTilemap.WorldToCell(worldPos);
                        _myTiles.Add(new Vector2Int(globalCellPos.x, globalCellPos.y));
                    }
                }
            }
            if (wallTilemap != null && MapGenerator.Instance != null && MapGenerator.Instance.GlobalGroundTilemap != null)
            {
                wallTilemap.CompressBounds();
                foreach (var pos in wallTilemap.cellBounds.allPositionsWithin)
                {
                    if (wallTilemap.HasTile(pos))
                    {
                        Vector3 worldPos = wallTilemap.CellToWorld(pos);
                        Vector3Int globalCellPos = MapGenerator.Instance.GlobalGroundTilemap.WorldToCell(worldPos);
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

                if (_myTiles != null)
                {
                    _myTiles.Add(new Vector2Int(targetCellPos.x, targetCellPos.y));
                }
            }
        }
    }

    public void EraseTilesFromGlobal(Tilemap globalGround, Tilemap globalWall, Tilemap globalShadow)
    {
        UnstampTilemap(groundTilemap, globalGround);
        UnstampTilemap(wallTilemap, globalWall);
        UnstampTilemap(shadowTilemap, globalShadow);
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

        // 1. 메인 타일맵의 로컬 셀 (0, 0)의 현재 월드 좌표를 구합니다.
        Vector3 cellWorldPos = mainTM.CellToWorld(Vector3Int.zero);

        Vector3 targetWorldPos;
        if (MapGenerator.Instance != null && MapGenerator.Instance.GlobalGroundTilemap != null)
        {
            Tilemap globalTM = MapGenerator.Instance.GlobalGroundTilemap;
            // 2. 글로벌 타일맵 기준으로 가장 가까운 셀 좌표를 찾습니다.
            Vector3Int globalCellPos = globalTM.WorldToCell(cellWorldPos);
            // 3. 해당 셀의 정확한 월드 좌표를 얻습니다.
            targetWorldPos = globalTM.CellToWorld(globalCellPos);
        }
        else
        {
            // 폴백: 글로벌 타일맵이 없으면 정수 단위로 스냅합니다.
            float targetX = Mathf.Round(cellWorldPos.x / unit) * unit;
            float targetY = Mathf.Round(cellWorldPos.y / unit) * unit;
            targetWorldPos = new Vector3(targetX, targetY, cellWorldPos.z);
        }

        // 4. 목표 월드 좌표와 현재 월드 좌표의 오차(Offset)를 구합니다.
        Vector3 offset = targetWorldPos - cellWorldPos;

        // 5. 이 오차만큼 방의 루트 위치를 보정해 줍니다.
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

    private void CreateFogMask()
    {
        if (_fogMaskObj != null || roomType == RoomType.Spawn) return;

        _fogMaskObj = new GameObject("FogMask");
        _fogMaskObj.transform.SetParent(transform);
        _fogMaskObj.transform.localPosition = (Vector3)centerOffset;

        _fogMaskRenderer = _fogMaskObj.AddComponent<SpriteRenderer>();
        Texture2D tex = Texture2D.whiteTexture;
        // pixelsPerUnit을 tex.width로 동적 할당하여 해상도와 무관하게 1x1 유닛 크기를 강제 보장
        _fogMaskRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        _fogMaskRenderer.color = Color.black;

        // 소팅 레이어를 Effect로 변경하고 오더를 높여 방 전체 타일 및 플레이어 위로 렌더링되게 수정
        _fogMaskRenderer.sortingLayerName = "Effect";
        _fogMaskRenderer.sortingOrder = 100;

        // 패딩 마진 없이 딱 방 크기(roomSize)에 밀착되도록 수정
        _fogMaskObj.transform.localScale = new Vector3(roomSize.x, roomSize.y, 1f);
    }

    public void RevealRoom()
    {
        if (_fogMaskObj == null) return;
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeOutFogMask(1.0f));
    }

    private System.Collections.IEnumerator FadeOutFogMask(float duration)
    {
        /*
         * [가이드 주석: 타일맵 기반 안개 대체 시 적용 방법]
         * 만약 차후에 이 스프라이트 가림막을 타일맵(Fog Tilemap) 형태로 교체하고 싶다면:
         * 1. 맵 생성 완료 후 혹은 각 방 영역의 좌표들을 구합니다.
         * 2. globalFogTilemap 레이어를 생성하여 방 영역에 검은색 안개 타일들을 채워둡니다.
         * 3. 이 함수(FadeOutFogMask) 또는 RevealRoom 내에서 아래와 유사한 루프를 통해 타일들을 지웁니다:
         *    BoundsInt bounds = new BoundsInt(
         *        Mathf.FloorToInt(transform.position.x + centerOffset.x - roomSize.x * 0.5f),
         *        Mathf.FloorToInt(transform.position.y + centerOffset.y - roomSize.y * 0.5f),
         *        0, roomSize.x, roomSize.y, 1
         *    );
         *    foreach (var pos in bounds.allPositionsWithin) {
         *        globalFogTilemap.SetTile(pos, null);
         *    }
         */
        if (_fogMaskRenderer == null)
        {
            if (_fogMaskObj != null) MapGenerator.SafeDestroy(_fogMaskObj);
            yield break;
        }

        float elapsed = 0f;
        Color startColor = _fogMaskRenderer.color;
        Color targetColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _fogMaskRenderer.color = Color.Lerp(startColor, targetColor, elapsed / duration);
            yield return null;
        }

        _fogMaskRenderer.color = targetColor;
        MapGenerator.SafeDestroy(_fogMaskObj);
        _fogMaskObj = null;
        _fogMaskRenderer = null;
        _fadeCoroutine = null;
    }

    private Transform FindTransformRecursive(Transform current, string targetName)
    {
        foreach (Transform child in current)
        {
            if (child.name == targetName)
                return child;

            // 성능 최적화: 가림막, 데코 오브젝트, 조명 등 타일맵과 무관한 하위 계층은 깊이 탐색을 하지 않고 스킵합니다.
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

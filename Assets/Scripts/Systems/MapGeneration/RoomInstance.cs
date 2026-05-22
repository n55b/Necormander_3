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
    
    [HideInInspector] public int debugDepth = -1; // 맵 생성 시 계산된 깊이 저장용
    
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
        roomType = type;
        _roomEvent = GetComponent<IRoomEvent>();

        // 앵커 수집
        anchors.Clear();
        anchors.AddRange(GetComponentsInChildren<RoomAnchor>());

        Transform wallTransform = transform.Find("Wall");
        if (wallTransform != null) 
        {
            wallTilemap = wallTransform.GetComponent<Tilemap>();
            
            var childCols = wallTransform.GetComponentsInChildren<Collider2D>();
            foreach (var ccol in childCols) { ccol.enabled = false; Destroy(ccol); }
            
            // [핵심 복구] 자식 타일맵에 붙어있는 Rigidbody2D 파괴
            // 이게 남아있으면 Static 바디로 취급되어 부모가 밀려날 때 Wall만 제자리에 남습니다!
            var childRbs = wallTransform.GetComponentsInChildren<Rigidbody2D>();
            foreach (var crb in childRbs) { crb.simulated = false; Destroy(crb); }
        }
        
        Transform groundTransform = transform.Find("Ground");
        if (groundTransform != null) 
        {
            groundTilemap = groundTransform.GetComponent<Tilemap>();
        }

        Transform shadowTransform = transform.Find("Shadow");
        if (shadowTransform != null)
        {
            shadowTilemap = shadowTransform.GetComponent<Tilemap>();
        }

        Tilemap mainTM = wallTilemap != null ? wallTilemap : groundTilemap;
        if (mainTM != null)
        {
            mainTM.CompressBounds();
            // 자식 타일맵의 localPosition 오프셋을 더해 주어야 부모 기준의 정확한 기하 중심이 됩니다.
            centerOffset = (Vector2)mainTM.localBounds.center + (Vector2)mainTM.transform.localPosition;
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
        if (_physicsCollider != null) Destroy(_physicsCollider);
        if (_rb != null) Destroy(_rb);
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
            if (_fogMaskObj != null) Destroy(_fogMaskObj);
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
        Destroy(_fogMaskObj);
        _fogMaskObj = null;
        _fogMaskRenderer = null;
        _fadeCoroutine = null;
    }
}

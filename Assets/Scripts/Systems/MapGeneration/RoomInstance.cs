using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class RoomInstance : MonoBehaviour
{
    public RoomType roomType;
    /// <summary>
    /// 일반 방이 어떤 보상을 주는지.
    /// 각 타입이 몇 개 방에 배정되는지는 MapGenerationDataSO 의 카운트가 정한다 —
    /// 나중에 특정 풀을 Reward 방으로 옮기거나 로비 고정으로 돌릴 때 숫자만 바꾸면 된다.
    ///
    /// [26/08/15] 2번은 원래 SubSummon 이었다. 서브 소환수 삭제로 Item 이 됐다.
    /// <b>값(2)은 그대로 둔다</b> — enum 은 정수로 직렬화되므로, 중간 값을 지우거나 순서를 바꾸면
    /// 이미 저작된 방 프리팹들의 보상 타입이 통째로 밀린다.
    /// </summary>
    public enum NormalRewardType { PlayerSkill, MainSummon, Item }
    [Header("일반 방 보상 세부 설정")]
    public NormalRewardType normalRewardType;
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
    [HideInInspector] public bool isRevealedOnMinimap = false; // [추가] 미니맵에 밝혀져 전사된 상태인지 여부

    public float GetDiameter()
    {
        return Mathf.Max(roomSize.x, roomSize.y);
    }

    [Header("Combat & Events")]
    public bool isCleared = false;
    public bool hasBeenVisited = false;
    public List<GameObject> doorObjects = new List<GameObject>(); // MapGenerator에서 할당
    [SerializeField] private AudioClip roomBGM; // [추가] 이 방에서 나올 음악

    [Header("미니맵 아이콘 설정")]
    [Tooltip("방 프리팹 하위의 MiniMapIcons 최상위 부모 오브젝트입니다. 미지정 시 transform.Find(\"MiniMapIcons\")로 자동 검색합니다.")]
    [SerializeField] private Transform miniMapIconsParent;

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

        // [수정] 일반 방(Normal)일 때 보상 종류를 전체 Enum 개수에 맞추어 무작위로 초기 결정 (맵 빌더에서 오버라이트 가능)
        if (roomType == RoomType.Normal)
        {
            normalRewardType = (NormalRewardType)Random.Range(0, System.Enum.GetValues(typeof(NormalRewardType)).Length);
        }

        // 미니맵 아이콘들 상태 갱신
        SetRewardTypeAndSyncIcon(normalRewardType);

        // [수정] 방 한가운데 감지용 콜라이더(CombatCenterTrigger) 프리팹 바인딩 및 누락 시 Fallback 안전 자동 셋업
        // Augment(증강 선택) 방도 전투 방이다 — 한가운데 도달하면 카드가 뜨고 그 뒤에 전투가 시작된다.
        if (roomType == RoomType.Normal || roomType == RoomType.Elite || roomType == RoomType.Boss || roomType == RoomType.Augment)
        {
            Transform centerTriggerTrans = transform.Find("CombatCenterTrigger");
            GameObject centerTriggerObj = null;

            if (centerTriggerTrans != null)
            {
                centerTriggerObj = centerTriggerTrans.gameObject;
            }
            else
            {
                // Fallback: 프리팹 누락 시 씬 붕괴 방지를 위해 코드로 동적 백업 생성
                centerTriggerObj = new GameObject("CombatCenterTrigger");
                centerTriggerObj.transform.SetParent(this.transform, false);
                centerTriggerObj.transform.localPosition = (Vector3)centerOffset;

                var circleCol = centerTriggerObj.AddComponent<CircleCollider2D>();
                circleCol.isTrigger = true;
                circleCol.radius = 3.5f;
                Debug.Log($"<color=yellow>[RoomInstance]</color> Built fallback center trigger for {gameObject.name}");
            }

            if (centerTriggerObj != null)
            {
                var triggerComp = centerTriggerObj.GetComponent<RoomCombatTrigger>() ?? centerTriggerObj.AddComponent<RoomCombatTrigger>();
                triggerComp.Init(this);
            }
        }
    }

    /// <summary>
    /// 외부 맵 생성기 등에서 방 보상 속성을 지정하고 미니맵 아이콘의 상태를 동적으로 동기화 갱신합니다.
    /// </summary>
    public void SetRewardTypeAndSyncIcon(NormalRewardType rewardType)
    {
        normalRewardType = rewardType;

        // 인스펙터에 직접 연결된 부모 트랜스폼을 우선 사용하고, 없을 때만 동적 탐색(Find)으로 보완
        Transform iconsParent = miniMapIconsParent != null ? miniMapIconsParent : transform.Find("MiniMapIcons");
        if (iconsParent != null)
        {
            // 모든 자식 아이콘 오브젝트 비활성화
            for (int i = 0; i < iconsParent.childCount; i++)
            {
                iconsParent.GetChild(i).gameObject.SetActive(false);
            }

            Transform targetIcon = null;

            if (roomType == RoomType.Normal)
            {
                if (normalRewardType == NormalRewardType.PlayerSkill)
                    targetIcon = iconsParent.Find("PlayerSkillMinimapIcon") ?? iconsParent.Find("playerskill");
                else
                    // ponytail: 메인/서브 소환수가 미니맵 아이콘을 공유한다. 전용 아이콘이 생기면
                    // 룸 프리팹에 오브젝트를 추가하고 여기서 갈라주면 된다.
                    targetIcon = iconsParent.Find("MinionSkillMinimapIcon") ?? iconsParent.Find("minionskill");
            }
            else if (roomType == RoomType.Boss)
            {
                targetIcon = iconsParent.Find("boss") ?? iconsParent.Find("BossMinimapIcon");
            }
            else if (roomType == RoomType.Shop)
            {
                targetIcon = iconsParent.Find("ShopMiniMapIcon") ?? iconsParent.Find("shop") ?? iconsParent.Find("ShopMinimapIcon");
            }
            else if (roomType == RoomType.EnhanceShop)
            {
                // 전용 아트가 생기기 전까지는 유니티 기본 스프라이트를 주황색으로 물들인 네모다.
                // (일반 상점의 노랑과 안 겹치면서 보스 빨강/엘리트 보라와도 구분되는 색.)
                // 진짜 아이콘이 생기면 MiniMapIcons 프리팹의 이 오브젝트 스프라이트만 갈아 끼우면 된다.
                targetIcon = iconsParent.Find("EnhanceShopMiniMapIcon") ?? iconsParent.Find("enhanceshop");
            }
            else if (roomType == RoomType.Spawn)
            {
                targetIcon = iconsParent.Find("spawn") ?? iconsParent.Find("SpawnMinimapIcon");
            }
            else if (roomType == RoomType.Reward)
            {
                targetIcon = iconsParent.Find("RewardMiniMapIcon") ?? iconsParent.Find("reward") ?? iconsParent.Find("RewardMinimapIcon");
            }
            else if (roomType == RoomType.Elite)
            {
                targetIcon = iconsParent.Find("EliteMiniMapIcon") ?? iconsParent.Find("elite");
            }
            else if (roomType == RoomType.Augment)
            {
                // 아직 전용 아트가 없어서 유니티 기본 스프라이트를 빨갛게 물들인 네모다.
                // 진짜 아이콘이 생기면 MiniMapIcons 프리팹의 이 오브젝트 스프라이트만 갈아 끼우면 된다.
                targetIcon = iconsParent.Find("AugmentMiniMapIcon") ?? iconsParent.Find("augment");
            }

            if (targetIcon != null)
            {
                targetIcon.gameObject.SetActive(true);
            }
        }
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

            // [수정] 전투를 시작하지 않는 예외적인 방(스폰, 상점, 강화 상점, 보상방 등)에만 입장 즉시 BGM 및 이벤트 격발
            if (roomType == RoomType.Spawn || roomType == RoomType.Shop || roomType == RoomType.EnhanceShop || roomType == RoomType.Reward)
            {
                if (roomBGM != null && SoundManager.Instance != null)
                {
                    SoundManager.Instance.ChangeBGM(roomBGM);
                }
                _roomEvent?.OnPlayerEnter(this);
            }
        }
    }

    /// <summary>
    /// 플레이어가 방 한가운데 CombatCenterTrigger에 도달하는 순간 호출되어 실제 전투를 시작합니다.
    /// </summary>
    public void StartCombatEvent()
    {
        if (isCleared || roomType == RoomType.Spawn || roomType == RoomType.Shop || roomType == RoomType.EnhanceShop || roomType == RoomType.Reward) return;

        // 문 닫음
        SetDoorsOpen(false);

        // BGM 전투 음악 전환
        if (roomBGM != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.ChangeBGM(roomBGM);
        }

        // 룸 이벤트 전투 시퀀스 시작
        _roomEvent?.OnPlayerEnter(this);
        Debug.Log($"<color=yellow>[Room]</color> Player reached center, combat sequence triggered: {gameObject.name}");
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

    /// <summary>
    /// 방이 클리어될 때마다 발화한다. 서브 소환수 패시브처럼 '방 클리어 시' 발동하는 것들이 구독한다.
    /// IRoomEvent.OnRoomCleared 는 방 타입별 구현이라 방 밖에서는 구독할 수 없어서 별도로 둔다.
    /// </summary>
    public static event System.Action<RoomInstance> OnAnyRoomCleared;

    public void MarkCleared()
    {
        isCleared = true;
        SetDoorsOpen(true);
        _roomEvent?.OnRoomCleared(this);

        // [추가] 방 클리어 시 플레이어 체력을 10 회복시킵니다.
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            var pHealth = GameManager.Instance.PLAYERCONTROLLER.GetComponentInChildren<CharacterHealth>();
            if (pHealth != null && !pHealth.IsDead)
            {
                pHealth.Heal(10f);
            }
        }

        OnAnyRoomCleared?.Invoke(this);
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
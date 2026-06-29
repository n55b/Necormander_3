using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using TMPro;

public class UIBasedMiniMap : MonoBehaviour
{
    public static UIBasedMiniMap Instance { get; private set; }

    [Header("UI 컨테이너 설정")]
    [SerializeField] private RectTransform fullMapContainer; // MiniMapUI 오브젝트 연결
    [SerializeField] private RectTransform hudMapContainer;  // Image_MiniMap 오브젝트 연결

    [Header("전체 지도 (Full Map) 설정")]
    [SerializeField] private float fullRoomSize = 50f;
    [SerializeField] private float fullRoomSpacing = 15f;

    [Header("HUD 미니맵 (HUD Map) 설정")]
    [SerializeField] private float hudRoomSize = 20f;
    [SerializeField] private float hudRoomSpacing = 6f;

    [Header("기본 폴백 스프라이트 설정")]
    [SerializeField] private Sprite fallbackRoomSprite;       // 방 기본 흰색 사각형 스프라이트

    [Header("🌟 1. 방 테두리 / 배경 커스텀 에셋 (비워두면 기본 틴팅 폴백 작동)")]
    [SerializeField] private Sprite customNormalRoomSprite;   // 일반 방 전용 오버라이드 스킨
    [SerializeField] private Sprite customBossRoomSprite;     // 보스 방 전용 오버라이드 스킨
    [SerializeField] private Sprite customShopRoomSprite;     // 상점 방 전용 오버라이드 스킨
    [SerializeField] private Sprite customRewardRoomSprite;   // 보상 방 전용 오버라이드 스킨
    [SerializeField] private Sprite customEliteRoomSprite;    // 엘리트 방 전용 오버라이드 스킨

    [Header("🌟 2. 지형 도트 커스텀 연출")]
    [SerializeField] private Sprite customTerrainDotSprite;   // 지형 도트용 스프라이트 (비워두면 사각형)
    [SerializeField] private bool useTerrainShadow = true;     // 2D 입체 그림자 효과 사용 여부
    [SerializeField] private Color terrainShadowColor = new Color(0f, 0f, 0f, 0.6f);
    [SerializeField] private Vector2 terrainShadowOffset = new Vector2(1.5f, -1.5f);

    [Header("🌟 3. 플레이어 / 적군 마커 커스텀 오버라이드")]
    [SerializeField] private Sprite customPlayerIcon;         // 플레이어 마커 이미지 (비워두면 기본 얼굴)
    [SerializeField] private bool syncPlayerZRotation = true; // 플레이어 회전 각도(방향) 동기화 여부
    [SerializeField] private Sprite customEnemyIcon;          // 적 마커 이미지 (비워두면 빨간 도트)

    // 스프라이트 시트 로드 캐시
    private Sprite _playerIcon;
    private Sprite _shopIcon;
    private Sprite _rewardIcon;
    private Sprite _stairIcon;

    private List<GameObject> _spawnedFullRooms = new List<GameObject>();
    private List<GameObject> _spawnedHudRooms = new List<GameObject>();

    // 방별 실제 바닥 타일맵 로컬 좌표 캐시 (방 모양 반영용)
    private Dictionary<string, HashSet<Vector2Int>> _roomTilemapsCache = new Dictionary<string, HashSet<Vector2Int>>();

    // 몬스터 레이더 추적용 캐시
    private List<GameObject> _cachedEnemies = new List<GameObject>();
    private float _enemyScanTimer = 0f;
    private const float EnemyScanInterval = 0.15f;

    // 실시간 적 마커 딕셔너리
    private Dictionary<GameObject, List<RectTransform>> _enemyMarkers = new Dictionary<GameObject, List<RectTransform>>();

    // 실시간 플레이어 마커 RectTransform 캐시
    private List<RectTransform> _playerMarkers = new List<RectTransform>();

    // 실시간 적 마커들을 꽂아줄 UI 부모 컨테이너
    private Dictionary<string, Transform> _roomRadarContainers = new Dictionary<string, Transform>();

    // 전투 및 방 줌 상태 캐시
    private bool _lastWasBattle = false;

    private void Awake()
    {
        Instance = this;
        
        if (fullMapContainer == null)
        {
            Transform t = transform.Find("MiniMapUI");
            if (t != null) fullMapContainer = t.GetComponent<RectTransform>();
        }
        if (hudMapContainer == null)
        {
            Transform t = transform.Find("Image_MiniMap");
            if (t != null) hudMapContainer = t.GetComponent<RectTransform>();
        }

        // Sprites/Icon_map_32px 스프라이트 시트 동적 로드
        Sprite[] icons = Resources.LoadAll<Sprite>("Sprites/Icon_map_32px");
        if (icons != null)
        {
            foreach (var s in icons)
            {
                if (s.name == "Icon_map_Player") _playerIcon = s;
                else if (s.name == "Icon_map_Shop") _shopIcon = s;
                else if (s.name == "Icon_map_Reward") _rewardIcon = s;
                else if (s.name == "Icon_map_Stair") _stairIcon = s;
            }
        }
    }

    private void Update()
    {
        if (MapGenerator.Instance == null) return;
        RoomInstance currentRoom = MapGenerator.Instance.CurrentRoom;
        if (currentRoom == null) return;

        // 1. 전투 여부에 따른 실시간 HUD 미니맵 다이내믹 줌 연출
        bool isBattle = false;
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            isBattle = GameManager.Instance.PLAYERCONTROLLER.GetPlayerState() == PlayerStates.Battle;
        }

        if (isBattle != _lastWasBattle)
        {
            _lastWasBattle = isBattle;
            hudRoomSize = isBattle ? 90f : 20f; 
            hudRoomSpacing = isBattle ? 0f : 6f;
            RefreshMap();
        }

        // 2. 경량화 적군 위치 물리 스캔
        _enemyScanTimer += Time.deltaTime;
        if (_enemyScanTimer >= EnemyScanInterval)
        {
            _enemyScanTimer = 0f;
            ScanRoomEnemies(currentRoom);
        }

        // 3. 플레이어 및 적군 마커 위치 매 프레임 실시간 레이더 좌표 연산 동기화
        UpdateRealTimeMarkers(currentRoom);
    }

    public void RefreshMap()
    {
        if (MapGenerator.Instance == null) return;

        RoomInstance currentRoom = MapGenerator.Instance.CurrentRoom;
        if (currentRoom == null) return;

        _enemyMarkers.Clear();
        _playerMarkers.Clear();
        _roomRadarContainers.Clear();

        bool isBattle = false;
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            isBattle = GameManager.Instance.PLAYERCONTROLLER.GetPlayerState() == PlayerStates.Battle;
        }

        // 전체 지도 그리기
        if (fullMapContainer != null)
        {
            ClearContainer(ref _spawnedFullRooms);
            DrawRoomsOnContainer(fullMapContainer, _spawnedFullRooms, currentRoom, fullRoomSize, fullRoomSpacing, true, false);
        }

        // HUD 미니맵 그리기 (전투 시 단일 방 포커싱 적용)
        if (hudMapContainer != null)
        {
            ClearContainer(ref _spawnedHudRooms);
            DrawRoomsOnContainer(hudMapContainer, _spawnedHudRooms, currentRoom, hudRoomSize, hudRoomSpacing, false, isBattle);
        }

        ScanRoomEnemies(currentRoom);
        UpdateRealTimeMarkers(currentRoom);
    }

    private void ClearContainer(ref List<GameObject> spawnedList)
    {
        foreach (var ui in spawnedList)
        {
            if (ui != null) Destroy(ui);
        }
        spawnedList.Clear();
    }

    private void DrawRoomsOnContainer(
        RectTransform container, 
        List<GameObject> spawnedList, 
        RoomInstance currentRoom, 
        float roomUiSize, 
        float roomUiSpacing, 
        bool isFullMap,
        bool focusOnlyCurrentRoom)
    {
        foreach (var room in MapGenerator.Instance.AllRooms)
        {
            if (room == null) continue;

            if (focusOnlyCurrentRoom && room != currentRoom) continue;

            bool isVisited = room.hasBeenVisited || room.roomType == RoomType.Spawn;
            bool isRevealed = isVisited;

            if (!isRevealed)
            {
                var connected = MapGenerator.Instance.GetConnectedRooms(room);
                foreach (var conn in connected)
                {
                    if (conn != null && (conn.hasBeenVisited || conn.roomType == RoomType.Spawn))
                    {
                        isRevealed = true;
                        break;
                    }
                }
            }

            if (!isRevealed) continue;

            // 방 UI 오브젝트 동적 생성
            GameObject roomObj = new GameObject($"RoomUI_{room.name}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            roomObj.transform.SetParent(container, false);
            spawnedList.Add(roomObj);

            RectTransform rt = roomObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(roomUiSize, roomUiSize);

            Vector2 gridDiff = new Vector2(
                room.gridPosition.x - currentRoom.gridPosition.x,
                room.gridPosition.y - currentRoom.gridPosition.y
            );
            rt.anchoredPosition = gridDiff * (roomUiSize + roomUiSpacing);

            Image img = roomObj.GetComponent<Image>();
            img.sprite = fallbackRoomSprite;

            Button btn = roomObj.GetComponent<Button>();

            // 🌟 1. 방 모양 지형 그리기 (전투 줌인 시 현재 방 내부에만 지형 투과)
            if (focusOnlyCurrentRoom && room == currentRoom)
            {
                img.color = new Color(0f, 0f, 0f, 0f); // 배경 사각형 투명화
                DrawRoomTerrainShape(roomObj, room, roomUiSize);
            }

            // 방 프리팹 깊은 자식 계층의 MiniMapIcon 스프라이트 복사
            Sprite roomIconSprite = null;
            Color iconColor = Color.white;

            SpriteRenderer[] childRenderers = room.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in childRenderers)
            {
                if (sr != null && sr.gameObject.name == "MiniMapIcon")
                {
                    roomIconSprite = sr.sprite;
                    iconColor = sr.color;
                    break;
                }
            }

            if (roomIconSprite == null)
            {
                if (room.roomType == RoomType.Shop) roomIconSprite = _shopIcon;
                else if (room.roomType == RoomType.Reward) roomIconSprite = _rewardIcon;
                else if (room.roomType == RoomType.Spawn) roomIconSprite = _stairIcon;
            }

            // 🌟 2. 인스펙터 커스텀 방 스프라이트 교체 분기 (비워져 있으면 fallback 기본 컬러 틴팅)
            Sprite customRoomSprite = GetCustomRoomSprite(room.roomType);

            if (!focusOnlyCurrentRoom)
            {
                if (customRoomSprite != null)
                {
                    // 커스텀 방 스프라이트 장착 완료시 틴트 없이 원본 출력
                    img.sprite = customRoomSprite;
                    img.color = Color.white;
                }
                else
                {
                    // 커스텀 스프라이트 누락 시: 기존 틴팅 방식으로 그리기
                    if (isVisited)
                    {
                        switch (room.roomType)
                        {
                            case RoomType.Spawn:
                                img.color = new Color(0.2f, 0.7f, 1f, 1.0f);
                                break;
                            case RoomType.Shop:
                                img.color = new Color(1f, 0.85f, 0.2f, 1.0f);
                                break;
                            case RoomType.Reward:
                                img.color = new Color(0.2f, 0.85f, 0.4f, 1.0f);
                                break;
                            case RoomType.Boss:
                                img.color = new Color(0.95f, 0.2f, 0.2f, 1.0f);
                                break;
                            case RoomType.Elite:
                                img.color = new Color(0.8f, 0.3f, 0.9f, 1.0f);
                                break;
                            default:
                                img.color = new Color(0.35f, 0.45f, 0.65f, 1.0f);
                                break;
                        }
                    }
                    else
                    {
                        img.color = new Color(0.4f, 0.4f, 0.4f, 0.6f);
                    }
                }

                // 기호 및 룸 아이콘 얹기
                if (isVisited)
                {
                    if (roomIconSprite != null)
                    {
                        AddMarkerImage(roomObj, roomIconSprite, roomUiSize * 0.6f, iconColor);
                    }
                    else if (room.roomType == RoomType.Boss)
                    {
                        AddMarkerText(roomObj, "☠", roomUiSize);
                    }

                    if (isFullMap)
                    {
                        btn.interactable = true;
                        btn.onClick.AddListener(() => { TeleportToRoom(room); });
                    }
                    else
                    {
                        btn.interactable = false;
                    }
                }
                else
                {
                    btn.interactable = false;
                    if (roomIconSprite != null)
                    {
                        AddMarkerImage(roomObj, roomIconSprite, roomUiSize * 0.6f, new Color(iconColor.r, iconColor.g, iconColor.b, 0.4f));
                    }
                    else if (room.roomType == RoomType.Boss)
                    {
                        AddMarkerText(roomObj, "☠", roomUiSize, 0.4f);
                    }
                }
            }

            // 🌟 3. 플레이어 캐릭터 머리 아이콘 연동
            if (room == currentRoom)
            {
                GameObject playerMarker = new GameObject("PlayerMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                playerMarker.transform.SetParent(roomObj.transform, false);
                
                RectTransform pRt = playerMarker.GetComponent<RectTransform>();
                pRt.sizeDelta = new Vector2(roomUiSize * 0.35f, roomUiSize * 0.35f);
                pRt.anchoredPosition = Vector2.zero;

                Image pImg = playerMarker.GetComponent<Image>();
                // 인스펙터 커스텀 오버라이드 또는 동적 시트 로드 아이콘 선택
                pImg.sprite = (customPlayerIcon != null) ? customPlayerIcon : _playerIcon;
                pImg.color = Color.white; 

                _playerMarkers.Add(pRt);
            }

            // 🌟 4. 적군 레이더 컨테이너 등록
            if (room == currentRoom)
            {
                GameObject radarContainer = new GameObject("RadarContainer", typeof(RectTransform));
                radarContainer.transform.SetParent(roomObj.transform, false);
                RectTransform radarRt = radarContainer.GetComponent<RectTransform>();
                radarRt.sizeDelta = new Vector2(roomUiSize, roomUiSize);
                radarRt.anchoredPosition = Vector2.zero;

                string containerKey = isFullMap ? "full" : "hud";
                _roomRadarContainers[containerKey] = radarContainer.transform;
            }
        }
    }

    private Sprite GetCustomRoomSprite(RoomType type)
    {
        switch (type)
        {
            case RoomType.Spawn: return customNormalRoomSprite; // 스폰 방도 기본 일반 형태 활용
            case RoomType.Shop: return customShopRoomSprite;
            case RoomType.Reward: return customRewardRoomSprite;
            case RoomType.Boss: return customBossRoomSprite;
            case RoomType.Elite: return customEliteRoomSprite;
            default: return customNormalRoomSprite;
        }
    }

    // 🌟 방의 실제 Ground 타일 배치 캐시를 읽어 미니 픽셀 도트들로 방 모양 테두리를 드로잉합니다.
    private void DrawRoomTerrainShape(GameObject roomObj, RoomInstance room, float roomUiSize)
    {
        string cacheKey = room.gameObject.name;
        if (!_roomTilemapsCache.ContainsKey(cacheKey))
        {
            HashSet<Vector2Int> tiles = new HashSet<Vector2Int>();
            Tilemap[] tms = room.GetComponentsInChildren<Tilemap>(true);
            foreach (var tm in tms)
            {
                if (tm == null || !tm.name.Contains("Ground")) continue;
                
                tm.CompressBounds();
                BoundsInt bounds = tm.cellBounds;
                Vector3 roomCenterWorld = room.transform.position + (Vector3)room.centerOffset;

                foreach (var pos in bounds.allPositionsWithin)
                {
                    if (tm.HasTile(pos))
                    {
                        Vector3 tileWorld = tm.CellToWorld(pos);
                        int localX = Mathf.RoundToInt(tileWorld.x - roomCenterWorld.x);
                        int localY = Mathf.RoundToInt(tileWorld.y - roomCenterWorld.y);
                        tiles.Add(new Vector2Int(localX, localY));
                    }
                }
            }
            _roomTilemapsCache[cacheKey] = tiles;
        }

        var terrainTiles = _roomTilemapsCache[cacheKey];
        if (terrainTiles.Count == 0) return;

        GameObject terrainContainer = new GameObject("TerrainContainer", typeof(RectTransform));
        terrainContainer.transform.SetParent(roomObj.transform, false);
        RectTransform containerRt = terrainContainer.GetComponent<RectTransform>();
        containerRt.sizeDelta = new Vector2(roomUiSize, roomUiSize);
        containerRt.anchoredPosition = Vector2.zero;

        float roomW = room.roomSize.x;
        float roomH = room.roomSize.y;
        if (roomW <= 0.1f) roomW = 25f;
        if (roomH <= 0.1f) roomH = 25f;

        float dotSize = (roomUiSize / roomW) * 0.95f; 

        foreach (var tilePos in terrainTiles)
        {
            GameObject dotObj = new GameObject("TerrainDot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dotObj.transform.SetParent(terrainContainer.transform, false);

            RectTransform dRt = dotObj.GetComponent<RectTransform>();
            dRt.sizeDelta = new Vector2(dotSize, dotSize);

            float normX = tilePos.x / roomW;
            float normY = tilePos.y / roomH;
            dRt.anchoredPosition = new Vector2(normX * roomUiSize, normY * roomUiSize);

            Image img = dotObj.GetComponent<Image>();
            img.sprite = (customTerrainDotSprite != null) ? customTerrainDotSprite : fallbackRoomSprite;
            img.color = new Color(0.25f, 0.4f, 0.6f, 0.75f); // 차분하고 멋스러운 블루 그레이 틴트

            // 🌟 [입체 그림자 셋팅] useTerrainShadow가 인스펙터에서 켜져 있다면 UI Shadow 컴포넌트 자동 주입
            if (useTerrainShadow)
            {
                Shadow shadow = dotObj.AddComponent<Shadow>();
                shadow.effectColor = terrainShadowColor;
                shadow.effectDistance = terrainShadowOffset;
            }
        }
    }

    private void AddMarkerImage(GameObject parent, Sprite sprite, float size, Color color)
    {
        GameObject imgObj = new GameObject("MarkerImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imgObj.transform.SetParent(parent.transform, false);

        RectTransform rt = imgObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;

        Image img = imgObj.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
    }

    private void AddMarkerText(GameObject parent, string symbol, float roomUiSize, float opacity = 1f)
    {
        GameObject textObj = new GameObject("SymbolText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(parent.transform, false);

        RectTransform rt = textObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(roomUiSize, roomUiSize);
        rt.anchoredPosition = Vector2.zero;

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = symbol;
        tmp.fontSize = roomUiSize * 0.5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, opacity);
    }

    private void ScanRoomEnemies(RoomInstance currentRoom)
    {
        _cachedEnemies.Clear();

        int enemyLayer = LayerMask.GetMask("Enemy");
        int bossLayer = LayerMask.GetMask("Boss");
        int targetMask = enemyLayer | bossLayer;

        Vector3 roomCenter = currentRoom.transform.position + (Vector3)currentRoom.centerOffset;
        Vector2 boxSize = new Vector2(currentRoom.roomSize.x + 3f, currentRoom.roomSize.y + 3f);

        Collider2D[] cols = Physics2D.OverlapBoxAll(roomCenter, boxSize, 0f, targetMask);
        if (cols != null)
        {
            foreach (var col in cols)
            {
                if (col == null) continue;
                GameObject enemyObj = col.gameObject;
                
                if (col.transform.parent != null && col.gameObject.layer == LayerMask.NameToLayer("Enemy"))
                {
                    enemyObj = col.transform.root.gameObject;
                }

                if (!_cachedEnemies.Contains(enemyObj))
                {
                    _cachedEnemies.Add(enemyObj);
                }
            }
        }

        // 씬 상에서 죽은 적의 UI 마커 즉시 제거
        List<GameObject> deadEnemies = new List<GameObject>();
        foreach (var enemy in _enemyMarkers.Keys)
        {
            if (enemy == null || !_cachedEnemies.Contains(enemy))
            {
                deadEnemies.Add(enemy);
            }
        }
        foreach (var dead in deadEnemies)
        {
            if (_enemyMarkers.TryGetValue(dead, out var list))
            {
                foreach (var marker in list)
                {
                    if (marker != null) Destroy(marker.gameObject);
                }
            }
            _enemyMarkers.Remove(dead);
        }

        // 실시간 플레이 중 새로 생성(스폰)된 적의 UI 마커 즉시 동적 소환
        foreach (var enemy in _cachedEnemies)
        {
            if (enemy == null) continue;
            if (!_enemyMarkers.ContainsKey(enemy))
            {
                List<RectTransform> newMarkers = new List<RectTransform>();
                
                foreach (var pair in _roomRadarContainers)
                {
                    Transform parentContainer = pair.Value;
                    if (parentContainer == null) continue;

                    GameObject enemyMarker = new GameObject("EnemyMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    enemyMarker.transform.SetParent(parentContainer, false);

                    RectTransform eRt = enemyMarker.GetComponent<RectTransform>();
                    
                    float parentSize = parentContainer.parent.GetComponent<RectTransform>().sizeDelta.x;
                    
                    // 🌟 [적 마커 스케일 분기] 보스이거나 이름에 Boss가 섞인 강한 적은 마커 크기를 1.8배 확대
                    bool isBoss = enemy.CompareTag("Boss") || enemy.name.Contains("Boss");
                    float scaleMultiplier = isBoss ? 0.22f : 0.12f;
                    
                    eRt.sizeDelta = new Vector2(parentSize * scaleMultiplier, parentSize * scaleMultiplier);

                    Image eImg = enemyMarker.GetComponent<Image>();
                    eImg.sprite = (customEnemyIcon != null) ? customEnemyIcon : null; // 커스텀 적 아이콘 오버라이드 지원
                    eImg.color = isBoss ? new Color(1f, 0.1f, 0.1f, 1f) : Color.red; // 보스는 진한 빨간색 강조

                    newMarkers.Add(eRt);
                }
                _enemyMarkers[enemy] = newMarkers;
            }
        }
    }

    private void UpdateRealTimeMarkers(RoomInstance currentRoom)
    {
        if (currentRoom == null) return;
        Vector3 roomCenter = currentRoom.transform.position + (Vector3)currentRoom.centerOffset;

        float roomW = currentRoom.roomSize.x;
        float roomH = currentRoom.roomSize.y;
        if (roomW <= 0.1f) roomW = 25f;
        if (roomH <= 0.1f) roomH = 25f;

        // 1. 플레이어 위치 및 회전 각도 실시간 동기화
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            Vector3 playerPos = GameManager.Instance.PLAYERCONTROLLER.transform.position;
            Vector3 pDiff = playerPos - roomCenter;

            float pNormX = pDiff.x / roomW;
            float pNormY = pDiff.y / roomH;

            foreach (var pRt in _playerMarkers)
            {
                if (pRt != null && pRt.parent != null)
                {
                    float parentSize = pRt.parent.GetComponent<RectTransform>().sizeDelta.x;
                    pRt.anchoredPosition = new Vector2(pNormX * parentSize, pNormY * parentSize);

                    // 🌟 [플레이어 360도 회전 실시간 매핑] syncPlayerZRotation이 켜져 있을 때 각도 복사
                    if (syncPlayerZRotation)
                    {
                        float playerZRot = GameManager.Instance.PLAYERCONTROLLER.transform.rotation.eulerAngles.z;
                        pRt.localRotation = Quaternion.Euler(0f, 0f, playerZRot);
                    }
                }
            }
        }

        // 2. 적군 위치 실시간 스크롤 동기화
        foreach (var pair in _enemyMarkers)
        {
            GameObject enemy = pair.Key;
            List<RectTransform> markerList = pair.Value;

            if (enemy == null || markerList == null) continue;

            Vector3 eDiff = enemy.transform.position - roomCenter;
            float eNormX = eDiff.x / roomW;
            float eNormY = eDiff.y / roomH;

            foreach (var markerRt in markerList)
            {
                if (markerRt != null && markerRt.parent != null && markerRt.parent.parent != null)
                {
                    float parentSize = markerRt.parent.parent.GetComponent<RectTransform>().sizeDelta.x;
                    markerRt.anchoredPosition = new Vector2(eNormX * parentSize, eNormY * parentSize);
                }
            }
        }
    }

    private void TeleportToRoom(RoomInstance room)
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        if (GameManager.Instance.PLAYERCONTROLLER.GetPlayerState() == PlayerStates.Battle) return;

        Transform playerTr = GameManager.Instance.PLAYERCONTROLLER.transform;
        Vector3 targetPos = room.transform.position + (Vector3)room.centerOffset;
        targetPos.z = playerTr.position.z;
        playerTr.position = targetPos;

        if (MapGenerator.Instance != null)
        {
            MapGenerator.Instance.SetCurrentRoom(room);
        }
        RefreshMap();

        Debug.Log($"<color=green>[Teleport]</color> {room.gameObject.name}의 중심으로 UI 텔레포트 완료!");

        var mapUI = Object.FindFirstObjectByType<MapUIManager>();
        if (mapUI != null) mapUI.CloseMapUI();
    }
}

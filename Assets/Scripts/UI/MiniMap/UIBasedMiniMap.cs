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

    [Header("스프라이트 설정")]
    [SerializeField] private Sprite roomSprite;              // 방 기본 흰색 사각형 스프라이트

    // 스프라이트 시트 로드 캐시
    private Sprite _playerIcon;
    private Sprite _shopIcon;
    private Sprite _rewardIcon;
    private Sprite _stairIcon;

    private List<GameObject> _spawnedFullRooms = new List<GameObject>();
    private List<GameObject> _spawnedHudRooms = new List<GameObject>();

    // 🌟 방별 실제 바닥 타일맵 로컬 좌표 캐시 (방 모양 반영용)
    // Key: 방 이름, Value: 방 중심 기준 타일들의 로컬 상대 셀 좌표 목록
    private Dictionary<string, HashSet<Vector2Int>> _roomTilemapsCache = new Dictionary<string, HashSet<Vector2Int>>();

    // 몬스터 레이더 추적용 캐시
    private List<GameObject> _cachedEnemies = new List<GameObject>();
    private float _enemyScanTimer = 0f;
    private const float EnemyScanInterval = 0.15f; // 0.15초마다 몹 목록 갱신

    // 실시간 적 마커 딕셔너리 (Key: 몬스터 GameObject, Value: 생성된 UI 적 마커 RectTransforms)
    private Dictionary<GameObject, List<RectTransform>> _enemyMarkers = new Dictionary<GameObject, List<RectTransform>>();

    // 🌟 실시간 플레이어 마커 RectTransform 캐시 (매 프레임 위치 실시간 추적용)
    private List<RectTransform> _playerMarkers = new List<RectTransform>();

    // 실시간 적 마커들을 꽂아줄 UI 부모 컨테이너 (Refresh할 때마다 초기화)
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
            hudRoomSize = isBattle ? 90f : 20f;     // 전투 중이면 90으로 초대형 확대 (단일 방 집중), 평소엔 20으로 축소
            hudRoomSpacing = isBattle ? 0f : 6f;
            RefreshMap();
        }

        // 2. 경량화 적군 위치 물리 스캔 (0.15초 주기로 씬 내 몹 수집)
        _enemyScanTimer += Time.deltaTime;
        if (_enemyScanTimer >= EnemyScanInterval)
        {
            _enemyScanTimer = 0f;
            ScanRoomEnemies(currentRoom);
        }

        // 3. 🌟 플레이어 및 적군 마커 위치 매 프레임 실시간 레이더 좌표 연산 동기화
        UpdateRealTimeMarkers(currentRoom);
    }

    public void RefreshMap()
    {
        if (MapGenerator.Instance == null) return;

        RoomInstance currentRoom = MapGenerator.Instance.CurrentRoom;
        if (currentRoom == null) return;

        // 마커 및 컨테이너 사전 초기화
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

        // 미니맵 재생성 즉시 1차 스캔 실행하여 몹 도트 즉각 스폰
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

            // 전투 중 단일 방 모드일 때는 현재 방 외에는 스킵
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
            img.sprite = roomSprite;

            Button btn = roomObj.GetComponent<Button>();

            // 🌟 1. 방 고유의 지형 모양 픽셀 사상 (전투 중 줌인 상태일 때 현재 방 내부에만 투과 렌더링)
            if (focusOnlyCurrentRoom && room == currentRoom)
            {
                // 기본 사각형 배경을 보이지 않게 투명화하여 실제 방 지형 모양만 돋보이게 만듭니다.
                img.color = new Color(0f, 0f, 0f, 0f);
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

            // 방문 여부 및 방 종류에 따른 색상 지정 (전투 줌 상태가 아닐 때만 사각형 색 채우기)
            if (!focusOnlyCurrentRoom)
            {
                if (isVisited)
                {
                    switch (room.roomType)
                    {
                        case RoomType.Spawn:
                            img.color = new Color(0.2f, 0.7f, 1f, 1.0f); // 하늘색
                            break;
                        case RoomType.Shop:
                            img.color = new Color(1f, 0.85f, 0.2f, 1.0f); // 노란색
                            break;
                        case RoomType.Reward:
                            img.color = new Color(0.2f, 0.85f, 0.4f, 1.0f); // 초록색
                            break;
                        case RoomType.Boss:
                            img.color = new Color(0.95f, 0.2f, 0.2f, 1.0f); // 붉은색
                            break;
                        case RoomType.Elite:
                            img.color = new Color(0.8f, 0.3f, 0.9f, 1.0f); // 보라색
                            break;
                        default:
                            img.color = new Color(0.35f, 0.45f, 0.65f, 1.0f); // 일반 방: 블루/그레이
                            break;
                    }

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
                    img.color = new Color(0.4f, 0.4f, 0.4f, 0.6f);
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

            // 🌟 2. 플레이어 캐릭터 본래 얼굴 이미지 그대로 얹기 (Color.white 복구로 선명하게 표시!)
            if (room == currentRoom)
            {
                GameObject playerMarker = new GameObject("PlayerMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                playerMarker.transform.SetParent(roomObj.transform, false);
                
                RectTransform pRt = playerMarker.GetComponent<RectTransform>();
                pRt.sizeDelta = new Vector2(roomUiSize * 0.35f, roomUiSize * 0.35f);
                pRt.anchoredPosition = Vector2.zero; // 실시간 위치는 Update에서 매 프레임 갱신합니다.

                Image pImg = playerMarker.GetComponent<Image>();
                if (_playerIcon != null)
                {
                    pImg.sprite = _playerIcon;
                    pImg.color = Color.white; 
                }
                else
                {
                    pImg.color = Color.yellow;
                }

                // 매 프레임 추적하기 위해 리스트 캐싱
                _playerMarkers.Add(pRt);
            }

            // 🌟 3. 적군 마커를 꽂아둘 레이더 컨테이너를 생성하여 저장
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

        // 룸 지형 컨테이너 생성
        GameObject terrainContainer = new GameObject("TerrainContainer", typeof(RectTransform));
        terrainContainer.transform.SetParent(roomObj.transform, false);
        RectTransform containerRt = terrainContainer.GetComponent<RectTransform>();
        containerRt.sizeDelta = new Vector2(roomUiSize, roomUiSize);
        containerRt.anchoredPosition = Vector2.zero;

        // 방의 정규화 가로세로 비율
        float roomW = room.roomSize.x;
        float roomH = room.roomSize.y;
        if (roomW <= 0.1f) roomW = 25f;
        if (roomH <= 0.1f) roomH = 25f;

        // 미니 지형 도트의 스케일 크기 (방 크기 대비 90% 공간 사상)
        float dotSize = (roomUiSize / roomW) * 0.95f; 

        // 캐싱된 모든 타일에 미니 지형 이미지 소환
        foreach (var tilePos in terrainTiles)
        {
            GameObject dotObj = new GameObject("TerrainDot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dotObj.transform.SetParent(terrainContainer.transform, false);

            RectTransform dRt = dotObj.GetComponent<RectTransform>();
            dRt.sizeDelta = new Vector2(dotSize, dotSize);

            // 타일의 물리 상대 좌표를 UI 로컬 비율 좌표로 변사
            float normX = tilePos.x / roomW;
            float normY = tilePos.y / roomH;
            dRt.anchoredPosition = new Vector2(normX * roomUiSize, normY * roomUiSize);

            Image img = dotObj.GetComponent<Image>();
            img.sprite = roomSprite;
            // 은은한 블루 그레이 색으로 타일 지형 형상화
            img.color = new Color(0.25f, 0.4f, 0.6f, 0.75f);
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

    // 🌟 안전한 물리 레이어 스캔 및 실시간 적 마커 동적 갱신(소멸/생성 싱크)
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

        // 🌟 [핵심 추가] 씬 상에서 죽은 적의 UI 마커 즉시 제거
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

        // 🌟 [핵심 추가] 실시간 플레이 중 새로 생성(스폰)된 적의 UI 마커 즉시 동적 소환
        foreach (var enemy in _cachedEnemies)
        {
            if (enemy == null) continue;
            if (!_enemyMarkers.ContainsKey(enemy))
            {
                List<RectTransform> newMarkers = new List<RectTransform>();
                
                // full 및 hud 레이더 컨테이너 모두에 동적 소환
                foreach (var pair in _roomRadarContainers)
                {
                    Transform parentContainer = pair.Value;
                    if (parentContainer == null) continue;

                    GameObject enemyMarker = new GameObject("EnemyMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    enemyMarker.transform.SetParent(parentContainer, false);

                    RectTransform eRt = enemyMarker.GetComponent<RectTransform>();
                    
                    // 스케일 계산용 (부모 UI 사이즈의 15% 크기)
                    float parentSize = parentContainer.parent.GetComponent<RectTransform>().sizeDelta.x;
                    eRt.sizeDelta = new Vector2(parentSize * 0.12f, parentSize * 0.12f);

                    Image eImg = enemyMarker.GetComponent<Image>();
                    eImg.color = Color.red;

                    newMarkers.Add(eRt);
                }
                _enemyMarkers[enemy] = newMarkers;
            }
        }
    }

    // 🌟 플레이어 및 적 몬스터들의 좌표를 매 프레임 실시간 사상하여 미니맵 상에 꼼지락거리며 흐르도록 갱신합니다.
    private void UpdateRealTimeMarkers(RoomInstance currentRoom)
    {
        if (currentRoom == null) return;
        Vector3 roomCenter = currentRoom.transform.position + (Vector3)currentRoom.centerOffset;

        float roomW = currentRoom.roomSize.x;
        float roomH = currentRoom.roomSize.y;
        if (roomW <= 0.1f) roomW = 25f;
        if (roomH <= 0.1f) roomH = 25f;

        // 1. 🌟 플레이어 위치 실시간 스크롤 동기화
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
                }
            }
        }

        // 2. 🌟 적군 위치 실시간 스크롤 동기화
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

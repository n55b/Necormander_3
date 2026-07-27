using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using TMPro;

public class UIBasedMiniMap : Singleton<UIBasedMiniMap>
{

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
    [Tooltip("지형 도트에 곱해지는 색(틴트). 커스텀 스프라이트를 원본 색 그대로 보고 싶으면 흰색(1,1,1,1)으로 두면 됨.")]
    [SerializeField] private Color terrainDotColor = new Color(0.25f, 0.4f, 0.6f, 0.75f); // 기존 하드코딩 값이 기본
    [Tooltip("각 지형 도트를 셀 크기의 이 '배수'로 그려 인접 도트가 겹치게 한다. 1.0=딱 맞닿음(틈 보임), 1.35=35% 겹침(빈틈 없이 꽉 참). 절대 px가 아니라 배수라 큰 방/작은 방 모두 같은 비율로 겹쳐 균일하다. 네모를 더 또렷하게 보고 싶으면 1.2 근처로, 더 꽉 채우려면 키우면 됨.")]
    [SerializeField] private float terrainDotOverlap = 1.35f;

    [Tooltip("전투 HUD 미니맵의 타일 1칸당 픽셀(고정 스케일). 방 크기와 무관하게 이 크기로 그려 빈틈이 없고, 방이 크면 미니맵 전체가 오른쪽 상단 기준으로 커진다.")]
    [SerializeField] private float hudPixelsPerTile = 6f;

    [Tooltip("전투 미니맵의 플레이어/적 마커 크기(타일 칸 수 기준). 지도가 고정 스케일로 커지므로 마커도 화면 비율이 아니라 칸 수로 잡아야 방이 커져도 마커가 같이 커지지 않는다.")]
    [SerializeField] private float hudMarkerTiles = 3f;
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

    // 방별 바닥 타일 '중심'의 방 중심 기준 오프셋 캐시 (방 모양 반영용). 키는 인스턴스 ID.
    private Dictionary<int, List<Vector2>> _roomFloorCache = new Dictionary<int, List<Vector2>>();

    // 전투 미니맵에서 쓸 마커 지름(px). 0이면 비전투 = 기존 방 크기 비율 방식.
    private float _hudMarkerPx = 0f;

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

    protected override void OnAwake()
    {
        
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
            // 전투 HUD는 '고정 스케일'(타일당 hudPixelsPerTile px)로 방 크기대로 그린다 → 빈틈 없고 방이 크면 미니맵도 커짐.
            // 지도가 커져도 마커는 칸 수 기준으로 고정. 개요(비전투)는 기존대로 방 크기 비율.
            _hudMarkerPx = isBattle ? hudPixelsPerTile * hudMarkerTiles : 0f;

            // 전투 시엔 방 크기대로 프레임 밖으로 커질 수 있으니 클리핑 해제, 비전투 개요에선 프레임 안으로 유지.
            var hudMask = hudMapContainer.GetComponent<RectMask2D>();
            if (hudMask != null) hudMask.enabled = !isBattle;
            var hudMaskLegacy = hudMapContainer.GetComponent<Mask>();
            if (hudMaskLegacy != null) hudMaskLegacy.enabled = !isBattle;

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
        // 캔버스에 그려진 방들의 UI 좌표를 누적 캐싱할 임시 딕셔너리
        Dictionary<RoomInstance, Vector2> roomUiPositions = new Dictionary<RoomInstance, Vector2>();

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
            Vector2 drawSize = new Vector2(roomUiSize, roomUiSize);
            rt.sizeDelta = drawSize;

            Vector2 gridDiff = new Vector2(
                room.gridPosition.x - currentRoom.gridPosition.x,
                room.gridPosition.y - currentRoom.gridPosition.y
            );
            rt.anchoredPosition = gridDiff * (roomUiSize + roomUiSpacing);
            
            // 좌표 캐싱 등록
            roomUiPositions[room] = rt.anchoredPosition;

            Image img = roomObj.GetComponent<Image>();
            img.sprite = fallbackRoomSprite;

            Button btn = roomObj.GetComponent<Button>();

            // 🌟 1. 방 모양 지형 그리기 (전투 줌인 시 현재 방 내부에만 지형 투과)
            if (focusOnlyCurrentRoom && room == currentRoom)
            {
                img.color = new Color(0f, 0f, 0f, 0f); // 배경 사각형 투명화

                // [액션 미니맵] 타일당 고정 px로 방 바닥 크기 그대로 그린다 → 방이 크면 미니맵도 그만큼 커진다.
                // 커진 만큼 화면 밖으로 나가지 않도록 피벗/앵커를 오른쪽 상단으로 잡아 왼쪽-아래로만 자라게 한다.
                drawSize = GetFocusRoomUiSize(room);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = drawSize;
                DrawRoomTerrainShape(roomObj, room, drawSize);
            }

            // [수정] 방 프리팹 자식 계층 중 활성화된(activeSelf) MiniMapIcon 스프라이트 정밀 추출
            Sprite roomIconSprite = null;
            Color iconColor = Color.white;

            SpriteRenderer[] childRenderers = room.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in childRenderers)
            {
                if (sr != null && sr.gameObject.activeSelf)
                {
                    string nameLower = sr.gameObject.name.ToLower();
                    if (nameLower.Contains("minimapicon") || nameLower.Contains("minimap_icon"))
                    {
                        roomIconSprite = sr.sprite;
                        iconColor = sr.color;
                        break;
                    }
                }
            }

            // 일반 방은 보상 아이콘(normalRewardType 동기화 스프라이트)을 미니맵에 노출하지 않는다.
            // 여기서 비우면 아래 폴백도 Normal 케이스가 없어 그대로 null 로 남고, 아이콘 그리기 자체가 스킵된다.
            if (room.roomType == RoomType.Normal) roomIconSprite = null;

            if (roomIconSprite == null)
            {
                if (room.roomType == RoomType.Shop) roomIconSprite = _shopIcon;
                else if (room.roomType == RoomType.Reward) roomIconSprite = _rewardIcon;
                else if (room.roomType == RoomType.Spawn) roomIconSprite = _stairIcon;
            }

            // 🌟 2. 인스펙터 커스텀 방 스프라이트 교체 분기 (비워져 있으면 fallback 기본 틴팅 폴백 작동)
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
                CenterOnParent(pRt);
                // 전투 미니맵은 지도가 커져도 마커는 칸 수 기준 고정 크기. 개요는 기존대로 방 칸 비율.
                float pSize = (focusOnlyCurrentRoom && _hudMarkerPx > 0f) ? _hudMarkerPx : roomUiSize * 0.35f;
                pRt.sizeDelta = new Vector2(pSize, pSize);
                pRt.anchoredPosition = Vector2.zero;

                Image pImg = playerMarker.GetComponent<Image>();
                pImg.sprite = customPlayerIcon != null ? customPlayerIcon : _playerIcon;
                pImg.color = Color.white;

                if (syncPlayerZRotation && GameManager.Instance?.PLAYERCONTROLLER != null)
                {
                    float angle = GameManager.Instance.PLAYERCONTROLLER.transform.eulerAngles.z;
                    pRt.localRotation = Quaternion.Euler(0f, 0f, angle);
                }

                _playerMarkers.Add(pRt);
            }

            // 🌟 4. 적군 레이더 컨테이너 등록
            if (room == currentRoom)
            {
                GameObject radarContainer = new GameObject("RadarContainer", typeof(RectTransform));
                radarContainer.transform.SetParent(roomObj.transform, false);
                RectTransform radarRt = radarContainer.GetComponent<RectTransform>();
                CenterOnParent(radarRt);
                radarRt.sizeDelta = drawSize;
                radarRt.anchoredPosition = Vector2.zero;

                string containerKey = isFullMap ? "full" : "hud";
                _roomRadarContainers[containerKey] = radarContainer.transform;
            }
        }

        // [수정] 방들의 UI 캔버스 배치가 모두 마무리된 뒤, 서로 문(복도)으로 연결된 방 노드들 사이에 흰색 UI 실선(Line)들을 드로잉
        HashSet<string> drawnLines = new HashSet<string>();
        foreach (var room in MapGenerator.Instance.AllRooms)
        {
            if (room == null || !roomUiPositions.ContainsKey(room)) continue;

            var connected = MapGenerator.Instance.GetConnectedRooms(room);
            foreach (var conn in connected)
            {
                if (conn == null || !roomUiPositions.ContainsKey(conn)) continue;

                // 양방향 중복 렌더링 방지용 고유 정렬 키 생성
                string lineKey = room.GetInstanceID() < conn.GetInstanceID() 
                    ? $"{room.GetInstanceID()}_{conn.GetInstanceID()}" 
                    : $"{conn.GetInstanceID()}_{room.GetInstanceID()}";

                if (drawnLines.Contains(lineKey)) continue;
                drawnLines.Add(lineKey);

                // 두 방의 UI 중심 좌표
                Vector2 posA = roomUiPositions[room];
                Vector2 posB = roomUiPositions[conn];

                // 복도 선 UI 오브젝트 생성 (부모 캔버스 하위에 배치)
                GameObject lineObj = new GameObject($"Line_{room.name}_{conn.name}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                lineObj.transform.SetParent(container, false);
                lineObj.transform.SetAsFirstSibling(); // 선이 방 마커 뒤로 숨어 예쁘게 표현되도록 최하단 정렬
                spawnedList.Add(lineObj);

                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                Vector2 dir = posB - posA;
                
                // [수정] 방 정중앙을 침범하지 않도록, 양끝 방의 테두리(반지름 2개 합산 = roomUiSize) 크기만큼 선 길이를 축소
                float length = dir.magnitude - roomUiSize;
                if (length <= 0.1f)
                {
                    // 예외적으로 방들이 겹쳐 있거나 너무 붙어 있는 경우 그리지 않음
                    Destroy(lineObj);
                    continue;
                }

                // 선 규격 설정 (양끝 수축된 길이 반영, 두께 3.5f의 깔끔하고 부드러운 흰색 라인)
                lineRt.sizeDelta = new Vector2(length, 3.5f);
                lineRt.anchoredPosition = posA + (dir * 0.5f); // 중간 좌표 안착
                
                // Z회전각 각도 환산
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                lineRt.localRotation = Quaternion.Euler(0f, 0f, angle);

                // 반투명 흰색 단색 이미지 처리
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.sprite = fallbackRoomSprite;
                lineImg.color = new Color(1f, 1f, 1f, 0.7f); // 70% 투명도 흰색선
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
    private void DrawRoomTerrainShape(GameObject roomObj, RoomInstance room, Vector2 uiSize)
    {
        var terrainTiles = GetRoomFloorTiles(room);
        if (terrainTiles.Count == 0) return;

        GameObject terrainContainer = new GameObject("TerrainContainer", typeof(RectTransform));
        terrainContainer.transform.SetParent(roomObj.transform, false);
        RectTransform containerRt = terrainContainer.GetComponent<RectTransform>();
        CenterOnParent(containerRt);
        containerRt.sizeDelta = uiSize;
        containerRt.anchoredPosition = Vector2.zero;

        // 방 크기(벽 포함 roomSize)가 아니라 '실제 바닥 타일 범위'에 꽉 차게 그린다.
        // uiSize = span × hudPixelsPerTile 이므로 cell은 항상 hudPixelsPerTile로 고정된다(방 크기와 무관).
        ComputeFloorLayout(terrainTiles, out Vector2 floorCenter, out Vector2 span);
        float cell = uiSize.x / span.x;

        // 그림자가 도트 사이사이에 껴서 격자처럼 보이지 않도록, 위→아래·왼→오 순서로 그린다.
        // (오른쪽/아래 이웃이 나중에(위에) 그려져 안쪽 그림자를 덮어, 실루엣 바깥 테두리에만 그림자가 남는다.)
        List<Vector2> sortedTiles = new List<Vector2>(terrainTiles);
        sortedTiles.Sort((a, b) => !Mathf.Approximately(a.y, b.y) ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));

        foreach (var tilePos in sortedTiles)
        {
            GameObject dotObj = new GameObject("TerrainDot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dotObj.transform.SetParent(terrainContainer.transform, false);

            RectTransform dRt = dotObj.GetComponent<RectTransform>();
            CenterOnParent(dRt);
            // 도트 크기 = 셀 크기 × 배수 → 인접 도트가 살짝 겹쳐 빈틈 없이 이어진다.
            dRt.sizeDelta = new Vector2(cell * terrainDotOverlap, cell * terrainDotOverlap);
            // 바닥 범위 중심을 컨테이너 정중앙에 맞춰 배치 (도트 간격 = cell).
            dRt.anchoredPosition = (tilePos - floorCenter) * cell;

            Image img = dotObj.GetComponent<Image>();
            img.sprite = (customTerrainDotSprite != null) ? customTerrainDotSprite : fallbackRoomSprite;
            img.color = terrainDotColor; // 인스펙터에서 조절(흰색이면 커스텀 스프라이트 원본색 그대로 노출)

            // 🌟 [입체 그림자 셋팅] useTerrainShadow가 인스펙터에서 켜져 있다면 UI Shadow 컴포넌트 자동 주입
            if (useTerrainShadow)
            {
                Shadow shadow = dotObj.AddComponent<Shadow>();
                shadow.effectColor = terrainShadowColor;
                shadow.effectDistance = terrainShadowOffset;
            }
        }
    }

    /// <summary>방 바닥 타일 '중심'들의 방 중심 기준 월드 오프셋을 캐시해 반환.
    /// 정수로 반올림하지 않는다. CellToWorld는 타일의 코너를, centerOffset은 벽 바운딩박스의 중심을 가리켜
    /// 둘의 차가 항상 정확히 X.5로 떨어지는데, Mathf.RoundToInt는 .5를 짝수로 보내는 은행가 반올림이라
    /// 인접한 두 타일 열이 같은 정수로 뭉개져 바닥의 절반이 사라지고 남은 타일 간격이 2칸으로 벌어졌다.
    /// (벽 너비 47=홀수라 X는 항상, 벽 높이 27~32는 방마다 달라 Y는 방에 따라 붕괴 → "방마다 타일 크기가 달라 보임"의 원인)</summary>
    private List<Vector2> GetRoomFloorTiles(RoomInstance room)
    {
        int cacheKey = room.GetInstanceID(); // 이름은 프리팹끼리 겹칠 수 있어 인스턴스 ID로 캐시
        if (!_roomFloorCache.TryGetValue(cacheKey, out var tiles))
        {
            tiles = new List<Vector2>();
            Tilemap ground = room.groundTilemap; // RoomInstance가 이미 들고 있는 참조 (스탬프 후 비활성이어도 타일 데이터는 남음)
            if (ground != null)
            {
                ground.CompressBounds();
                Vector3 roomCenterWorld = room.transform.position + (Vector3)room.centerOffset;
                foreach (var pos in ground.cellBounds.allPositionsWithin)
                {
                    if (!ground.HasTile(pos)) continue;
                    Vector3 tileCenter = ground.GetCellCenterWorld(pos);
                    tiles.Add(new Vector2(tileCenter.x - roomCenterWorld.x, tileCenter.y - roomCenterWorld.y));
                }
            }
            _roomFloorCache[cacheKey] = tiles;
        }
        return tiles;
    }

    /// <summary>전투 HUD용: 타일당 hudPixelsPerTile px 고정 스케일로 잡은 UI 크기(칸수×픽셀). 방이 크면 미니맵도 그만큼 커진다.</summary>
    private Vector2 GetFocusRoomUiSize(RoomInstance room)
    {
        var tiles = GetRoomFloorTiles(room);
        if (tiles.Count == 0) return new Vector2(hudRoomSize, hudRoomSize);
        ComputeFloorLayout(tiles, out _, out Vector2 span);
        return span * hudPixelsPerTile;
    }

    /// <summary>타일 중심 목록의 중심과 칸 수(가로,세로)를 구한다. 지형 도트와 마커가 같은 기준으로 배치되도록 공용.</summary>
    private void ComputeFloorLayout(List<Vector2> tiles, out Vector2 center, out Vector2 span)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in tiles)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        // 타일 중심 간격이 1이라 (max-min)은 '칸수-1'. +1 해야 실제 칸수가 된다.
        span = new Vector2(Mathf.Max(1f, maxX - minX + 1f), Mathf.Max(1f, maxY - minY + 1f));
    }

    /// <summary>런타임 생성 RectTransform이 부모 정중앙 기준으로 배치되도록 앵커/피벗을 명시한다.
    /// (이 그리기 코드는 전부 '부모 중심 기준 오프셋'으로 좌표를 계산하므로 기본값에 의존하면 안 된다.)</summary>
    private static void CenterOnParent(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private void AddMarkerImage(GameObject parent, Sprite sprite, float size, Color color)
    {
        GameObject imgObj = new GameObject("MarkerImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imgObj.transform.SetParent(parent.transform, false);

        RectTransform rt = imgObj.GetComponent<RectTransform>();
        CenterOnParent(rt);
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
        CenterOnParent(rt);
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

        int targetMask = Layers.EnemyMask;

        Vector3 roomCenter = currentRoom.transform.position + (Vector3)currentRoom.centerOffset;
        Vector2 boxSize = new Vector2(currentRoom.roomSize.x + 3f, currentRoom.roomSize.y + 3f);

        // [근본수정] UpdateRealTimeMarkers의 정규화(-0.5~0.5) 계산에 실제로 쓰이는 방 경계와 동일한 기준으로,
        // 그 범위를 베어난 적(인접 방의 적 등)은 물리 박스에 걸려도 추적 대상에서 아예 제외해,
        // 레이더에 엉뚂한 위치에 마커가 생기는 일 자체를 막습니다.
        float roomHalfW = (currentRoom.roomSize.x <= 0.1f ? 25f : currentRoom.roomSize.x) * 0.5f + 1.5f;
        float roomHalfH = (currentRoom.roomSize.y <= 0.1f ? 25f : currentRoom.roomSize.y) * 0.5f + 1.5f;

        Collider2D[] cols = Physics2D.OverlapBoxAll(roomCenter, boxSize, 0f, targetMask);
        if (cols != null)
        {
            foreach (var col in cols)
            {
                if (col == null) continue;
                GameObject enemyObj = col.gameObject;
                
                if (col.transform.parent != null && col.gameObject.layer == Layers.Enemy)
                {
                    enemyObj = col.transform.root.gameObject;
                }

            Vector3 diffFromCenter = enemyObj.transform.position - roomCenter;
            if (Mathf.Abs(diffFromCenter.x) > roomHalfW || Mathf.Abs(diffFromCenter.y) > roomHalfH) continue;

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
                    CenterOnParent(eRt);

                    // 🌟 [적 마커 스케일 분기] 보스이거나 이름에 Boss가 섞인 강한 적은 마커 크기를 1.8배 확대
                    bool isBoss = enemy.CompareTag("Boss") || enemy.name.Contains("Boss");

                    // 전투 HUD는 지도가 고정 스케일로 커지므로 마커도 칸 수 기준 고정. 개요(full)는 기존 방 크기 비율.
                    float eSize;
                    if (pair.Key == "hud" && _hudMarkerPx > 0f)
                    {
                        eSize = _hudMarkerPx * (isBoss ? 1.0f : 0.5f);
                    }
                    else
                    {
                        float parentSize = parentContainer.parent.GetComponent<RectTransform>().sizeDelta.x;
                        eSize = parentSize * (isBoss ? 0.22f : 0.12f);
                    }
                    eRt.sizeDelta = new Vector2(eSize, eSize);

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

        // 지형 도트와 '같은 기준'(실제 바닥 범위)으로 마커를 얹어야 정확히 정렬된다.
        // 바닥 타일을 못 찾은 방만 방 크기로 폴백.
        var floorTiles = GetRoomFloorTiles(currentRoom);
        Vector2 floorCenter = Vector2.zero;
        Vector2 span;
        if (floorTiles.Count > 0)
        {
            ComputeFloorLayout(floorTiles, out floorCenter, out span);
        }
        else
        {
            span = new Vector2(
                currentRoom.roomSize.x <= 0.1f ? 25f : currentRoom.roomSize.x,
                currentRoom.roomSize.y <= 0.1f ? 25f : currentRoom.roomSize.y);
        }

        // 1. 플레이어 위치 및 회전 각도 실시간 동기화
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            Vector3 playerPos = GameManager.Instance.PLAYERCONTROLLER.transform.position;
            Vector3 pDiff = playerPos - roomCenter;

            float pNormX = (pDiff.x - floorCenter.x) / span.x;
            float pNormY = (pDiff.y - floorCenter.y) / span.y;

            foreach (var pRt in _playerMarkers)
            {
                if (pRt != null && pRt.parent != null)
                {
                    // 전투 HUD에선 parentSize = span × hudPixelsPerTile 이라 결과가 (월드오프셋 × 고정px) = 지형 도트 격자와 정확히 일치.
                    Vector2 parentSize = pRt.parent.GetComponent<RectTransform>().sizeDelta;
                    pRt.anchoredPosition = new Vector2(pNormX * parentSize.x, pNormY * parentSize.y);

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
            float eNormX = (eDiff.x - floorCenter.x) / span.x;
            float eNormY = (eDiff.y - floorCenter.y) / span.y;

            // [수정] 방 전환 직후에 이전 방의 적이 잠시 계속 남아있거나 등 계산이 틀어졌을 때, 마커가 방 타일 밖으로 멀리 튀어나가지 않도록
            // 정규화된 좌표를 방 경계값(-0.5~0.5)로 강제 클램프합니다.
            eNormX = Mathf.Clamp(eNormX, -0.5f, 0.5f);
            eNormY = Mathf.Clamp(eNormY, -0.5f, 0.5f);

            foreach (var markerRt in markerList)
            {
                if (markerRt != null && markerRt.parent != null && markerRt.parent.parent != null)
                {
                    Vector2 parentSize = markerRt.parent.parent.GetComponent<RectTransform>().sizeDelta;
                    markerRt.anchoredPosition = new Vector2(eNormX * parentSize.x, eNormY * parentSize.y);
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

        // 카메라가 부드러운 추적 지연 없이 텔레포트 위치로 즉시 스냅되도록 이동량 계산
        Vector3 teleportCamDelta = targetPos - playerTr.position;

        System.Action doTeleport = () =>
        {
            playerTr.position = targetPos;

            // 텔레포트와 동일한 워프 방식: 카메라를 즉시 스낵시킴
            if (CameraManager.Instance != null)
            {
                Transform camTarget = playerTr.Find("CameraTarget");
                if (camTarget != null)
                {
                    CameraManager.Instance.WarpCamera(camTarget, teleportCamDelta);
                }
            }


            if (MapGenerator.Instance != null)
            {
                MapGenerator.Instance.SetCurrentRoom(room);
            }
            RefreshMap();

            Debug.Log($"<color=green>[Teleport]</color> {room.gameObject.name}의 중심으로 UI 텔레포트 완료!");

            var mapUI = Object.FindFirstObjectByType<MapUIManager>();
            if (mapUI != null) mapUI.CloseMapUI();
        };

        // 화면 페이드(암전) 연출. 양은 여기 박지 않는다 — 문 통과와 똑같은 '방이동'이라
        // ScreenFadeCanvas의 Fader에서 그 줄 하나로 같이 조절된다. 페이더가 없으면 즉시 텔레포트로 폴백.
        Fader fader = Fader.FullScreenFader;
        if (fader != null)
        {
            fader.FadeOutIn(FadeSignal.방이동, doTeleport);
        }
        else
        {
            doTeleport();
        }
    }
}

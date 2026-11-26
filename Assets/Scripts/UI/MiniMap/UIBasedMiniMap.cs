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
    [SerializeField] private RectTransform fullMapBackground;
    [SerializeField] private RectTransform hudMapBackground;

    [Header("방 방문 상태 색상")]
    [SerializeField] private Color visitedRoomColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private Color unvisitedRoomColor = new Color(0.5f, 0.55f, 0.6f, 1f);
    [SerializeField] private Color currentRoomColor = new Color(0.55f, 0.85f, 1f, 1f);

    [Header("전체 지도 (Full Map) 설정")]
    [SerializeField] private float fullRoomSize = 50f;
    [SerializeField] private float fullRoomSpacing = 15f;

    [Header("HUD 미니맵 (HUD Map) 설정")]
    [SerializeField] private float hudRoomSize = 20f;
    [SerializeField] private float hudRoomSpacing = 6f;
    [SerializeField, Min(1f), Tooltip("바닥 지형이 없는 방을 전투 HUD에 표시할 때의 크기입니다.")]
    private float battleHudFallbackSize = 90f;
    [SerializeField, Min(0f), Tooltip("전투 HUD 지형을 프레임 안쪽에 놓는 양쪽 여백의 합입니다.")]
    private float battleHudPadding = 14f;

    [Header("기본 폴백 스프라이트 설정")]
    [SerializeField] private Sprite fallbackRoomSprite;       // 방 기본 흰색 사각형 스프라이트

    [Header("🌟 1. 방 테두리 / 배경 커스텀 에셋 (비워두면 기본 틴팅 폴백 작동)")]
    [SerializeField] private Sprite customNormalRoomSprite;   // 일반 방 전용 오버라이드 스킨
    [SerializeField] private Sprite customBossRoomSprite;     // 보스 방 전용 오버라이드 스킨
    [SerializeField] private Sprite customShopRoomSprite;     // 상점 방 전용 오버라이드 스킨
    [SerializeField] private Sprite customRewardRoomSprite;   // 보상 방 전용 오버라이드 스킨
    [SerializeField] private Sprite customEliteRoomSprite;    // 엘리트 방 전용 오버라이드 스킨
    [Tooltip("증강 선택 방 전용 스킨. 비워두면 일반 방과 같은 네모 위에 빨간 아이콘만 얹힌다.")]
    [SerializeField] private Sprite customAugmentRoomSprite;

    [Header("🌟 2. 지형 도트 커스텀 연출")]
    [SerializeField] private Sprite customTerrainDotSprite;   // 지형 도트용 스프라이트 (비워두면 사각형)
    [Tooltip("지형 도트에 곱해지는 색(틴트). 커스텀 스프라이트를 원본 색 그대로 보고 싶으면 흰색(1,1,1,1)으로 두면 됨.")]
    [SerializeField] private Color terrainDotColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [Tooltip("각 지형 도트를 셀 크기의 이 '배수'로 그려 인접 도트가 겹치게 한다. 1.0=딱 맞닿음(틈 보임), 1.35=35% 겹침(빈틈 없이 꽉 참). 절대 px가 아니라 배수라 큰 방/작은 방 모두 같은 비율로 겹쳐 균일하다. 네모를 더 또렷하게 보고 싶으면 1.2 근처로, 더 꽉 채우려면 키우면 됨.")]
    [SerializeField] private float terrainDotOverlap = 1.35f;

    [Tooltip("전투 HUD 타일 1칸의 최대 UI 크기. 큰 방은 고정된 미니맵 프레임 안에 들어오도록 같은 비율로 축소한다.")]
    [SerializeField] private float hudPixelsPerTile = 6f;

    [Tooltip("전투 미니맵의 플레이어/적 마커 크기(타일 칸 수 기준). 축소 후에도 알아볼 수 있도록 3~7 UI 유닛으로 제한한다.")]
    [SerializeField] private float hudMarkerTiles = 3f;
    [SerializeField] private bool useTerrainShadow = true;     // 2D 입체 그림자 효과 사용 여부
    [SerializeField] private Color terrainShadowColor = new Color(0f, 0f, 0f, 0.6f);
    [SerializeField] private Vector2 terrainShadowOffset = new Vector2(1.5f, -1.5f);

    [Header("🌟 3. 플레이어 / 적군 마커 커스텀 오버라이드")]
    [SerializeField] private Sprite customPlayerIcon;         // 플레이어 마커 이미지 (비워두면 기본 얼굴)
    [SerializeField] private bool syncPlayerZRotation = true; // 플레이어 회전 각도(방향) 동기화 여부
    [SerializeField] private Sprite customEnemyIcon;          // 적 마커 이미지 (비워두면 빨간 도트)
    [SerializeField, Min(0.02f), Tooltip("새 적과 제거된 적을 레이더에 반영하는 주기(초)입니다.")]
    private float enemyScanInterval = 0.15f;
    [SerializeField, Min(0f), Tooltip("방 경계 바깥에서 레이더가 허용할 거리입니다.")]
    private float enemyScanPadding = 1.5f;
    [SerializeField, Min(1f), Tooltip("방 크기가 지정되지 않은 경우 사용할 월드 단위입니다.")]
    private float fallbackRoomWorldSize = 25f;

    // 스프라이트 시트 로드 캐시
    private Sprite _playerIcon;
    private Sprite _shopIcon;
    private Sprite _rewardIcon;
    private Sprite _stairIcon;

    private List<GameObject> _spawnedFullRooms = new List<GameObject>();
    private List<GameObject> _spawnedHudRooms = new List<GameObject>();

    private readonly Dictionary<int, MiniMapFloor> _roomFloorCache = new();
    private MapGenerator floorCacheOwner;

    // 전투 미니맵에서 쓸 마커 지름(px). 0이면 비전투 = 기존 방 크기 비율 방식.
    private float _hudMarkerPx = 0f;

    private readonly List<Collider2D> _radarHits = new();
    private readonly HashSet<GameObject> _cachedEnemies = new();
    private readonly List<GameObject> _removedEnemies = new();
    private float _enemyScanTimer;

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

        HudMotionElement.Attach(hudMapContainer, 0, HudMotionElement.Motion.Pinned);

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
            RefreshMap();
            return;
        }

        // 2. 경량화 적군 위치 물리 스캔
        _enemyScanTimer += Time.deltaTime;
        if (_enemyScanTimer >= Mathf.Max(0.02f, enemyScanInterval))
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

        _lastWasBattle = isBattle;

        // 전체 지도 그리기
        if (fullMapContainer != null)
        {
            ClearContainer(_spawnedFullRooms);
            DrawRoomsOnContainer(fullMapContainer, _spawnedFullRooms, currentRoom, fullRoomSize, fullRoomSpacing, true, false);
        }

        // HUD 미니맵 그리기 (전투 시 단일 방 포커싱 적용)
        if (hudMapContainer != null)
        {
            ClearContainer(_spawnedHudRooms);
            // 전투 HUD는 현재 방 전체를 고정 프레임 안에 맞춘다.
            // 지형이 없는 폴백에도 마커 크기를 제한. 개요(비전투)는 기존 방 크기 비율.
            _hudMarkerPx = isBattle ? Mathf.Clamp(hudPixelsPerTile * hudMarkerTiles, 3f, 7f) : 0f;

            // 1006: 전투 중에도 고정 프레임 밖으로 지도가 넘치지 않는다.
            var hudMask = hudMapContainer.GetComponent<RectMask2D>();
            if (hudMask != null) hudMask.enabled = true;
            var hudMaskLegacy = hudMapContainer.GetComponent<Mask>();
            if (hudMaskLegacy != null) hudMaskLegacy.enabled = true;

            DrawRoomsOnContainer(hudMapContainer, _spawnedHudRooms, currentRoom, hudRoomSize,
                isBattle ? 0f : hudRoomSpacing, false, isBattle);
        }

        ScanRoomEnemies(currentRoom);
        UpdateRealTimeMarkers(currentRoom);
    }

    public void SetHudVisible(bool visible)
    {
        if (hudMapContainer != null) UIOpenMotion.SetVisible(hudMapContainer.gameObject, visible);
    }

    internal void ClearMap()
    {
        ClearContainer(_spawnedFullRooms);
        ClearContainer(_spawnedHudRooms);
        _enemyMarkers.Clear();
        _cachedEnemies.Clear();
        _radarHits.Clear();
        _playerMarkers.Clear();
        _roomRadarContainers.Clear();
        _roomFloorCache.Clear();
        _enemyScanTimer = 0f;
        _lastWasBattle = false;
    }

    private void ClearContainer(List<GameObject> spawnedList)
    {
        foreach (var ui in spawnedList)
        {
            if (ui == null) continue;
            ui.SetActive(false);
            Destroy(ui);
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

            // Spawn 타입이 여러 개여도 현재 방만 시작부터 보인다.
            // 실제 시작 방은 ForceEnter 전 첫 Refresh가 올 수 있어 currentRoom도 방문으로 취급한다.
            bool isVisited = room.hasBeenVisited || room == currentRoom;
            bool isRevealed = isVisited;

            if (!isRevealed)
            {
                var connected = MapGenerator.Instance.GetConnectedRooms(room);
                foreach (var conn in connected)
                {
                    if (conn != null && (conn.hasBeenVisited || conn == currentRoom))
                    {
                        isRevealed = true;
                        break;
                    }
                }
            }

            if (!isRevealed) continue;

            // 방 UI 오브젝트 동적 생성
            GameObject roomObj = new GameObject($"RoomUI_{room.name}", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectorUiImage), typeof(Button));
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
            // HUD/미방문 방의 비활성 버튼 틴트가 방문 상태 색을 흐리지 않게 한다.
            btn.transition = Selectable.Transition.None;

            // 🌟 1. 방 모양 지형 그리기 (전투 줌인 시 현재 방 내부에만 지형 투과)
            if (focusOnlyCurrentRoom && room == currentRoom)
            {
                img.color = new Color(0f, 0f, 0f, 0f); // 배경 사각형 투명화

                // [액션 미니맵] 현재 방 바닥을 프레임 안쪽 크기에 맞춰 가운데 배치한다.
                drawSize = GetFocusRoomUiSize(room);
                CenterOnParent(rt);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = drawSize;
                DrawRoomTerrainShape(roomObj, room, drawSize);
            }

            // 방 프리팹 자식 계층 중 활성화된(activeSelf) MiniMapIcon 스프라이트 정밀 추출
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

            // 일반 방은 미니맵 아이콘을 그리지 않는다(RoomInstance 도 일반 방 아이콘을 켜지 않는다).
            // 여기서 비우면 아래 폴백도 Normal 케이스가 없어 그대로 null 로 남고, 아이콘 그리기 자체가 스킵된다.
            if (room.roomType == RoomType.Normal) roomIconSprite = null;

            if (roomIconSprite == null)
            {
                // 강화 상점도 기존 상점 아이콘을 그대로 쓴다.
                if (room.roomType == RoomType.Shop || room.roomType == RoomType.EnhanceShop) roomIconSprite = _shopIcon;
                else if (room.roomType == RoomType.Reward) roomIconSprite = _rewardIcon;
                else if (room.roomType == RoomType.Spawn) roomIconSprite = _stairIcon;
            }

            // 🌟 2. 인스펙터 커스텀 방 스프라이트 교체 분기 (비워져 있으면 fallback 기본 틴팅 폴백 작동)
            Sprite customRoomSprite = GetCustomRoomSprite(room.roomType);

            if (!focusOnlyCurrentRoom)
            {
                if (customRoomSprite != null)
                {
                    img.sprite = customRoomSprite;
                }
                img.color = room == currentRoom ? currentRoomColor
                    : isVisited ? visitedRoomColor : unvisitedRoomColor;

                if (isFullMap)
                {
                    img.sprite = null;
                    img.color = room == currentRoom ? new Color32(24,121,116,255)
                        : isVisited ? new Color32(105,87,91,255) : new Color32(42,51,49,255);
                    var plate = roomObj.AddComponent<RoundedPlate>();
                    plate.rim = room == currentRoom ? new Color32(126,230,187,255) : new Color32(146,134,118,255);
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
                        AddBossMarker(roomObj, roomUiSize);
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
                        AddBossMarker(roomObj, roomUiSize, 0.4f);
                    }
                }
            }

            // 🌟 3. 플레이어 캐릭터 머리 아이콘 연동
            if (room == currentRoom)
            {
                GameObject playerMarker = new GameObject("PlayerMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectorUiImage));
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

        // 방들의 UI 캔버스 배치가 모두 마무리된 뒤, 서로 문(복도)으로 연결된 방 노드들 사이에 흰색 UI 실선(Line)들을 드로잉
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
                
                // 방 정중앙을 침범하지 않도록, 양끝 방의 테두리(반지름 2개 합산 = roomUiSize) 크기만큼 선 길이를 축소
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
                lineImg.color = isFullMap ? new Color32(176,182,186,205) : new Color(1f, 1f, 1f, 0.7f); // 70% 투명도 흰색선
                lineImg.raycastTarget = false;
            }
        }

        if (isFullMap) FitBackground(container, fullMapBackground, spawnedList);
        else if (hudMapBackground != null) hudMapBackground.SetAsFirstSibling();
    }

    // 프리팹의 배경만 늘린다. 방 위치/배율은 그대로 두고 큰 방과 긴 지도도 덮는다.
    private static void FitBackground(RectTransform container, RectTransform background, List<GameObject> drawnObjects)
    {
        if (background == null) return;

        Bounds bounds = new Bounds(container.rect.center, container.rect.size);
        var corners = new Vector3[4];
        foreach (var obj in drawnObjects)
        {
            if (obj == null) continue;
            var rect = obj.transform as RectTransform;
            if (rect == null) continue;
            rect.GetWorldCorners(corners);
            foreach (var corner in corners)
                bounds.Encapsulate(container.InverseTransformPoint(corner));
        }

        background.anchorMin = background.anchorMax = container.pivot;
        background.pivot = new Vector2(0.5f, 0.5f);
        background.anchoredPosition = bounds.center;
        background.sizeDelta = (Vector2)bounds.size + Vector2.one * 24f;
        // 복도가 SetAsFirstSibling으로 추가되므로 배경은 마지막에 다시 맨 뒤로 보낸다.
        if (background.TryGetComponent<Image>(out var face) && !face.TryGetComponent<RoundedPlate>(out _))
        {
            face.sprite = null;
            face.color = new Color32(42,51,49,212);
            background.gameObject.AddComponent<RoundedPlate>().rim = new Color32(89,102,97,230);
        }
        background.SetAsFirstSibling();
    }

    private Sprite GetCustomRoomSprite(RoomType type)
    {
        switch (type)
        {
            case RoomType.Spawn: return customNormalRoomSprite; // 스폰 방도 기본 일반 형태 활용
            case RoomType.Shop: return customShopRoomSprite;
            // 강화 상점은 지형도 상점과 같아서 스킨을 공유한다. 따로 뜯고 싶으면 필드를 하나 더 만들면 됨.
            case RoomType.EnhanceShop: return customShopRoomSprite;
            case RoomType.Reward: return customRewardRoomSprite;
            case RoomType.Boss: return customBossRoomSprite;
            case RoomType.Elite: return customEliteRoomSprite;
            case RoomType.Augment: return customAugmentRoomSprite;
            default: return customNormalRoomSprite;
        }
    }

    private void DrawRoomTerrainShape(GameObject roomObj, RoomInstance room, Vector2 uiSize)
    {
        MiniMapTerrainGraphic.Draw((RectTransform)roomObj.transform, GetRoomFloor(room), uiSize,
            customTerrainDotSprite != null ? customTerrainDotSprite : fallbackRoomSprite,
            terrainDotOverlap, terrainDotColor, useTerrainShadow ? terrainShadowColor : Color.clear, terrainShadowOffset);
    }

    private MiniMapFloor GetRoomFloor(RoomInstance room)
    {
        if (floorCacheOwner != MapGenerator.Instance)
        {
            _roomFloorCache.Clear();
            floorCacheOwner = MapGenerator.Instance;
        }

        int roomId = room.GetInstanceID();
        if (_roomFloorCache.TryGetValue(roomId, out var floor)) return floor;

        var tiles = new List<Vector2>();
        Tilemap ground = room.groundTilemap;
        if (ground != null)
        {
            ground.CompressBounds();
            Vector3 roomCenter = room.transform.position + (Vector3)room.centerOffset;
            foreach (var cell in ground.cellBounds.allPositionsWithin)
            {
                if (!ground.HasTile(cell)) continue;
                Vector3 center = ground.GetCellCenterWorld(cell);
                // Ground under wall tiles fills transparent artwork and is not walkable terrain.
                if (room.wallTilemap != null && room.wallTilemap.HasTile(room.wallTilemap.WorldToCell(center))) continue;
                tiles.Add(center - roomCenter);
            }
        }

        floor = new MiniMapFloor(tiles);
        _roomFloorCache[roomId] = floor;
        return floor;
    }

    private Vector2 GetFocusRoomUiSize(RoomInstance room)
    {
        var floor = GetRoomFloor(room);
        if (floor.Tiles.Count == 0) return Vector2.one * battleHudFallbackSize;
        Vector2 available = hudMapContainer != null
            ? hudMapContainer.rect.size - Vector2.one * battleHudPadding
            : Vector2.one * battleHudFallbackSize;
        float cell = Mathf.Max(0.01f, Mathf.Min(hudPixelsPerTile, available.x / floor.Span.x, available.y / floor.Span.y));
        _hudMarkerPx = Mathf.Clamp(cell * hudMarkerTiles, 3f, 7f);
        return floor.Span * cell;
    }

    /// <summary>런타임 생성 RectTransform이 부모 정중앙 기준으로 배치되도록 앵커/피벗을 명시한다.
    /// (이 그리기 코드는 전부 '부모 중심 기준 오프셋'으로 좌표를 계산하므로 기본값에 의존하면 안 된다.)</summary>
    private static void CenterOnParent(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private void AddMarkerImage(GameObject parent, Sprite sprite, float size, Color color)
    {
        GameObject imgObj = new GameObject("MarkerImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectorUiImage));
        imgObj.transform.SetParent(parent.transform, false);

        RectTransform rt = imgObj.GetComponent<RectTransform>();
        CenterOnParent(rt);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;

        Image img = imgObj.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
    }

    private void AddBossMarker(GameObject parent, float roomUiSize, float opacity = 1f)
    {
        GameObject textObj = new GameObject("SymbolText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(parent.transform, false);

        RectTransform rt = textObj.GetComponent<RectTransform>();
        CenterOnParent(rt);
        rt.sizeDelta = new Vector2(roomUiSize, roomUiSize);
        rt.anchoredPosition = Vector2.zero;

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = "B";
        PixelText.Configure(tmp, 8);
        tmp.raycastTarget = false;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, opacity);
    }

    private void ScanRoomEnemies(RoomInstance currentRoom)
    {
        _cachedEnemies.Clear();

        Vector2 roomCenter = currentRoom.transform.position + (Vector3)currentRoom.centerOffset;
        Vector2 boxSize = GetRoomWorldSize(currentRoom) + Vector2.one * (2f * enemyScanPadding);
        Vector2 halfSize = boxSize * 0.5f;
        var filter = new ContactFilter2D { useTriggers = Physics2D.queriesHitTriggers };
        filter.SetLayerMask(Layers.EnemyMask);
        Physics2D.OverlapBox(roomCenter, boxSize, 0f, filter, _radarHits);
        foreach (var hit in _radarHits)
        {
            var enemy = SkillCombatUtil.ResolveEntityTransform(hit);
            // The Enemy layer also includes destructible traps.
            if (enemy == null || !enemy.TryGetComponent<BaseEntity>(out _)) continue;
            Vector2 offset = (Vector2)enemy.position - roomCenter;
            if (Mathf.Abs(offset.x) <= halfSize.x && Mathf.Abs(offset.y) <= halfSize.y)
                _cachedEnemies.Add(enemy.gameObject);
        }

        foreach (var enemy in _enemyMarkers.Keys)
            if (enemy == null || !_cachedEnemies.Contains(enemy)) _removedEnemies.Add(enemy);

        foreach (var enemy in _removedEnemies)
        {
            foreach (var marker in _enemyMarkers[enemy])
                if (marker != null) Destroy(marker.gameObject);
            _enemyMarkers.Remove(enemy);
        }
        _removedEnemies.Clear();

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

                    GameObject enemyMarker = new GameObject("EnemyMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectorUiImage));
                    enemyMarker.transform.SetParent(parentContainer, false);

                    RectTransform eRt = enemyMarker.GetComponent<RectTransform>();
                    CenterOnParent(eRt);

                    // 🌟 [적 마커 스케일 분기] 일반 몹이 아닌 적(엘리트/보스)은 마커를 크게 그린다.
                    // 예전엔 태그/이름으로 봐서 'Summoner Boss Minions'(일반 몹)까지 커졌다.
                    var enemyStatus = enemy.GetComponentInChildren<CharacterStatus>(true);
                    bool isBoss = enemyStatus != null && enemyStatus.Tier != EnemyTier.Normal;

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

    private Vector2 GetRoomWorldSize(RoomInstance room) => new(
        room.roomSize.x > 0 ? room.roomSize.x : fallbackRoomWorldSize,
        room.roomSize.y > 0 ? room.roomSize.y : fallbackRoomWorldSize);

    private void UpdateRealTimeMarkers(RoomInstance currentRoom)
    {
        if (currentRoom == null) return;
        Vector3 roomCenter = currentRoom.transform.position + (Vector3)currentRoom.centerOffset;

        var floor = GetRoomFloor(currentRoom);
        Vector2 floorCenter = floor.Center;
        Vector2 span = floor.Tiles.Count > 0 ? floor.Span : GetRoomWorldSize(currentRoom);

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
                    // 프레임 크기에 맞춰 축소된 지형 도트와 동일한 좌표계다.
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

            // 방 전환 직후에 이전 방의 적이 잠시 계속 남아있거나 등 계산이 틀어졌을 때, 마커가 방 타일 밖으로 멀리 튀어나가지 않도록
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

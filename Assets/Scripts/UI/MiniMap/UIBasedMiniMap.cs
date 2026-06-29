using UnityEngine;
using UnityEngine.UI;
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

    private List<GameObject> _spawnedFullRooms = new List<GameObject>();
    private List<GameObject> _spawnedHudRooms = new List<GameObject>();

    private void Awake()
    {
        Instance = this;
        
        // 씬 상의 자식 오브젝트들을 자동으로 찾아서 연결하는 방어용 초기화 로직
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
    }

    public void RefreshMap()
    {
        if (MapGenerator.Instance == null) return;

        // 1. 플레이어의 현재 방 탐색
        RoomInstance currentRoom = MapGenerator.Instance.CurrentRoom;
        if (currentRoom == null) return;

        // 2. 전체 지도 그리기
        if (fullMapContainer != null)
        {
            ClearContainer(ref _spawnedFullRooms);
            DrawRoomsOnContainer(fullMapContainer, _spawnedFullRooms, currentRoom, fullRoomSize, fullRoomSpacing, true);
        }

        // 3. HUD 미니맵 그리기
        if (hudMapContainer != null)
        {
            ClearContainer(ref _spawnedHudRooms);
            DrawRoomsOnContainer(hudMapContainer, _spawnedHudRooms, currentRoom, hudRoomSize, hudRoomSpacing, false);
        }
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
        bool isFullMap)
    {
        foreach (var room in MapGenerator.Instance.AllRooms)
        {
            if (room == null) continue;

            // 방의 미니맵 공개 여부 판별 (방문했거나, 이웃 방이 방문 완료되었거나)
            bool isVisited = room.hasBeenVisited || room.roomType == RoomType.Spawn;
            bool isRevealed = isVisited;

            if (!isRevealed)
            {
                // 인접한 방이 방문되었는지 검사
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

            // 아예 미개방 상태면 미니맵에 표시조차 안 함
            if (!isRevealed) continue;

            // 방 UI 오브젝트 동적 생성
            GameObject roomObj = new GameObject($"RoomUI_{room.name}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            roomObj.transform.SetParent(container, false);
            spawnedList.Add(roomObj);

            RectTransform rt = roomObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(roomUiSize, roomUiSize);

            // 현재 플레이어가 있는 방을 컨테이너 중앙 (0,0) 에 고정시킵니다.
            Vector2 gridDiff = new Vector2(
                room.gridPosition.x - currentRoom.gridPosition.x,
                room.gridPosition.y - currentRoom.gridPosition.y
            );
            rt.anchoredPosition = gridDiff * (roomUiSize + roomUiSpacing);

            Image img = roomObj.GetComponent<Image>();
            img.sprite = roomSprite;

            Button btn = roomObj.GetComponent<Button>();

            // 방 비주얼 장식 (방문 여부 및 방 종류에 따른 색상 지정)
            if (isVisited)
            {
                // 방문 완료: 화사한 풀 컬러 지정
                switch (room.roomType)
                {
                    case RoomType.Spawn:
                        img.color = new Color(0.2f, 0.7f, 1f, 1f); // 하늘색
                        AddMarkerText(roomObj, "S", roomUiSize);
                        break;
                    case RoomType.Shop:
                        img.color = new Color(1f, 0.85f, 0.2f, 1f); // 노란색
                        AddMarkerText(roomObj, "$", roomUiSize);
                        break;
                    case RoomType.Reward:
                        img.color = new Color(0.2f, 0.85f, 0.4f, 1f); // 초록색
                        AddMarkerText(roomObj, "🎁", roomUiSize);
                        break;
                    case RoomType.Boss:
                        img.color = new Color(0.95f, 0.2f, 0.2f, 1f); // 붉은색
                        AddMarkerText(roomObj, "☠", roomUiSize);
                        break;
                    case RoomType.Elite:
                        img.color = new Color(0.8f, 0.3f, 0.9f, 1f); // 보라색
                        AddMarkerText(roomObj, "E", roomUiSize);
                        break;
                    default:
                        img.color = new Color(0.35f, 0.45f, 0.65f, 1f); // 일반 방: 블루/그레이
                        break;
                }

                // 방문한 방이고 전체지도일 때만 클릭 텔레포트 가능하도록 셋업
                if (isFullMap)
                {
                    btn.interactable = true;
                    btn.onClick.AddListener(() =>
                    {
                        TeleportToRoom(room);
                    });
                }
                else
                {
                    btn.interactable = false; // HUD 작은 지도는 클릭 방지
                }
            }
            else
            {
                // 발견만 된 방: 어두운 투명 회색 실루엣 묘사
                img.color = new Color(0.4f, 0.4f, 0.4f, 0.6f);
                btn.interactable = false; // 안 가본 방 클릭 금지
                
                // 특수 방의 힌트 아이콘을 텍스트로 미리 보여줌 (아이작 스타일!)
                switch (room.roomType)
                {
                    case RoomType.Shop:
                        AddMarkerText(roomObj, "$", roomUiSize, 0.4f);
                        break;
                    case RoomType.Reward:
                        AddMarkerText(roomObj, "🎁", roomUiSize, 0.4f);
                        break;
                    case RoomType.Boss:
                        AddMarkerText(roomObj, "☠", roomUiSize, 0.4f);
                        break;
                }
            }

            // 플레이어가 서 있는 현재 방 위에 노란색 플레이어 마커 얹기
            if (room == currentRoom)
            {
                GameObject playerMarker = new GameObject("PlayerMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                playerMarker.transform.SetParent(roomObj.transform, false);
                
                RectTransform pRt = playerMarker.GetComponent<RectTransform>();
                pRt.sizeDelta = new Vector2(roomUiSize * 0.4f, roomUiSize * 0.4f); // 방 크기의 40% 크기
                pRt.anchoredPosition = Vector2.zero; // 방 중앙에 고정

                Image pImg = playerMarker.GetComponent<Image>();
                pImg.color = Color.yellow; // 번쩍이는 노란색 점
            }
        }
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
        tmp.fontSize = roomUiSize * 0.5f; // 글자 크기 보정
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, opacity);
    }

    private void TeleportToRoom(RoomInstance room)
    {
        if (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;
        if (GameManager.Instance.PLAYERCONTROLLER.GetPlayerState() == PlayerStates.Battle) return;

        // 플레이어 텔레포트 실행
        Transform playerTr = GameManager.Instance.PLAYERCONTROLLER.transform;
        Vector3 targetPos = room.transform.position + (Vector3)room.centerOffset;
        targetPos.z = playerTr.position.z;
        playerTr.position = targetPos;

        // 🌟 순간이동 즉시 현재 위치 방 정보를 갱신하고 미니맵을 실시간으로 다시 그립니다.
        if (MapGenerator.Instance != null)
        {
            MapGenerator.Instance.SetCurrentRoom(room);
        }
        RefreshMap();

        Debug.Log($"<color=green>[Teleport]</color> {room.gameObject.name}의 중심으로 UI 텔레포트 완료!");

        // 맵 UI 윈도우 닫기
        var mapUI = Object.FindFirstObjectByType<MapUIManager>();
        if (mapUI != null) mapUI.CloseMapUI();
    }
}

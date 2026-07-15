using UnityEngine;

/// <summary>
/// 룸 앵커에 부착되어 아이작 스타일의 순간이동 트리거 및 카메라 워프를 처리하는 컨트롤러입니다.
/// </summary>
public class DoorController : MonoBehaviour
{
    private BoxCollider2D _teleportTrigger;
    private DoorController _destinationDoor;
    private Vector2Int _roomEntranceDirection; // 순간이동 후 플레이어가 안착할 방 내부 방향
    private static bool _isTeleporting = false;
    private bool _isEnabled = false;

    private void EnsureTriggerCollider()
    {
        if (_teleportTrigger == null)
        {
            _teleportTrigger = gameObject.AddComponent<BoxCollider2D>();
            _teleportTrigger.isTrigger = true;
            
            // 문 방향에 맞추어 트리거 감지 영역 넓히기 (벽 뚫린 3칸 영역 전체를 커버하여 공허 탈출 예방)
            if (_roomEntranceDirection.y != 0) // Up/Down 이동 -> 가로로 긴 트리거
            {
                _teleportTrigger.size = new Vector2(4.5f, 1.5f);
            }
            else if (_roomEntranceDirection.x != 0) // Left/Right 이동 -> 세로로 긴 트리거
            {
                _teleportTrigger.size = new Vector2(1.5f, 4.5f);
            }
            else
            {
                _teleportTrigger.size = new Vector2(1.5f, 1.5f); // 기본값
            }
            
            _teleportTrigger.enabled = _isEnabled;
        }
        else
        {
            // 이미 생성되어 있는 경우에도 방향에 맞게 크기 동적 갱신
            if (_roomEntranceDirection.y != 0)
            {
                _teleportTrigger.size = new Vector2(4.5f, 1.5f);
            }
            else if (_roomEntranceDirection.x != 0)
            {
                _teleportTrigger.size = new Vector2(1.5f, 4.5f);
            }
        }
    }

    public void SetTriggerEnabled(bool enabled)
    {
        _isEnabled = enabled;
        EnsureTriggerCollider();
        if (_teleportTrigger != null)
        {
            _teleportTrigger.enabled = enabled;
        }
    }

    public void SetupTeleport(DoorController destination, Vector2Int entranceDir)
    {
        _destinationDoor = destination;
        _roomEntranceDirection = entranceDir;
        EnsureTriggerCollider();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_isEnabled || _isTeleporting) return;

        if (!other.CompareTag("Player")) return;

        PlayerController player = other.GetComponentInParent<PlayerController>() ?? other.GetComponent<PlayerController>();
        if (player == null || _destinationDoor == null) return;

        _isTeleporting = true;
        player.SetInputBlocked(true);

        // 문은 양(시간/색)을 안 들고 있다. '방이동' 이라는 신호 이름만 넘기면
        // 실제 수치는 ScreenFadeCanvas의 Fader 반응 목록에서 '방이동' 줄을 찾아 쓴다 → 기획자가 거기서 조절.
        //
        // 텔레포트는 '완전히 깜깜해진 순간'에 끼워넣는다 = 화면이 가려진 동안 순간이동해서 뚝 끊기는 게 안 보인다.
        // 이 페이드는 커튼(DontDestroyOnLoad) 위에서 돌기 때문에, 이 문/방이 도중에 파괴돼도
        // 화면이 검은 채로 굳지 않는다. (예전엔 이 코루틴이 문과 함께 죽으면 암전인 채 멈췄다.)
        Fader fader = Fader.FullScreenFader;
        if (fader == null)
        {
            // 페이더를 못 구하면 연출만 생략하고 이동은 정상 수행 (검은 화면에 갇히는 것보다 낫다)
            DoTeleport(player);
            FinishTeleport(player);
            return;
        }

        fader.FadeOutIn(FadeSignal.방이동, () => DoTeleport(player), () => FinishTeleport(player));
    }

    /// <summary>암전된 동안 실행: 플레이어 순간이동 + 카메라 워프 + 미니맵 초점 이동.</summary>
    private void DoTeleport(PlayerController player)
    {
        if (player == null || _destinationDoor == null) return;

        Vector3 prevPos = player.transform.position;

        // 목적지 앵커 위치에서 방 안쪽 방향(_roomEntranceDirection)으로 2.5유닛 정도 띈 좌표를 목적지로 설정
        Vector3 destAnchorPos = _destinationDoor.transform.position;
        Vector3 spawnOffset = new Vector3(_roomEntranceDirection.x, _roomEntranceDirection.y, 0) * 2.5f;
        Vector3 targetWorldPos = destAnchorPos + spawnOffset;

        player.transform.position = targetWorldPos;

        // 카메라 즉시 이동 (Warp)
        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.WarpCamera(player.transform, targetWorldPos - prevPos);
        }

        // 목적지 방의 촘촘한 미니맵 좌표로 카메라 초점 실시간 정렬
        RoomInstance targetRoom = _destinationDoor.GetComponentInParent<RoomInstance>();
        if (targetRoom != null && MapGenerator.Instance != null)
        {
            MapGenerator.Instance.UpdateMiniMapCameraFocus(targetRoom);
        }
    }

    /// <summary>페이드 인까지 끝난 뒤: 조작 복구.</summary>
    private void FinishTeleport(PlayerController player)
    {
        if (player != null) player.SetInputBlocked(false);
        _isTeleporting = false;
    }
}

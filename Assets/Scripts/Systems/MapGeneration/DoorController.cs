using System.Collections;
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
            _teleportTrigger.size = new Vector2(1.5f, 1.5f); // 앵커 지점 기준 적절한 감지 영역
            _teleportTrigger.enabled = _isEnabled;
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

        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponentInParent<PlayerController>() ?? other.GetComponent<PlayerController>();
            if (player != null && _destinationDoor != null)
            {
                StartCoroutine(TeleportSequence(player));
            }
        }
    }

    private IEnumerator TeleportSequence(PlayerController player)
    {
        _isTeleporting = true;
        player.SetInputBlocked(true);

        // 페이드 아웃 연출
        float fadeTime = 0.2f;
        CanvasGroup fadeCanvas = CreateFadeCanvas();
        if (fadeCanvas != null)
        {
            float elapsed = 0f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.unscaledDeltaTime;
                fadeCanvas.alpha = Mathf.Clamp01(elapsed / fadeTime);
                yield return null;
            }
            fadeCanvas.alpha = 1f;
        }

        // 플레이어 순간이동 전 위치 저장
        Vector3 prevPos = player.transform.position;

        // 목적지 앵커 위치 계산 및 플레이어 텔레포트
        // 목적지 앵커 위치에서 방 안쪽 방향(_roomEntranceDirection)으로 2.5유닛 정도 띈 좌표를 목적지로 설정
        Vector3 destAnchorPos = _destinationDoor.transform.position;
        Vector3 spawnOffset = new Vector3(_roomEntranceDirection.x, _roomEntranceDirection.y, 0) * 2.5f;
        Vector3 targetWorldPos = destAnchorPos + spawnOffset;

        // 플레이어 트랜스폼 순간이동
        player.transform.position = targetWorldPos;

        // 카메라 즉시 이동 (Warp)
        if (CameraManager.Instance != null)
        {
            Vector3 delta = targetWorldPos - prevPos;
            CameraManager.Instance.WarpCamera(player.transform, delta);
        }

        // 목적지 방의 촘촘한 미니맵 좌표로 카메라 초점 실시간 정렬
        RoomInstance targetRoom = _destinationDoor.GetComponentInParent<RoomInstance>();
        if (targetRoom != null && MapGenerator.Instance != null)
        {
            MapGenerator.Instance.UpdateMiniMapCameraFocus(targetRoom);
        }

        yield return new WaitForSecondsRealtime(0.1f);

        // 페이드 인 연출
        if (fadeCanvas != null)
        {
            float elapsed = 0f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.unscaledDeltaTime;
                fadeCanvas.alpha = Mathf.Clamp01(1f - (elapsed / fadeTime));
                yield return null;
            }
            Destroy(fadeCanvas.gameObject);
        }

        player.SetInputBlocked(false);
        _isTeleporting = false;
    }

    private CanvasGroup CreateFadeCanvas()
    {
        GameObject canvasObj = new GameObject("TeleportFadeCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        CanvasGroup group = canvasObj.AddComponent<CanvasGroup>();

        GameObject panel = new GameObject("FadeImage");
        panel.transform.SetParent(canvasObj.transform, false);
        var image = panel.AddComponent<UnityEngine.UI.Image>();
        image.color = Color.black;

        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        group.alpha = 0f;
        return group;
    }
}

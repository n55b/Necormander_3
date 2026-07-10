using UnityEngine;
using UnityEngine.AI;
using NavMeshPlus.Components;
using System.Collections;

/// <summary>
/// 연습(테스트)씬 전용 스크립트입니다.
/// 이 씬은 MapGenerator(절차적 생성)를 사용하지 않고, 씬에 미리 배치해 둔
/// Room_Spawn(Test) / Room_Elite(Test) 프리팹 두 개만으로 구성됩니다.
///
/// 흐름:
/// 1) GameManager가 플레이어를 스폰할 때까지 대기
/// 2) 두 방을 초기화하고, 물리 바디를 고정(CleanupPhysics)하고, NavMesh를 다시 굽는다
///    (평소 MapGenerator가 대신 해주던 작업들)
/// 3) 스폰방 중앙에 플레이어 배치
/// 4) 잠시 후 넓은 Room_Elite의 중앙으로 플레이어를 이동 (12시 방향 DoorAnchor를 통과하는 연출)
/// 5) 3초 대기 후 테스트할 엘리트 몬스터 소환
///
/// [주의 1] DoorAnchor는 실제로는 "복도가 시작되는 지점"을 표시할 뿐, 방 타일 내부 좌표가
/// 아닙니다. 이 씬에는 두 방을 잇는 복도가 실제로 존재하지 않으므로, 착지 지점은 항상 방의
/// 실제 중앙(RoomInstance.centerOffset 기준)을 사용합니다.
///
/// [주의 2] RoomInstance.Initialize()가 만드는 Rigidbody2D는 기본적으로 Dynamic이라,
/// MapGenerator가 없는 이 씬에서는 플레이어/몬스터와 충돌하며 방 자체가 밀려날 수 있습니다.
/// (기둥이 벽에 붙어 보이거나 플레이어가 엉뚱한 곳에 떨어지는 것처럼 보이는 원인)
/// 그래서 Initialize() 직후 CleanupPhysics()를 호출해 방을 완전히 고정시킵니다.
/// </summary>
public class EliteTestSpawner : MonoBehaviour
{
    [Header("소환할 테스트 몬스터")]
    [SerializeField] private GameObject eliteMonsterPrefab;

    [Header("연습씬 방 오브젝트 (씬에 미리 배치해둔 Room_Spawn / Room_Elite)")]
    [SerializeField] private RoomInstance spawnRoom;
    [SerializeField] private RoomInstance eliteRoom;

    [Header("타이밍")]
    [Tooltip("플레이어 스폰 완료 후, 엘리트 방으로 이동하기 전 대기 시간")]
    [SerializeField] private float delayBeforeMove = 1f;
    [Tooltip("엘리트 방 도착 후 보스 스폰까지 대기 시간 (기획 요청: 3초)")]
    [SerializeField] private float delayBeforeBossSpawn = 3f;

    private Vector3 _spawnRoomCenter;
    private Vector3 _eliteRoomCenter;

    private IEnumerator Start()
    {
        // 1) 플레이어 스폰 대기
        while (GameManager.Instance == null || !GameManager.Instance.IsPlayerReady)
        {
            yield return null;
        }

        PlayerController player = GameManager.Instance.PLAYERCONTROLLER;
        if (player == null)
        {
            Debug.LogError("<color=red>[EliteTestSpawner]</color> 플레이어를 찾을 수 없습니다.");
            yield break;
        }

        // 2) 방 초기화 + 물리 바디 고정 (평소 MapGenerator가 대신 해주던 작업을 이 씬에서는 직접 수행)
        if (spawnRoom != null)
        {
            spawnRoom.Initialize(RoomType.Spawn);
            spawnRoom.CleanupPhysics(); // Dynamic Rigidbody2D가 충돌로 밀려나 방이 움직이는 것을 방지
            _spawnRoomCenter = spawnRoom.transform.position + (Vector3)spawnRoom.centerOffset;
        }
        if (eliteRoom != null)
        {
            eliteRoom.Initialize(RoomType.Elite);
            eliteRoom.CleanupPhysics();
            _eliteRoomCenter = eliteRoom.transform.position + (Vector3)eliteRoom.centerOffset;
        }

        // 이 씬에는 MapGenerator가 없으므로, 두 방의 타일맵을 반영해 NavMesh를 직접 다시 굽습니다.
        NavMeshSurface navSurface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (navSurface != null)
        {
            navSurface.RemoveData();
            navSurface.BuildNavMesh();
        }
        else
        {
            Debug.LogWarning("<color=yellow>[EliteTestSpawner]</color> NavMeshSurface를 찾지 못했습니다. 몬스터가 이동하지 못할 수 있습니다.");
        }

        yield return null; // 콜라이더/NavMesh 세팅이 한 프레임 안정화되도록 대기

        // 3) 스폰방 중앙에 플레이어 배치
        if (spawnRoom != null)
        {
            player.transform.position = _spawnRoomCenter;
            spawnRoom.ForceEnter();
        }

        yield return new WaitForSeconds(delayBeforeMove);

        // 4) 엘리트 방(넓은 공간) 중앙으로 이동
        if (eliteRoom != null)
        {
            yield return StartCoroutine(MoveToRoom(player, eliteRoom, _eliteRoomCenter));
        }

        // 5) 도착 3초 후 보스 소환
        yield return new WaitForSeconds(delayBeforeBossSpawn);

        SpawnElite(eliteRoom != null ? _eliteRoomCenter : _spawnRoomCenter);
    }

    /// <summary>
    /// 실제 DoorController 텔레포트처럼 카메라 워프와 방 진입 연출(ForceEnter)을 적용하며
    /// 플레이어를 목표 좌표(방 실제 중앙, 미리 캐싱해 둔 값)로 이동시킵니다.
    /// </summary>
    private IEnumerator MoveToRoom(PlayerController player, RoomInstance to, Vector3 targetPos)
    {
        player.SetInputBlocked(true);

        Vector3 prevPos = player.transform.position;
        player.transform.position = targetPos;

        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.WarpCamera(player.transform, targetPos - prevPos);
        }

        to.ForceEnter();

        yield return null;

        player.SetInputBlocked(false);
    }

    private void SpawnElite(Vector3 spawnPos)
    {
        if (eliteMonsterPrefab == null)
        {
            Debug.LogWarning("<color=yellow>[EliteTestSpawner]</color> eliteMonsterPrefab이 비어있습니다. 인스펙터에서 연결해주세요.");
            return;
        }

        if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 6f, NavMesh.AllAreas))
        {
            spawnPos = hit.position;
        }

        Instantiate(eliteMonsterPrefab, spawnPos, Quaternion.identity);
        Debug.Log("<color=magenta>[EliteTestSpawner]</color> 테스트용 엘리트 몬스터 소환 완료: " + spawnPos);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine; // [추가] 시네머신 연동용

/// <summary>
/// 게임의 전체 생명주기와 매니저들의 초기화 순서를 관리하는 중앙 컨트롤러입니다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab; // [추가] 플레이어 프리팹
    [SerializeField] private PlayerController playerController;
    public PlayerController PLAYERCONTROLLER => playerController;

    [Header("Core Managers")]
    [SerializeField] public DataManager dataManager;
    [SerializeField] public EconomyManager economyManager;
    [SerializeField] public ThrowImpactManager throwImpactManager;
    [SerializeField] public MouseManager mouseManager;
    [SerializeField] public MouseCursorManager mouseCursorManager;
    
    [Header("Growth System")]
    [SerializeField] public InventoryManager inventoryManager;
    [SerializeField] public SquadSpawner squadSpawner;
    [SerializeField] public RewardManager rewardManager;

    [Header("UI References")]
    [SerializeField] public PlayerStateUI playerStateUI;

    [Header("Map Generation")]
    [SerializeField] public MapGenerator mapGenerator; 
    [SerializeField] private MapGenerationDataSO currentStageMapData;
    [SerializeField] private RoomPrefabDataSO currentStageRoomData;

    private bool _isTimeStopped = false;
    public bool IsTimeStopped => _isTimeStopped;

    public void SetTimeStop(bool stop)
    {
        _isTimeStopped = stop;
        Time.timeScale = stop ? 0f : 1f;
        Debug.Log($"<color=yellow>[TimeSystem]</color> Time Scale set to: {Time.timeScale}");
    }

    public void TimeStopTimer(float duration)
    {
        StartCoroutine(TimeStopCoroutine(duration));
    }

    private IEnumerator TimeStopCoroutine(float duration)
    {
        SetTimeStop(true);
        yield return new WaitForSecondsRealtime(duration);
        SetTimeStop(false);
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        InitializeGame();
    }

    private void InitializeGame()
    {
        Debug.Log("<b>[GameManager]</b> Starting Initialization Sequence...");

        if (dataManager == null) dataManager = GetComponentInChildren<DataManager>();
        if (economyManager == null) economyManager = GetComponentInChildren<EconomyManager>();
        if (throwImpactManager == null) throwImpactManager = GetComponentInChildren<ThrowImpactManager>();
        if (mouseManager == null) mouseManager = GetComponentInChildren<MouseManager>();
        if (mouseCursorManager == null) mouseCursorManager = GetComponentInChildren<MouseCursorManager>();
        if (inventoryManager == null) inventoryManager = GetComponentInChildren<InventoryManager>();
        if (squadSpawner == null) squadSpawner = GetComponentInChildren<SquadSpawner>();
        if (rewardManager == null) rewardManager = GetComponentInChildren<RewardManager>();

        if (dataManager != null) dataManager.Initialize();
        if (inventoryManager != null) inventoryManager.Initialize();
        if (economyManager != null) economyManager.Initialize();
        if (throwImpactManager != null) throwImpactManager.Initialize();
        if (rewardManager != null) rewardManager.Initialize();
        
        if (squadSpawner != null)
        {
            var allyManager = Object.FindFirstObjectByType<AllyManager>();
            squadSpawner.Initialize(inventoryManager, allyManager);
        }

        Debug.Log("<b>[GameManager]</b> Initial Managers Loaded.");
    }

    private IEnumerator Start()
    {
        if (mapGenerator == null)
            mapGenerator = Object.FindFirstObjectByType<MapGenerator>();

        if (mapGenerator != null && currentStageMapData != null && currentStageRoomData != null)
        {
            Debug.Log("<color=cyan>[GameManager]</color> Injecting Map Data and Waiting for Generation...");
            mapGenerator.SetMapData(currentStageMapData, currentStageRoomData);
            yield return StartCoroutine(mapGenerator.GenerateMapCoroutine());
        }
        else if (mapGenerator != null)
        {
             Debug.LogWarning("[GameManager] Stage Data is missing! Using MapGenerator's default data.");
             yield return StartCoroutine(mapGenerator.GenerateMapCoroutine());
        }

        // [핵심 추가] 맵 생성 완료 후 플레이어 동적 스폰
        SpawnPlayer();

        // 플레이어 HUD 초기화 (스폰된 플레이어의 Health 참조 확보)
        if (playerStateUI != null && playerController != null)
        {
            var health = playerController.GetComponentInChildren<CharacterHealth>();
            var allyManager = Object.FindFirstObjectByType<AllyManager>();
            playerStateUI.Initialize(health, allyManager);
            Debug.Log("<color=cyan>[GameManager]</color> Player HUD Initialized.");
        }

        Debug.Log("<color=green>[GameManager]</color> All Systems Ready!");
    }

    private void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[GameManager] Player Prefab이 할당되지 않았습니다!");
            return;
        }

        GameObject playerObj = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        playerController = playerObj.GetComponent<PlayerController>();

        if (playerController != null)
        {
            playerController.SetInputBlocked(true);
        }

        if (mapGenerator != null)
        {
            mapGenerator.PlacePlayerAtSpawn();
        }

        // [핵심 추가] 카메라 추적 타겟 자동 할당
        // 플레이어 하위의 'CameraTarget' 오브젝트를 찾아 시네머신 카메라에 연결합니다.
        Transform camTarget = playerObj.transform.Find("CameraTarget");
        if (camTarget != null)
        {
            var vcam = Object.FindFirstObjectByType<CinemachineCamera>();
            if (vcam != null)
            {
                vcam.Follow = camTarget;
                Debug.Log("<color=cyan>[GameManager]</color> Cinemachine Camera Target assigned to: " + camTarget.name);
            }
            else
            {
                Debug.LogWarning("[GameManager] 씬에서 CinemachineCamera를 찾을 수 없습니다.");
            }
        }

        if (playerController != null)
        {
            playerController.SetInputBlocked(false);
        }

        Debug.Log("<color=cyan>[GameManager]</color> Player Spawned, Placed, and Camera Assigned.");
    }
}

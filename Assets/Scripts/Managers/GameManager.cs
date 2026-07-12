using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임의 전체 생명주기와 매니저들의 초기화 순서를 관리하는 중앙 컨트롤러입니다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private PlayerController playerController;
    public PlayerController PLAYERCONTROLLER => playerController;
    public bool IsPlayerReady { get; private set; } = false;

    [Header("Global Volume")]
    [SerializeField] public Volume globalvolume;

    [Header("Core Managers")]
    [SerializeField] public DataManager dataManager;
    [SerializeField] public EconomyManager economyManager;
    [SerializeField] public CameraManager cameraManager;
    [SerializeField] public HitStopManager hitStopManager;

    [SerializeField] public ThrowImpactManager throwImpactManager;
    [SerializeField] public MouseManager mouseManager;
    [SerializeField] public MouseCursorManager mouseCursorManager;

    [Header("Growth System")]
    [SerializeField] public InventoryManager inventoryManager;
    [SerializeField] public PlayerSkillInventoryManager playerSkillInventoryManager;

    [SerializeField] public SquadSpawner squadSpawner;
    [SerializeField] public RewardManager rewardManager;

    [Header("UI References")]
    [SerializeField] public PlayerStateUI playerStateUI;
    [SerializeField] public MinionStateUI minionStateUI;
    [SerializeField] public MinionSkillQueueUI minionSkillQueueUI;
    [Header("Combat Sound")]
    [SerializeField] private PlayerAttackSoundData playerAttackSoundData;


    [Header("Map Generation")]
    [SerializeField] public MapGenerator mapGenerator;
    [SerializeField] private MapGenerationDataSO currentStageMapData;
    public MapGenerationDataSO CurrentStageMapData => currentStageMapData;
    [SerializeField] private RoomPrefabDataSO currentStageRoomData;

    private bool _isTimeStopped = false;
    public bool IsTimeStopped => _isTimeStopped;

    public void SetTimeStop(bool stop)
    {
        _isTimeStopped = stop;
        Time.timeScale = stop ? 0f : 1f;

        if (!stop) playerController?.CanChangeAnimState();
        Debug.Log($"<color=yellow>[TimeSystem]</color> Time Scale set to: {Time.timeScale}");
    }



    // [추가] 게임 매니저에서 비네트 색상 변경 메서드
    public void ChangeVignetteColor(float time, Color color)
    {
        StartCoroutine(ChangeVignetteColorCoroutine(time, color));
    }

    private IEnumerator ChangeVignetteColorCoroutine(float time, Color color)
    {
        Vignette vignette;
        if (globalvolume != null && globalvolume.profile.TryGet(out vignette))
        {
            Color originalColor = vignette.color.value;
            vignette.color.value = color;

            yield return new WaitForSeconds(time);

            vignette.color.value = originalColor;
        }
    }

    // 게임오버 처리 메서드
    public void Gameover()
    {
        StopAllCoroutines(); // 모든 코루틴 정지
        SetTimeStop(true); // 게임 시간 정지

        // 게임 오버 시 세이브 데이터를 삭제하여 이전 층에서 이어서 하기 방지
        SaveSystem.DeleteSave();

        GameOverManager.Instance.TriggerGameOver();
    }



    [Header("Test Options")]
    [SerializeField] public bool testMode_InfiniteStamina = false;
    [SerializeField] public bool testMode_DisableAutoBattle = false;

    [Header("Floor Info")]
    [SerializeField] public int currentFloor = 1;
    [SerializeField] public bool debugStartAtBoss = false; // [추가] 보스방 직행 디버그 옵션
    [SerializeField] public int debugStartFloor = 4;       // [추가] 보스방 기준 층
    [System.NonSerialized] private SaveData _loadedSaveData = null;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Awake에서 세이브 데이터를 먼저 로드해 둡니다.
        _loadedSaveData = SaveSystem.Load();
        if (_loadedSaveData != null)
        {
            currentFloor = _loadedSaveData.currentFloor;
            Debug.Log($"<b>[GameManager]</b> Loaded save data. Current Floor: {currentFloor}");
        }
        else
        {
            currentFloor = 1;
            Debug.Log("<b>[GameManager]</b> No save data found. Starting from Floor 1.");
        }

        if (debugStartAtBoss)
        {
            currentFloor = debugStartFloor;
            Debug.Log($"<color=yellow>[GameManager]</color> Debug Start at Boss enabled! Forcing Floor {currentFloor}");
        }

        InitializeGame();
    }

    private void InitializeGame()
    {
        Debug.Log("<b>[GameManager]</b> Starting Initialization Sequence...");

        if (dataManager == null) dataManager = GetComponentInChildren<DataManager>();
        if (economyManager == null) economyManager = GetComponentInChildren<EconomyManager>();
        if (cameraManager == null) cameraManager = GetComponentInChildren<CameraManager>();
        if (hitStopManager == null) hitStopManager = GetComponentInChildren<HitStopManager>();

        if (throwImpactManager == null) throwImpactManager = GetComponentInChildren<ThrowImpactManager>();
        if (mouseManager == null) mouseManager = GetComponentInChildren<MouseManager>();
        if (mouseCursorManager == null) mouseCursorManager = GetComponentInChildren<MouseCursorManager>();
        if (inventoryManager == null) inventoryManager = GetComponentInChildren<InventoryManager>();
        if (playerSkillInventoryManager == null) playerSkillInventoryManager = GetComponentInChildren<PlayerSkillInventoryManager>();

        if (squadSpawner == null) squadSpawner = GetComponentInChildren<SquadSpawner>();
        if (rewardManager == null) rewardManager = GetComponentInChildren<RewardManager>();
        if (globalvolume == null) globalvolume = GameObject.Find("Global Volume")?.GetComponent<Volume>();

        if (dataManager != null) dataManager.Initialize();

        if (inventoryManager != null)
        {
            inventoryManager.Initialize(_loadedSaveData != null);

        if (playerSkillInventoryManager != null)
            playerSkillInventoryManager.Initialize();

            if (_loadedSaveData != null)
            {
                inventoryManager.LoadFromData(_loadedSaveData);

            if (playerSkillInventoryManager != null)
                playerSkillInventoryManager.LoadFromData(_loadedSaveData);

            }
        }

        if (economyManager != null) economyManager.Initialize();
        if (cameraManager != null) cameraManager.Initialize();
        if (hitStopManager != null) hitStopManager.Initialize();

        if (throwImpactManager != null) throwImpactManager.Initialize();
        if (rewardManager != null) rewardManager.Initialize();

        // 초기화 시점에는 아직 플레이어가 없으므로 SquadSpawner의 AllyManager 연결은 미룹니다.

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

        SpawnPlayer();

        if (playerStateUI != null && playerController != null)
        {
            var health = playerController.GetComponentInChildren<CharacterHealth>();
            var allyManager = playerController.GetComponent<AllyManager>();

            // 플레이어 체력 복구
            if (_loadedSaveData != null && health != null)
            {
                health.SetHP(_loadedSaveData.playerHP);
                Debug.Log($"<color=green>[GameManager]</color> Player HP Restored to: {_loadedSaveData.playerHP}");
            }

            if (minionStateUI != null)
            {
                var skillCtrl = playerController.GetComponent<PlayerSkillController>();
                minionStateUI.Initialize(skillCtrl);
                Debug.Log("<color=cyan>[GameManager]</color> MinionStateUI Initialized.");

                if (minionSkillQueueUI != null)
                    minionSkillQueueUI.Initialize(skillCtrl);

            // DashCooldownUI는 PlayerStateUI.Initialize()에서 자체 처리

            }

            playerStateUI.Initialize(health, allyManager);

            // PlayerSkillController.Awake()가 같은 프레임에 실행됐으므로
            // equippedMinions가 이미 채워진 상태 → 아이콘 즉시 갱신
            playerStateUI.RefreshSkillIcons();

            // 전투 사운드 콘트롤러 연결
            var combatSound = playerController.GetComponent<PlayerCombatSoundController>();
            if (combatSound != null && playerAttackSoundData != null)
                combatSound.SetSoundData(playerAttackSoundData);

            // 로딩 완료 → UI 사운드 연다
            UISoundController.SetReady();




            Debug.Log("<color=cyan>[GameManager]</color> Player HUD Initialized.");
        }

        // 마을 시스템을 위해서 임시로 추가한 코드
        if(SceneManager.GetActiveScene().name == "VillageScene")
        {
            if (squadSpawner != null)
            {
                squadSpawner.RefreshFullSquad();
            }
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

            // [핵심 수정] 부대 스포너에 새로 생성된 플레이어의 AllyManager를 연결해줍니다.
            var allyManager = playerObj.GetComponent<AllyManager>();
            if (squadSpawner != null && allyManager != null)
            {
                squadSpawner.Initialize(inventoryManager, allyManager);
                Debug.Log("<color=cyan>[GameManager]</color> SquadSpawner Re-Initialized with new AllyManager.");
            }
        }

        if (mapGenerator != null)
        {
            mapGenerator.PlacePlayerAtSpawn();
        }

        Transform camTarget = playerObj.transform.Find("CameraTarget");
        if (camTarget != null)
        {
            var vcam = Object.FindFirstObjectByType<CinemachineCamera>();
            if (vcam != null)
            {
                vcam.Follow = camTarget;

                // 텔레포트와 동일한 워프 방식: CameraManager를 통해 즉시 스냅시킴
                Vector3 camDelta = camTarget.position - vcam.transform.position;
                var camMgr = CameraManager.Instance != null ? CameraManager.Instance : vcam.GetComponent<CameraManager>();
                if (camMgr != null)
                {
                    camMgr.WarpCamera(camTarget, camDelta);
                }
                else
                {
                    vcam.OnTargetObjectWarped(camTarget, camDelta);
                }

                Debug.Log("<color=cyan>[GameManager]</color> Cinemachine Camera Target assigned to: " + camTarget.name);
            }
        }

        if (playerController != null)
        {
            playerController.SetInputBlocked(false);
        }

        IsPlayerReady = true;
        Debug.Log("<color=cyan>[GameManager]</color> Player Spawned, Placed, and Camera Assigned.");
    }

public void GoToNextFloor()
    {
        SaveData data = new SaveData();

        // 다음 층수 저장
        data.currentFloor = currentFloor + 1;

        // 플레이어 체력 횝득
        if (playerController != null)
        {
            var health = playerController.GetComponentInChildren<CharacterHealth>();
            if (health != null)
            {
                data.playerHP = health.CurHP;
            }
        }
        else
        {
            data.playerHP = 10f; // 기본값
        }

        // 인벤토리, 골드, 보물, 보석 저장
        if (inventoryManager != null)
        {
            inventoryManager.SaveToData(data);

            if (playerSkillInventoryManager != null)
                playerSkillInventoryManager.SaveToData(data);
        }

        SaveSystem.Save(data);

        Debug.Log($"<color=green>[GameManager]</color> Floor Cleared! Transitioning to Floor {data.currentFloor}...");

        // 층 이동 시 이전 BGM이 이어지지 않도록 초기화
        if (SoundManager.Instance != null) SoundManager.Instance.StopBGM();

        // 씨 재로드
        string nextSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (ScreenFadeController.Instance != null)
        {
            ScreenFadeController.Instance.FadeOutIn(0.5f, 0.2f, 0.5f, () =>
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
            });
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
        }
    }
}
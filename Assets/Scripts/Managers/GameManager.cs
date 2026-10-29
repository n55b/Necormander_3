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

    [Header("플레이어")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private PlayerController playerController;
    public PlayerController PLAYERCONTROLLER => playerController;
    public bool IsPlayerReady { get; private set; } = false;
    public string InitializationError { get; private set; }
    private float _loadingProgress;
    /// <summary>실제 초기화 단계 진행률. SceneLoader는 이 값과 IsPlayerReady만 소비한다.</summary>
    public float LoadingProgress => Mathf.Max(_loadingProgress,
        mapGenerator != null ? 0.1f + 0.65f * mapGenerator.GenerationProgress : 0f);

    [Header("전체 볼륨")]
    [SerializeField] public Volume globalvolume;

    [Header("핵심 관리자 연결")]
    [SerializeField] public DataManager dataManager;
    [SerializeField] public CameraManager cameraManager;
    [SerializeField] public HitStopManager hitStopManager;

    [SerializeField] public MouseManager mouseManager;
    [SerializeField] public MouseCursorManager mouseCursorManager;

    [Header("성장 시스템")]
    [SerializeField] public InventoryManager inventoryManager;
    [SerializeField] public PlayerSkillInventoryManager playerSkillInventoryManager;
    [Tooltip("아이템 주머니. 장비(playerSkillInventoryManager)와 별개 시스템이다.")]
    [SerializeField] public ItemPouch itemPouch;

    [SerializeField] public RewardManager rewardManager;

    [Header("상태 UI와 반응 연출")]
    [SerializeField] public OffscreenEnemyArrowManager offscreenEnemyArrowManager;

    [Header("UI 연결")]
    [SerializeField] public PlayerStateUI playerStateUI;
    [Header("전투 효과음")]
    [SerializeField] private PlayerAttackSoundData playerAttackSoundData;


    [Header("맵 생성")]
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



    private Vignette _hurtVignette;
    private Color _vignetteRestColor;
    private float _vignetteRestoreAt;

    // 연속 피격이 이전 피격의 빨간색을 복원 색으로 저장하지 않도록 한 곳에서 소유한다.
    public void ChangeVignetteColor(float time, Color color)
    {
        if (_hurtVignette == null)
        {
            if (globalvolume == null || !globalvolume.profile.TryGet(out _hurtVignette)) return;
            _vignetteRestColor = _hurtVignette.color.value;
        }
        _hurtVignette.color.value = color;
        _vignetteRestoreAt = Time.unscaledTime + Mathf.Max(0f, time);
    }

    private void LateUpdate()
    {
        if (_hurtVignette != null && Time.unscaledTime >= _vignetteRestoreAt) RestoreVignette();
    }

    private void RestoreVignette()
    {
        if (_hurtVignette != null) _hurtVignette.color.value = _vignetteRestColor;
        _hurtVignette = null;
    }

    private void OnDisable() => RestoreVignette();

    // 게임오버 처리 메서드
    public void Gameover()
    {
        StopAllCoroutines(); // 모든 코루틴 정지
        SetTimeStop(true); // 게임 시간 정지

        // 게임 오버 시 세이브 데이터를 삭제하여 이전 층에서 이어서 하기 방지
        // 주의: 우클릭 해금/선택은 여기서 지워지지 않는다. 영구 성장요소라 PlayerPrefs 쪽에
        // 따로 산다(RightClickUnlockState). 런 세이브만 지우는 게 맞다.
        SaveSystem.DeleteSave();

        GameOverManager.Instance.TriggerGameOver();
    }



    [Header("테스트 옵션")]
    [SerializeField] public bool testMode_DisableAutoBattle = false;
    [Tooltip("디버그: 같은 상점에서도 장비를 여러 번 강화할 수 있다. 골드 비용, 강화 분기, 최종 단계 제한은 유지한다. 배포 시 꺼둘 것.")]
    [SerializeField] public bool testMode_UnlimitedShopEnhancements = false;

    [Header("층 정보")]
    [SerializeField] public int currentFloor = 1;
    [Tooltip("0이면 꺼짐. 1 이상이면 던전 첫 진입 시 세이브의 층수를 무시하고 그 층에서 시작한다. " +
             "다음 층 포탈로 이동할 때는 이 값을 다시 적용하지 않고 현재 층 + 1로 진행한다. " +
             "보스 층인지 아닌지는 MapGenerationData 의 Floor Bosses 표가 정하므로, " +
             "표에 있는 층(현재 4)을 넣으면 스폰방+보스방 2칸짜리 맵으로 시작한다.")]
    [SerializeField] public int debugStartFloor = 0;

    [Header("엘리트 아레나 (EliteTestScene 용 — 배포 빌드에선 반드시 꺼둘 것)")]
    [Tooltip("켜면 맵을 '스폰 방 + 엘리트 방' 두 칸짜리로만 만들고, 플레이어를 엘리트 방 입구에 바로 놓는다.")]
    [SerializeField] public bool debugEliteArena = false;
    [Tooltip("아레나에서 싸울 엘리트. 비워두면 엘리트 풀에서 무작위로 하나 뽑는다. " +
             "엘리트에 전용 방 프리팹이 지정돼 있으면 그 방이 배치된다.")]
    [SerializeField] public EnemyMinionDataSO debugForcedElite;
    [System.NonSerialized] private SaveData _loadedSaveData = null;
    private static int _pendingNextFloor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetFloorTransition() => _pendingNextFloor = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

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

        currentFloor = ResolveStartingFloor(currentFloor);

        InitializeGame();
    }

    private int ResolveStartingFloor(int savedFloor)
    {
        // 씬 프리팹은 다시 로드되므로 debugStartFloor를 0으로 바꾸는 것만으로는 유지되지 않는다.
        // 다음 층 이동 요청만 한 번 소비한다. 새 던전 진입에는 디버그 설정을 다시 쓸 수 있다.
        int nextFloor = _pendingNextFloor;
        _pendingNextFloor = 0;
        if (TutorialFlow.IsRunning)
        {
            Debug.Log("<color=cyan>[GameManager]</color> 튜토리얼 — Floor Tuning 0번을 적용합니다.");
            return 0;
        }
        if (nextFloor > 0)
        {
            Debug.Log($"<color=green>[GameManager]</color> 층 이동 — Debug Start Floor를 재적용하지 않고 {nextFloor}층으로 진행합니다.");
            return nextFloor;
        }
        if (debugStartFloor > 0)
        {
            Debug.Log($"<color=yellow>[GameManager]</color> Debug Start Floor — 첫 진입은 {debugStartFloor}층에서 시작합니다.");
            return debugStartFloor;
        }
        return savedFloor;
    }

    private void InitializeGame()
    {
        Debug.Log("<b>[GameManager]</b> Starting Initialization Sequence...");

        if (dataManager == null) dataManager = GetComponentInChildren<DataManager>();
        if (cameraManager == null) cameraManager = GetComponentInChildren<CameraManager>();
        if (hitStopManager == null) hitStopManager = GetComponentInChildren<HitStopManager>();

        if (mouseManager == null) mouseManager = GetComponentInChildren<MouseManager>();
        if (mouseCursorManager == null) mouseCursorManager = GetComponentInChildren<MouseCursorManager>();
        if (inventoryManager == null) inventoryManager = GetComponentInChildren<InventoryManager>();
        if (playerSkillInventoryManager == null) playerSkillInventoryManager = GetComponentInChildren<PlayerSkillInventoryManager>();
        if (itemPouch == null) itemPouch = GetComponentInChildren<ItemPouch>();

        if (rewardManager == null) rewardManager = GetComponentInChildren<RewardManager>();
        if (offscreenEnemyArrowManager == null) offscreenEnemyArrowManager = GetComponentInChildren<OffscreenEnemyArrowManager>();
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

        // [아이템 주머니] 장비와 별개 시스템이라 inventoryManager 블록 밖에 둔다.
        // dataManager.Initialize() 뒤여야 한다 — 로드할 때 레지스트리로 이름→SO 를 해석한다.
        if (itemPouch != null)
        {
            itemPouch.Initialize(_loadedSaveData != null);
            if (_loadedSaveData != null) itemPouch.LoadFromData(_loadedSaveData);
        }

        // [증강] static 상태라 씬을 다시 로드해도 안 죽는다. 새 씬을 시작할 때마다 반드시 비운다 —
        // 안 그러면 지난 층의 페널티가 그대로 남고, DamageEventBus 구독까지 중복으로 쌓인다.
        ActiveAugment.ResetAll();

        if (cameraManager != null) cameraManager.Initialize();
        if (hitStopManager != null) hitStopManager.Initialize();

        if (rewardManager != null) rewardManager.Initialize();
        if (offscreenEnemyArrowManager != null) offscreenEnemyArrowManager.Initialize();


        Debug.Log("<b>[GameManager]</b> Initial Managers Loaded.");
        _loadingProgress = 0.1f;
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

        if (mapGenerator != null && !mapGenerator.IsMapGenerationCompleted)
        {
            InitializationError = "맵 생성이 완료되지 않아 씬 입장을 중단합니다.";
            Debug.LogError("[GameManager] " + InitializationError);
            yield break;
        }
        _loadingProgress = 0.75f;
        SpawnPlayer();
        if (playerController == null)
        {
            InitializationError = "플레이어 생성에 실패해 씬 입장을 중단합니다.";
            yield break;
        }
        _loadingProgress = 0.85f;

        if (playerStateUI != null && playerController != null)
        {
            var health = playerController.GetComponentInChildren<CharacterHealth>();

            // 플레이어 체력 복구
            if (_loadedSaveData != null && health != null)
            {
                health.SetHP(_loadedSaveData.playerHP);
                Debug.Log($"<color=green>[GameManager]</color> Player HP Restored to: {_loadedSaveData.playerHP}");
            }

            playerStateUI.Initialize(health);

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

        _loadingProgress = 0.90f;
        if (mapGenerator != null)
        {
            // 맵/문/NavMesh 배치가 끝난 뒤 방별 후보를 계산한다. 랜덤 추첨과 소환은 여전히 방 진입 시 수행한다.
            foreach (var room in mapGenerator.AllRooms)
            {
                if (room == null) continue;
                var normal = room.GetComponent<NormalRoomEvent>();
                if (normal == null) continue;
                normal.PrepareSpawnCells(room, currentStageMapData);
                yield return null;
            }
            if (dataManager != null)
                yield return StartCoroutine(dataManager.PrewarmEncounter(mapGenerator, currentStageMapData, currentFloor));
        }
        _loadingProgress = 0.95f;
        // 첫 피격 프레임에 오디오 데이터를 불러오지 않도록 로딩 화면에서 준비한다.
        if (playerAttackSoundData != null && playerAttackSoundData.playerHurtClips != null)
        {
            foreach (var clip in playerAttackSoundData.playerHurtClips)
            {
                if (clip == null) continue;
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                while (clip.loadState == AudioDataLoadState.Loading) yield return null;
            }
        }
        // 첫 적중 때 무거운 연출 프리팹이 한꺼번에 생성되지 않도록 로딩 중 분산한다.
        yield return StartCoroutine(PolishFeedback.Prewarm());
        // 스폰 직후가 아니라 HUD 초기화와 카메라 LateUpdate까지 마친 뒤에만 완료한다.
        yield return null;
        yield return null;
        _loadingProgress = 1f;
        IsPlayerReady = true;
        if (!UnityNote.SceneLoader.IsLoading) playerController.SetInputBlocked(false);
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

            // [장비] 로드 시엔 플레이어가 아직 없어서 장비 스탯 패시브가 스킵됐다. 이제 스폰됐으니 재적용.
            if (playerSkillInventoryManager != null)
                playerSkillInventoryManager.ReapplyEquipmentPassives();

            // [아이템] 같은 이유. 새 플레이어에 주머니 스탯 효과를 다시 붙인다(멱등).
            if (itemPouch != null) itemPouch.Refresh();
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
                vcam.PreviousStateIsValid = false;

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

        Debug.Log("<color=cyan>[GameManager]</color> Player Spawned, Placed, and Camera Assigned.");
    }

    public void GoToNextFloor()
    {
        if (UnityNote.SceneLoader.IsLoading) return;
        // 튜토리얼엔 '다음 층'이 없다 — 마을로 나가면서 끝난다. 여기 한 곳에서 갈라놓으면
        // 튜토리얼 방에 어떤 포탈이 서든(마지막 방 포탈이든 보상 방 포탈이든) 전부 마을로 나간다.
        if (TutorialFlow.IsRunning)
        {
            TutorialFlow.Complete();

            // 튜토리얼에서 주운 장비/골드는 인계하지 않는다(0903 확정). 저장을 안 하는 것만으로는
            // 부족하다 — 예전 세이브가 남아 있으면 마을의 새 GameManager 가 그걸 읽어버린다.
            SaveSystem.DeleteSave();

            Debug.Log("<color=green>[GameManager]</color> 튜토리얼 완료 — 마을로 나갑니다.");
            LoadSceneWithFade("VillageScene", FadeSignal.씬전환);
            return;
        }

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

        if (itemPouch != null) itemPouch.SaveToData(data);

        SaveSystem.Save(data);

        Debug.Log($"<color=green>[GameManager]</color> Floor Cleared! Transitioning to Floor {data.currentFloor}...");

        // 씬 재로드
        LoadSceneWithFade(SceneManager.GetActiveScene().name, FadeSignal.층이동, data.currentFloor);
    }

    /// <summary>층수 전달은 GameManager, 로딩 화면/씬 교체는 공통 SceneLoader가 맡는다.</summary>
    private void LoadSceneWithFade(string sceneName, FadeSignal signal, int nextFloor = 0)
    {
        _pendingNextFloor = nextFloor;
        if (!UnityNote.SceneLoader.Load(sceneName, signal)) _pendingNextFloor = 0;
    }

    /// <summary>
    /// 현재 상태(장착 미니언/스킬, 골드, 보물, 보석, 체력)를 '층 이동 없이' 즉시 디스크에 저장한다.
    /// 마을→던전(BattleScene) 진입처럼 씬만 바뀌는 시점에 호출하면, 마을에서 세팅한 장착이 그대로 유지된다.
    /// (층수는 현재 값 그대로 저장 — 진입 시 특정 층으로 강제하고 싶으면 호출 전에 currentFloor를 조정.)
    /// </summary>
    public void SaveCurrentState()
    {
        SaveData data = new SaveData();
        data.currentFloor = currentFloor;

        if (playerController != null)
        {
            var health = playerController.GetComponentInChildren<CharacterHealth>();
            if (health != null) data.playerHP = health.CurHP;
        }
        else
        {
            data.playerHP = 10f;
        }

        if (inventoryManager != null)
        {
            inventoryManager.SaveToData(data);
            if (playerSkillInventoryManager != null)
                playerSkillInventoryManager.SaveToData(data);
        }

        if (itemPouch != null) itemPouch.SaveToData(data);

        SaveSystem.Save(data);
        Debug.Log("<color=green>[GameManager]</color> 현재 상태 저장 완료 (씬 이동 전).");
    }
}

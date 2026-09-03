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

    /// <summary>'다음 씬이 다 지어졌는지' 물어보는 조건을 만든다. 씬 전환 페이드가
    /// 언제 밝혀도 되는지를 이걸로 판단한다 (Fader.FadeOutIn 의 waitUntil 에 넘긴다).
    ///
    /// [함정] SceneManager.LoadScene 은 부른 즉시 로드되지 않고 '그 프레임 끝'에 로드된다.
    /// 그래서 그냥 IsPlayerReady 를 보면, 아직 멀쩡히 살아있는 '떠나는 씬'의 GameManager 가
    /// true 를 답해버려서 기다림이 통째로 무시된다 → 페이드 인이 로드 전에 다 끝나고,
    /// 맵이 지어지는 걸 밝은 화면으로 구경하게 된다.
    /// 그래서 지금 인스턴스를 기억해뒀다가 '그놈이 아닌 새 GameManager' 가 준비될 때까지 기다린다.</summary>
    public static System.Func<bool> NextSceneReady()
    {
        GameManager leaving = Instance;
        return () => Instance != leaving && Instance != null && Instance.IsPlayerReady;
    }

    [Header("Global Volume")]
    [SerializeField] public Volume globalvolume;

    [Header("Core Managers")]
    [SerializeField] public DataManager dataManager;
    [SerializeField] public EconomyManager economyManager;
    [SerializeField] public CameraManager cameraManager;
    [SerializeField] public HitStopManager hitStopManager;

    [SerializeField] public MouseManager mouseManager;
    [SerializeField] public MouseCursorManager mouseCursorManager;

    [Header("Growth System")]
    [SerializeField] public InventoryManager inventoryManager;
    [SerializeField] public PlayerSkillInventoryManager playerSkillInventoryManager;
    [Tooltip("아이템 주머니. 장비(playerSkillInventoryManager)와 별개 시스템이다.")]
    [SerializeField] public ItemPouch itemPouch;

    [SerializeField] public RewardManager rewardManager;

    [Header("HUD / Feedback")]
    [SerializeField] public OffscreenEnemyArrowManager offscreenEnemyArrowManager;

    [Header("UI References")]
    [SerializeField] public PlayerStateUI playerStateUI;
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
        // 주의: 우클릭 해금/선택은 여기서 지워지지 않는다. 영구 성장요소라 PlayerPrefs 쪽에
        // 따로 산다(RightClickUnlockState). 런 세이브만 지우는 게 맞다.
        SaveSystem.DeleteSave();

        GameOverManager.Instance.TriggerGameOver();
    }



    [Header("Test Options")]
    [SerializeField] public bool testMode_InfiniteStamina = false;
    [SerializeField] public bool testMode_DisableAutoBattle = false;

    [Header("Floor Info")]
    [SerializeField] public int currentFloor = 1;
    [Tooltip("0이면 꺼짐. 1 이상이면 세이브를 무시하고 그 층에서 시작한다. " +
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

        if (debugStartFloor > 0)
        {
            currentFloor = debugStartFloor;
            Debug.Log($"<color=yellow>[GameManager]</color> Debug Start Floor 설정됨 — 세이브를 무시하고 {currentFloor}층에서 시작합니다.");
        }

        // 튜토리얼은 '층 개념 밖'이다(MapGenerator 의 배치 분기와 같은 규칙). Floor Tuning 의 0층을 쓴다 —
        // 씬에 박힌 debugStartFloor 도, 남아 있던 세이브도 여기서 끊는다.
        //
        // 반드시 위 두 분기보다 뒤여야 한다. 튜토리얼이 도는 씬은 던전(BattleScene)과 같은 씬이라,
        // 보스 테스트하려고 debugStartFloor 를 4로 올려두면 튜토리얼까지 4층 조절표로 돌아버린다
        // (= 1층의 웨이브 1회 저작이 무시되고 전역 기본값 2회가 나온다).
        if (TutorialFlow.IsRunning)
        {
            currentFloor = 0;
            Debug.Log("<color=cyan>[GameManager]</color> 튜토리얼 — Floor Tuning 0번을 적용합니다.");
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

        if (economyManager != null) economyManager.Initialize();
        if (cameraManager != null) cameraManager.Initialize();
        if (hitStopManager != null) hitStopManager.Initialize();

        if (rewardManager != null) rewardManager.Initialize();
        if (offscreenEnemyArrowManager != null) offscreenEnemyArrowManager.Initialize();


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
        LoadSceneWithFade(SceneManager.GetActiveScene().name, FadeSignal.층이동);
    }

    /// <summary>
    /// 암전 → 씬 로드 → <b>새 씬이 다 지어진 뒤</b> 밝히기.
    ///
    /// 페이드 양(시간/색)은 여기 박지 않는다 — 신호 이름만 넘기면 ScreenFadeCanvas 의 Fader 가
    /// 그 줄을 찾아 쓴다(기획자가 거기서 조절). 암전을 SceneReady 까지 끄는 이유는, LoadScene 이
    /// 돌아와도 맵 생성 + 플레이어 스폰이 몇 프레임 더 남아 있어서다.
    /// </summary>
    private void LoadSceneWithFade(string sceneName, FadeSignal signal)
    {
        Fader fader = Fader.FullScreenFader;
        if (fader == null)
        {
            SceneManager.LoadScene(sceneName); // 페이더를 못 구해도 이동은 반드시 되어야 한다
            return;
        }

        fader.FadeOutIn(signal, () => SceneManager.LoadScene(sceneName), null, NextSceneReady());
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

using UnityEngine;

/// <summary>
/// 게임의 전체 생명주기와 매니저들의 초기화 순서를 관리하는 중앙 컨트롤러입니다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Player")]
    [SerializeField] private PlayerController playerController;
    public PlayerController PLAYERCONTROLLER => playerController;

    [Header("Core Managers")]
    [SerializeField] public DataManager dataManager;
    [SerializeField] public EconomyManager economyManager;
    [SerializeField] public ThrowImpactManager throwImpactManager;
    [SerializeField] public MouseManager mouseManager;
    [SerializeField] public MouseCursorManager mouseCursorManager;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // 초기화 시퀀스 시작 (순서가 매우 중요함)
        InitializeGame();
    }

    private void InitializeGame()
    {
        Debug.Log("<b>[GameManager]</b> Starting Initialization Sequence...");

        // 자식 오브젝트들에서 모든 매니저 자동 확보
        if (dataManager == null) dataManager = GetComponentInChildren<DataManager>();
        if (economyManager == null) economyManager = GetComponentInChildren<EconomyManager>();
        if (throwImpactManager == null) throwImpactManager = GetComponentInChildren<ThrowImpactManager>();
        if (mouseManager == null) mouseManager = GetComponentInChildren<MouseManager>();
        if (mouseCursorManager == null) mouseCursorManager = GetComponentInChildren<MouseCursorManager>();

        // 1. 순수 데이터 로드 (가장 먼저)
        if (dataManager != null) dataManager.Initialize();

        // 2. 시스템 로드 (데이터에 의존할 수 있는 매니저들)
        if (economyManager != null) economyManager.Initialize();
        if (throwImpactManager != null) throwImpactManager.Initialize();
        
        // 3. 마우스 및 컨트롤러 초기화
        // if (mouseManager != null) mouseManager.Initialize(); // 필요 시 추가
        
        // 4. 플레이어 참조 확보
        if (playerController == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) playerController = playerObj.GetComponent<PlayerController>();
        }

        Debug.Log("<b>[GameManager]</b> Initialization Sequence Completed.");
    }

    private void Start()
    {
        // 게임 시작 시 기본 미니언 한 마리 소환 (전사)
        SpawnStartingMinion();
    }

    private void SpawnStartingMinion()
    {
        if (dataManager == null || playerController == null) return;

        // 기본 미니언(전사) 데이터 가져오기
        MinionDataSO startingData = dataManager.GetMinionData(CommandData.SkeletonWarrior);
        if (startingData != null)
        {
            // 플레이어 근처 위치 계산 (SummonController 활용)
            var sumController = playerController.SUMCONTROLLER;
            Vector3 spawnPos = playerController.transform.position + Vector3.right; // 기본값

            if (sumController != null)
            {
                var positions = sumController.GetSummonPositions2D(1, 2f);
                if (positions.Count > 0) spawnPos = positions[0];
            }

            // 소환 수행
            var allyManager = Object.FindFirstObjectByType<AllyManager>();
            if (allyManager != null)
            {
                // [수정] 직접 CreateUnit 하지 않고 AllyManager를 통해 소환하여 리스트에 등록
                allyManager.SpawnAlly(startingData, spawnPos);
                Debug.Log("<color=green>[GameManager]</color> Starting Minion (Warrior) Registered and Spawned via AllyManager.");
            }
        }
    }
}

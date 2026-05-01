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
}

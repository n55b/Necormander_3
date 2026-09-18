using UnityEngine;

/// <summary>
/// 마을에서 본 마스터 테스트 방(BossTestScene)으로 바로 이동하는 포탈입니다.
/// FloorProceedPortal과 동일한 상호작용 규약(IInteractable, F키, 트리거 콜라이더)을 따르되,
/// 층 이동이 아니라 지정한 씬으로 직접 로드한다는 점만 다릅니다.
/// 배포 빌드에는 포함하지 않는 개발/테스트 전용 오브젝트입니다.
///
/// 씬 전환과 준비 대기는 공통 SceneLoader가 담당한다.
///
/// [버그 수정 — 장비 소실] FloorProceedPortal은 직접 씬을 로드하지 않고 GameManager.GoToNextFloor()를
/// 거치는데, 그 안에서 인벤토리/장착 스킬/골드/체력을 SaveSystem에 저장한 뒤 씬을 새로 로드한다.
/// 이 포탈은 처음엔 그 저장 과정을 건너뛰고 SceneManager로 바로 씬을 불러왔기 때문에, 새 씬의
/// GameManager.Awake()가 저장된 데이터를 못 찾아 장비가 전부 초기화됐다. GameManager에 정확히
/// 이 상황(층 이동 없이 씬만 바뀔 때)을 위한 SaveCurrentState()가 이미 있어서, 씬 전환 전에
/// 그것부터 호출하도록 고쳤다.
/// </summary>
public class BossTestPortal : MonoBehaviour, IInteractable
{
    [Tooltip("이 포탈이 로드할 씬 이름 (Build Settings에 등록되어 있어야 합니다)")]
    [SerializeField] private string targetSceneName = "BossTestScene";

    public string InteractionPrompt => "본 마스터 테스트 방으로 이동";

    private void Awake()
    {
        var col = GetComponent<CircleCollider2D>();
        if (col == null)
        {
            col = gameObject.AddComponent<CircleCollider2D>();
            col.radius = 0.6f;
        }
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        int interactableLayer = Layers.Interactable;
        gameObject.layer = interactableLayer != -1 ? interactableLayer : Layers.Default;
    }

    private void Start()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = new Color(0.75f, 0.1f, 0.1f, 0.85f); // 본 마스터를 상징하는 붉은 뼈빛 포탈
            sr.sortingOrder = 5;
        }
        Debug.Log("<color=purple>[BossTestPortal]</color> Initialized. Ready for interaction.");
    }

    private bool _loading = false;

    public bool Interact(GameObject interactor)
    {
        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogWarning("[BossTestPortal] targetSceneName이 비어 있습니다.");
            return false;
        }
        if (_loading || UnityNote.SceneLoader.IsLoading) return false; // 중복 입력 방지

        Debug.Log($"<color=green>[BossTestPortal]</color> Player interacted with portal. Moving to '{targetSceneName}'!");

        // 씬을 바꾸기 전에 반드시 현재 장비/인벤토리/골드/체력을 저장해 둔다.
        // (안 하면 새 씬의 GameManager가 저장된 데이터를 못 찾아 전부 초기화된다 — 실제로 겪었던 버그.)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SaveCurrentState();
        }
        else
        {
            Debug.LogWarning("[BossTestPortal] GameManager.Instance가 없어 상태 저장을 건너뜁니다. 장비가 초기화될 수 있습니다.");
        }

        _loading = UnityNote.SceneLoader.Load(targetSceneName, FadeSignal.층이동);
        return _loading;
    }

    public void OnFocused(GameObject interactor) { }
    public void OnLostFocus(GameObject interactor) { }
}

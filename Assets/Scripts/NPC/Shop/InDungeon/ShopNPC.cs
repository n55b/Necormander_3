using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 던전 내 상점 NPC.
/// NPCBase를 상속해 IInteractable을 구현합니다.
/// PlayerController.CheckForInteractable()이 자동으로 감지하고
/// F키 입력 시 Interact()를 호출합니다.
/// </summary>
public class ShopNPC : NPCBase
{
    [SerializeField] private List<SellItem> items;

    [Header("Shop UI")]
    [SerializeField] private GameObject shopPanel;
    // [수정] 상점 재고는 방당 한 번만 굴린다.
    // 재입장할 때마다 GenerateShopRoom을 다시 호출하면 품목이 리셋되고,
    // 회복 계열 보상을 무한히 재구매할 수 있었다.
    private bool _stockInitialized = false;


    // ─── IInteractable override ───────────────────────────────────────
    public override string InteractionPrompt => "F : 진열된 상품을 확인하세요";

    public override bool Interact(GameObject interactor)
    {
        return true;
    }

    private void Start()
    {
        // 씬에 직접 배치되어 있거나 방 입장 이벤트를 놓쳤을 경우를 대비해 Start 시점에도 초기화 시도
        Initialize();
    }

    // ─── 상점 초기화 (ShopRoomEvent 또는 Start에서 호출) ─────────────────────────
    public void Initialize()
    {
        // 이미 재고를 굴린 상점이면 재입장해도 다시 굴리지 않는다.
        if (_stockInitialized) return;

        var dm = GameManager.Instance != null
            ? GameManager.Instance.dataManager
            : null;
        if (dm == null || dm.SHOP_REGISTRY == null) return; // 아직 준비 안 됐으면 플래그를 세우지 않고 다음 시점에 재시도

        // 인스펙터 참조가 누락되었거나 중첩 프리팹 연결이 끊긴 경우를 대비한 자식/부모 탐색 fallback
        if (items == null || items.Count == 0)
        {
            items = new List<SellItem>(GetComponentsInChildren<SellItem>(true));
            if (items.Count == 0 && transform.parent != null)
            {
                items = new List<SellItem>(transform.parent.GetComponentsInChildren<SellItem>(true));
            }
        }

        List<RewardCandidate> prizes = RewardProcessor.GenerateShopRoom(dm);

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null) continue;

            if (i < prizes.Count)
            {
                items[i].gameObject.SetActive(true);
                items[i].item = prizes[i];
                items[i].InitializeUI();
            }
            else
            {
                // 뽑힌 아이템 풀보다 슬롯이 많으면 남는 슬롯은 숨김 처리
                items[i].gameObject.SetActive(false);
            }
        }

        _stockInitialized = true;
        Debug.Log($"<color=yellow>[ShopNPC]</color> Initialized shop with {Mathf.Min(prizes.Count, items.Count)} items.");
    }

    /// <summary>새 런/새 층 등에서 상점을 의도적으로 다시 굴리고 싶을 때 호출.</summary>
    public void ResetStock()
    {
        _stockInitialized = false;
    }
}

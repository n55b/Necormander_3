using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 던전 내 상점 NPC. F 로 말을 걸면 ShopPopupUI(상점 창)가 열린다.
/// 진열 재고(ShopOffer)를 들고 있고, 구매 처리도 여기서 한다.
/// </summary>
public class ShopNPC : NPCBase
{
    [Header("Stock")]
    [Tooltip("상점 창에 진열할 상품 개수")]
    [SerializeField, Min(1)] private int stockCount = 5;

    private readonly List<ShopOffer> _stock = new List<ShopOffer>();

    // 재고는 방당 한 번만 굴린다. 재입장마다 굴리면 품목이 리셋되어 무한 재구매가 가능했다.
    private bool _stockInitialized = false;

    // ─── 공개 API ────────────────────────────────────────────────────
    public IReadOnlyList<ShopOffer> Stock => _stock;
    public override string InteractionPrompt => "F : 진열된 상품을 확인하세요";

    /// <summary>F 입력 시 호출. 재고를 준비하고 상점 창을 연다.</summary>
    public override bool Interact(GameObject interactor)
    {
        Initialize();
        return ShopPopupUI.Open(this);
    }

    /// <summary>재고를 굴린다(ShopRoomEvent / Start / Interact). 이미 굴렸으면 무시한다.</summary>
    public void Initialize()
    {
        if (_stockInitialized) return;

        var dm = GameManager.Instance != null ? GameManager.Instance.dataManager : null;
        if (dm == null || dm.SHOP_REGISTRY == null) return; // 준비 전이면 플래그를 세우지 않고 다음 호출에 재시도

        HideLegacyFloorItems();
        RollStock(RewardProcessor.GenerateShopRoom(dm));

        _stockInitialized = true;
        Debug.Log($"<color=yellow>[ShopNPC]</color> Initialized shop with {_stock.Count} items.");
    }

    /// <summary>새 런/새 층 등에서 재고를 다시 굴리고 싶을 때 호출.</summary>
    public void ResetStock() => _stockInitialized = false;

    /// <summary>상점 창의 구매 버튼에서 호출. 골드 차감 → 지급, 지급 실패 시 환불하고 false.</summary>
    public bool TryPurchase(ShopOffer offer)
    {
        var inventory = InventoryManager.Instance;
        if (offer == null || offer.Purchased || !offer.IsValid || inventory == null || !_stock.Contains(offer)) return false;
        if (!inventory.SpendGold(offer.Price)) return false;

        if (!RewardManager.TryGivePurchase(offer.candidate, GetDropPosition()))
        {
            inventory.AddGold(offer.Price);
            return false;
        }

        offer.Purchased = true;
        return true;
    }

    // ─── 내부 전용 ───────────────────────────────────────────────────
    // 방 입장 이벤트를 놓쳐도 재고가 준비되도록 Start 에서도 초기화한다.
    private void Start() => Initialize();

    // 뽑힌 후보 중 유효한 것만 stockCount 개까지 진열한다.
    private void RollStock(List<RewardCandidate> prizes)
    {
        _stock.Clear();
        if (prizes == null) return;
        for (int i = 0; i < prizes.Count && _stock.Count < stockCount; i++)
        {
            var offer = new ShopOffer(prizes[i]);
            if (offer.IsValid) _stock.Add(offer);
        }
    }

    // 예전 바닥 진열품(SellItem)이 방 프리팹 오버라이드로 남아 있을 경우를 대비해 숨긴다.
    private void HideLegacyFloorItems()
    {
        foreach (var legacy in GetComponentsInChildren<SellItem>(true)) legacy.gameObject.SetActive(false);
    }

    // 주머니가 꽉 찼을 때 구매품이 떨어질 위치. 플레이어 발밑, 못 찾으면 NPC 위치.
    private Vector3 GetDropPosition()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        return player != null ? player.transform.position : transform.position;
    }
}

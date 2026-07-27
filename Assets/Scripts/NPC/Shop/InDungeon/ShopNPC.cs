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
    public override string InteractionPrompt => "F : 상점 열기";

    public override bool Interact(GameObject interactor)
    {
        return true;
    }

    public override void OnLostFocus(GameObject interactor)
    {
        base.OnLostFocus(interactor);
    }

    // ─── 상점 초기화 (ShopRoomEvent에서 호출) ─────────────────────────
public void Initialize()
    {
        // 이미 재고를 굴린 상점이면 재입장해도 다시 굴리지 않는다.
        if (_stockInitialized) return;

        var dm = GameManager.Instance != null
            ? GameManager.Instance.dataManager
            : null;
        if (dm == null) return; // 아직 준비 안 됐으면 플래그를 세우지 않고 다음 입장 때 재시도

        List<RewardCandidate> prizes = RewardProcessor.GenerateShopRoom(dm);

        for (int i = 0; i < prizes.Count; i++)
        {
            if (i < items.Count && items[i] != null)
            {
                items[i].item = prizes[i];
                items[i].InitializeUI();
            }
        }

        _stockInitialized = true;
    }

    /// <summary>새 런/새 층 등에서 상점을 의도적으로 다시 굴리고 싶을 때 호출.</summary>
    public void ResetStock()
    {
        _stockInitialized = false;
    }
}

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
        var dm = GameManager.Instance != null
            ? GameManager.Instance.dataManager
            : null;
        if (dm == null) return;

        List<RewardCandidate> prizes = RewardProcessor.GenerateShopRoom(dm);

        for (int i = 0; i < prizes.Count; i++)
        {
            if (i < items.Count && items[i] != null)
            {
                items[i].item = prizes[i];
                items[i].InitializeUI();
            }
        }
    }
}

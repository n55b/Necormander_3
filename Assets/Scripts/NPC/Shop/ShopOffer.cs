using UnityEngine;

/// <summary>
/// 상점 NPC 가 들고 있는 진열 상품 하나. (예전엔 바닥의 SellItem 오브젝트가 이 역할을 했다)
/// </summary>
public class ShopOffer
{
    public readonly RewardCandidate candidate;
    public bool Purchased { get; internal set; }

    public ShopOffer(RewardCandidate candidate) { this.candidate = candidate; }

    public int Price => candidate.goldAmount;
    public Sprite Icon => candidate.displayData != null ? candidate.displayData.icon : null;
    public bool IsValid => candidate.rawData != null && candidate.goldAmount >= 0;

    public string GetTitle() => TitleOf(candidate);
    public string GetDescription() => DescriptionOf(candidate);

    public static string TitleOf(RewardCandidate c)
    {
        if (c.rawData is ItemSO so) return so.DisplayName;
        var d = c.displayData;
        if (d == null) return "";
        if (d.localizedItemName != null && !d.localizedItemName.IsEmpty) return d.localizedItemName.GetLocalizedString();
        return d.itemName;
    }

    public static string DescriptionOf(RewardCandidate c)
    {
        if (c.rawData is ItemSO so) return so.TooltipBody;
        if (c.rawData is MinionDataSO minion) return HandSlotSelectionUI.Describe(minion);
        var d = c.displayData;
        if (d == null) return "";
        if (d.localizedDescription != null && !d.localizedDescription.IsEmpty) return d.localizedDescription.GetLocalizedString();
        return d.description;
    }
}

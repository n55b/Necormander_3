using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 클리어 보상 시퀀스(카테고리 선택 -> 아이템 선택)를 관리하는 매니저입니다.
/// </summary>
public class RewardManager : MonoBehaviour
{
    public static RewardManager Instance;

    public void Initialize()
    {
        Instance = this;
        Debug.Log("<color=cyan>[RewardManager]</color> Initialized.");
    }

    /// <summary>
    /// 방 클리어 시 호출되는 진입점입니다.
    /// </summary>
    public void RequestClearReward(RoomType type)
    {
        if (type == RoomType.Normal)
        {
            // 일반 방: 즉시 꾸러미 생성
            var rewards = RewardProcessor.GenerateNormalRoomRewards(
                GameManager.Instance.inventoryManager, 
                GameManager.Instance.dataManager
            );
            ShowNormalRewardUI(rewards);
        }
        else
        {
            // 엘리트/보상 방: 카테고리 선택 UI 먼저 띄움
            ShowCategorySelectionUI();
        }
    }

    // --- 단계별 UI 호출 (현재는 로그로 대체, 나중에 실제 UI와 연결) ---

    private void ShowCategorySelectionUI()
    {
        Debug.Log("<b>[UI Step 1]</b> 보상 카테고리를 선택하세요: [Minion, Metamorphosis, Gem, Treasure]");
        // [테스트용] 사용자 선택 시뮬레이션:
        // OnCategorySelected(RewardCategory.Minion); 
    }

    public void OnCategorySelected(RewardCategory category)
    {
        Debug.Log($"<color=yellow>[Reward]</color> {category} 카테고리 선택됨. 후보 생성 중...");

        List<RewardCandidate> candidates = RewardProcessor.GenerateCandidatesByCategory(
            GameManager.Instance.inventoryManager, 
            GameManager.Instance.dataManager, 
            category, 
            3
        );

        ShowItemSelectionUI(candidates);
    }

    private void ShowItemSelectionUI(List<RewardCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            Debug.Log("<color=orange>[Reward]</color> 해당 카테고리에 제안할 수 있는 보상이 없습니다!");
            return;
        }

        Debug.Log("<b>[UI Step 2]</b> 아이템 하나를 선택하세요:");
        for (int i = 0; i < candidates.Count; i++)
        {
            Debug.Log($"[{i}] {candidates[i].displayData.itemName} - {candidates[i].displayData.description}");
        }
    }

    private void ShowNormalRewardUI(List<RewardCandidate> rewards)
    {
        Debug.Log("<b>[Normal Room Reward]</b> 아래 보상을 모두 획득합니다:");
        foreach (var r in rewards)
        {
            if (r.category == RewardCategory.Gold) Debug.Log($"- Gold: {r.goldAmount}");
            else Debug.Log($"- {r.displayData.itemName}");
            
            ApplyReward(r);
        }
    }

    // --- 실제 보상 적용 ---

    public void ApplyReward(RewardCandidate candidate)
    {
        var inven = GameManager.Instance.inventoryManager;

        switch (candidate.category)
        {
            case RewardCategory.Minion:
                int emptyIdx = inven.Slots.FindIndex(s => s.IsEmpty);
                if (emptyIdx != -1) inven.EquipLineage(emptyIdx, (MinionLineageSO)candidate.rawData);
                break;

            case RewardCategory.Metamorphosis:
                inven.ApplyMetamorphosis((MinionLineageSO)candidate.rawData, candidate.techIndex);
                break;

            case RewardCategory.Gem:
                inven.EquipGem(((GemSO)candidate.rawData).targetJob, (GemSO)candidate.rawData);
                break;

            case RewardCategory.Treasure:
                inven.AddTreasure((TreasureSO)candidate.rawData);
                break;

            case RewardCategory.Gold:
                // TODO: EconomyManager 연동
                Debug.Log($"<color=yellow>[Economy]</color> 골드 {candidate.goldAmount} 획득!");
                break;
        }

        Debug.Log($"<color=green>[Reward]</color> 획득 완료: {candidate.displayData.itemName}");
        GameManager.Instance.squadSpawner.RefreshFullSquad();
    }
}

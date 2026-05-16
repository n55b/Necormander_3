using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [역할: 진행자] 방 클리어 후 발생하는 보상 획득의 전체 흐름을 관리합니다.
/// </summary>
public class RewardManager : MonoBehaviour
{
    public static RewardManager Instance;

    [Header("UI Reference")]
    [SerializeField] private RewardSelectionUI selectionUI;
    [SerializeField] private HandSlotSelectionUI handSlotUI;

    [Header("Reward Settings")]
    [SerializeField, Range(0f, 1f)] private float treasureDropChance = 0.5f;


    private Queue<List<RewardCandidate>> _rewardQueue = new Queue<List<RewardCandidate>>();

    public void Initialize()
    {
        Instance = this;
        Debug.Log("<color=cyan>[RewardManager]</color> Initialized.");
    }

    public void RequestClearReward(RoomType type)
    {
        _rewardQueue.Clear();

        if (type == RoomType.Normal)
        {
            int goldAmount = 200;
            GameManager.Instance.inventoryManager.AddGold(goldAmount);
            Debug.Log($"<color=yellow>[Reward]</color> Normal Room Cleared! {goldAmount} Gold obtained.");

            // [수정] 사용자 요청에 따라 소환수+보석 혼합 보상 3개 생성
            var normalRewards = RewardProcessor.GenerateNormalRoomRewards(
                GameManager.Instance.inventoryManager, 
                GameManager.Instance.dataManager
            );
            _rewardQueue.Enqueue(normalRewards);

            ProcessNextReward();
        }
        else
        {
            Debug.Log($"<color=yellow>[Reward]</color> {type} Room Cleared! Generating Mixed rewards...");

            var mixedCandidates = RewardProcessor.GenerateMixedCandidates(
                GameManager.Instance.inventoryManager, 
                GameManager.Instance.dataManager, 
                new List<RewardCategory> { 
                    RewardCategory.Ability,
                    RewardCategory.Metamorphosis 
                }
            );

            _rewardQueue.Enqueue(mixedCandidates);
            ProcessNextReward();
        }
    }

    private void ProcessNextReward()
    {
        if (_rewardQueue.Count > 0)
        {
            List<RewardCandidate> nextSet = _rewardQueue.Dequeue();
            ShowRewardSelection(nextSet);
        }
        else
        {
            Debug.Log("<color=green>[Reward]</color> All reward sequences completed.");
            
            // [추가] 모든 보상이 완료되면 시간 재개
            if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(false);

            if (selectionUI != null) selectionUI.Hide();
            if (handSlotUI != null) handSlotUI.Hide();
        }
    }

    /// <summary>
    /// [수정] 외부에서 직접 보상 후보 리스트를 전달하여 선택 UI를 띄울 수 있게 합니다.
    /// </summary>
    public void ShowRewardSelection(List<RewardCandidate> candidates)
    {
        if (selectionUI != null)
        {
            // [추가] 보상 창이 뜨면 시간 정지
            if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(true);
            
            selectionUI.Show(candidates);
        }
    }

    private void ShowItemSelectionUI(List<RewardCandidate> candidates)
    {
        if (selectionUI != null)
        {
            selectionUI.Show(candidates);
        }
    }

    public void ApplyReward(RewardCandidate candidate)
    {
        if (candidate.rawData == null) 
        {
            Debug.Log("<color=gray>[Reward]</color> No reward selected or empty slot.");
            ProcessNextReward();
            return;
        }

        var inven = GameManager.Instance.inventoryManager;

        switch (candidate.category)
        {
            case RewardCategory.Minion:
                // [수정] 이미 가지고 있는 직업이라면 수량만 늘리고, 새로우면 슬롯 선택 UI 오픈
                MinionLineageSO lineage = (MinionLineageSO)candidate.rawData;
                if (inven.HasJobInSlots(lineage.jobType))
                {
                    inven.AddMinionOrIncreaseQuantity(lineage.jobType, 1);
                    ProcessNextReward();
                }
                else
                {
                    if (handSlotUI != null)
                    {
                        if (selectionUI != null) selectionUI.Hide();
                        handSlotUI.Show(candidate);
                    }
                    else
                    {
                        inven.AddMinionOrIncreaseQuantity(lineage.jobType, 1);
                        ProcessNextReward();
                    }
                }
                break;

            case RewardCategory.Ability:
                if (handSlotUI != null)
                {
                    if (selectionUI != null) selectionUI.Hide();
                    handSlotUI.Show(candidate);
                }
                else
                {
                    int emptyIdx = inven.Slots.FindIndex(s => s.IsEmpty);
                    if (emptyIdx != -1)
                    {
                        inven.EquipThrowAbility(emptyIdx, (ThrowAbilitySO)candidate.rawData);
                    }
                    ProcessNextReward();
                }
                break;

            case RewardCategory.Metamorphosis:
                inven.ApplyMetamorphosis((MinionLineageSO)candidate.rawData, candidate.techIndex);
                ProcessNextReward();
                break;

            case RewardCategory.Gem:
                inven.AddGemToAvailable((GemSO)candidate.rawData, candidate.targetJob);
                ProcessNextReward();
                break;

            case RewardCategory.Treasure:
                inven.AddTreasure((TreasureSO)candidate.rawData);
                ProcessNextReward();
                break;
        }

        Debug.Log($"<color=green>[Reward]</color> Processing candidate: {candidate.displayData.itemName}");
        // [사용자 요청] 보상 획득 시 즉시 재소환하지 않음 (다음 전투 시작 시 소환)
        // GameManager.Instance.squadSpawner.RefreshFullSquad();
    }

    public void NotifyHandSlotSelectionComplete()
    {
        ProcessNextReward();
    }

    public void SkipReward()
    {
        Debug.Log("<color=orange>[Reward]</color> Reward skipped.");
        ProcessNextReward();
    }
}

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
    [SerializeField] private GemSlotSelectionUI gemSlotUI;

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

            var gemCandidates = RewardProcessor.GenerateCandidatesByCategory(
                GameManager.Instance.inventoryManager, 
                GameManager.Instance.dataManager, 
                RewardCategory.Gem
            );
            _rewardQueue.Enqueue(gemCandidates);

            if (Random.value < treasureDropChance)
            {
                var treasureCandidates = RewardProcessor.GenerateCandidatesByCategory(
                    GameManager.Instance.inventoryManager, 
                    GameManager.Instance.dataManager, 
                    RewardCategory.Treasure
                );
                _rewardQueue.Enqueue(treasureCandidates);
            }

            ProcessNextReward();
        }
        else
        {
            Debug.Log($"<color=yellow>[Reward]</color> {type} Room Cleared! Generating Mixed rewards...");

            var mixedCandidates = RewardProcessor.GenerateMixedCandidates(
                GameManager.Instance.inventoryManager, 
                GameManager.Instance.dataManager, 
                new List<RewardCategory> { 
                    RewardCategory.Minion, 
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
            ShowItemSelectionUI(nextSet);
        }
        else
        {
            Debug.Log("<color=green>[Reward]</color> All reward sequences completed.");
            if (selectionUI != null) selectionUI.Hide();
            if (handSlotUI != null) handSlotUI.Hide();
            if (gemSlotUI != null) gemSlotUI.Hide();
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
                        if (candidate.category == RewardCategory.Minion) inven.EquipLineage(emptyIdx, (MinionLineageSO)candidate.rawData);
                        else inven.EquipThrowAbility(emptyIdx, (ThrowAbilitySO)candidate.rawData);
                    }
                    ProcessNextReward();
                }
                break;

            case RewardCategory.Metamorphosis:
                inven.ApplyMetamorphosis((MinionLineageSO)candidate.rawData, candidate.techIndex);
                ProcessNextReward();
                break;

            case RewardCategory.Gem:
                if (gemSlotUI != null)
                {
                    if (selectionUI != null) selectionUI.Hide();
                    gemSlotUI.Show(candidate);
                }
                else
                {
                    inven.EquipGem(candidate.targetJob, (GemSO)candidate.rawData, 0);
                    ProcessNextReward();
                }
                break;

            case RewardCategory.Treasure:
                inven.AddTreasure((TreasureSO)candidate.rawData);
                ProcessNextReward();
                break;
        }

        Debug.Log($"<color=green>[Reward]</color> Processing candidate: {candidate.displayData.itemName}");
        GameManager.Instance.squadSpawner.RefreshFullSquad();
    }

    public void NotifyHandSlotSelectionComplete()
    {
        ProcessNextReward();
    }

    public void NotifyGemSelectionComplete()
    {
        ProcessNextReward();
    }

    public void SkipReward()
    {
        Debug.Log("<color=orange>[Reward]</color> Reward skipped.");
        ProcessNextReward();
    }
}

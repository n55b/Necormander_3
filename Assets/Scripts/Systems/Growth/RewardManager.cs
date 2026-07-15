using System;
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

    private Queue<List<RewardCandidate>> _rewardQueue = new Queue<List<RewardCandidate>>();

    [Header("보상 스킵 설정")]
    [SerializeField] private float skipRewardHealAmount = 10f;

    public void Initialize()
    {
        Instance = this;
        Debug.Log("<color=cyan>[RewardManager]</color> Initialized.");
    }

    public void RequestClearReward(RoomType type, RoomInstance.NormalRewardType normalRewardType = RoomInstance.NormalRewardType.PlayerSkill)
    {
        _rewardQueue.Clear();

        if (type == RoomType.Normal)
        {
            int goldAmount = 200;
            GameManager.Instance.inventoryManager.AddGold(goldAmount);
            Debug.Log($"<color=yellow>[Reward]</color> Normal Room Cleared! {goldAmount} Gold obtained. RewardType: {normalRewardType}");

            List<RewardCandidate> normalRewards;
            switch (normalRewardType)
            {
                case RoomInstance.NormalRewardType.PlayerSkill:
                    normalRewards = RewardProcessor.GeneratePlayerSkillRewards(
                        GameManager.Instance.inventoryManager,
                        GameManager.Instance.dataManager);
                    break;
                case RoomInstance.NormalRewardType.SubSummon:
                    normalRewards = RewardProcessor.GenerateSummonRewards(
                        GameManager.Instance.inventoryManager,
                        GameManager.Instance.dataManager,
                        MinionRole.Sub);
                    break;
                default: // MainSummon
                    normalRewards = RewardProcessor.GenerateSummonRewards(
                        GameManager.Instance.inventoryManager,
                        GameManager.Instance.dataManager,
                        MinionRole.Main);
                    break;
            }
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
                // Metamorphosis is unused for now; every minion reward always opens the hand-slot picker (swap-based design)
                MinionDataSO minion = (MinionDataSO)candidate.rawData;
                {
                    if (handSlotUI != null)
                    {
                        // [추가] 장착 UI가 뜨면 시간 정지 (상점 구매 등 직접 호출 케이스 대응)
                        if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(true);

                        if (selectionUI != null) selectionUI.Hide();
                        handSlotUI.Show(candidate);
                    }
                    else
                    {
                        inven.AddMinionOrIncreaseQuantity(minion.minionType, 1);
                        ProcessNextReward();
                    }
                }
                break;

            case RewardCategory.Ability:
                if (handSlotUI != null)
                {
                    // [추가] 장착 UI가 뜨면 시간 정지
                    if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(true);

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
                // 변이/진화 시스템 폐지로 동작 생략
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

            case RewardCategory.PlayerSkill:
                var skill = (PlayerSkillSO)candidate.rawData;
                PlayerSkillInventoryManager.Instance?.AddOwnedSkill(skill);

                if (GameManager.Instance != null && GameManager.Instance.playerStateUI != null)
                {
                    // 슬롯 선택을 기다림. 선택이 끝난 다음 NotifyHandSlotSelectionComplete 호출되어야 다음 보상으로 넘어감
                    if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(true);
                    if (selectionUI != null) selectionUI.Hide();

                    GameManager.Instance.playerStateUI.OpenChangeSkillUI(skill);
                }
                else
                {
                    // UI가 연결돼 있지 않으면 풀에만 넣고 즉시 다음 보상으로상으로행
                    ProcessNextReward();
                }
                break;
        }

        Debug.Log($"<color=green>[Reward]</color> Processing candidate: {candidate.displayData.itemName}");
    }

    public void NotifyHandSlotSelectionComplete()
    {
        ProcessNextReward();
    }

    public void SkipReward()
    {
        Debug.Log("<color=orange>[Reward]</color> Reward skipped.");

        // 보상을 건너뛰면 보상 대신 체력을 회복시켜줍니다.
        HealPlayer(skipRewardHealAmount);

        ProcessNextReward();
    }

    /// <summary>
    /// 플레이어 체력을 회복시킵니다. 보상 스킵뿐 아니라 다른 곳에서도 재사용할 수 있도록 분리해뒀습니다.
    /// </summary>
    private void HealPlayer(float amount)
    {
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (player != null && player.Stat != null && player.Stat.Health != null)
        {
            player.Stat.Health.Heal(amount);
        }
    }

}

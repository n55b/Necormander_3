using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [역할: 진행자] 방 클리어 후 발생하는 보상 획득의 전체 흐름을 관리합니다.
/// - 담당: 보상 시퀀스(Queue) 관리, UI 호출 및 닫기, 선택된 보상의 실제 인벤토리 지급.
/// - 활용: 게임 루프(예: 방 클리어 시점)에서 RequestClearReward를 호출하여 보상 시퀀스를 시작합니다.
/// - 특징: RewardProcessor를 사용하여 데이터를 생성하고, RewardSelectionUI를 통해 유저와 상호작용합니다.
/// </summary>
public class RewardManager : MonoBehaviour
{
    public static RewardManager Instance;

    [Header("UI Reference")]
    [SerializeField] private RewardSelectionUI selectionUI;

    // 보상 대기열 (한 번에 3개씩 묶인 후보자들 리스트)
    private Queue<List<RewardCandidate>> _rewardQueue = new Queue<List<RewardCandidate>>();

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
        _rewardQueue.Clear();

        if (type == RoomType.Normal)
        {
            // 1. 골드 즉시 획득
            int goldAmount = 200;
            GameManager.Instance.inventoryManager.AddGold(goldAmount);
            Debug.Log($"<color=yellow>[Reward]</color> 일반 방 클리어! {goldAmount} 골드 즉시 획득.");

            // 2. 보석 후보 생성 -> 큐에 삽입
            var gemCandidates = RewardProcessor.GenerateCandidatesByCategory(
                GameManager.Instance.inventoryManager, 
                GameManager.Instance.dataManager, 
                RewardCategory.Gem
            );
            _rewardQueue.Enqueue(gemCandidates);

            // 3. 보물 확률(예: 30%) -> 큐에 삽입
            if (Random.value < 0.3f)
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
            // 엘리트/보상 방: 카테고리 선택 UI 먼저 띄움
            ShowCategorySelectionUI();
        }
    }

    /// <summary>
    /// 대기열에 있는 다음 보상을 화면에 띄웁니다.
    /// </summary>
    private void ProcessNextReward()
    {
        if (_rewardQueue.Count > 0)
        {
            List<RewardCandidate> nextSet = _rewardQueue.Dequeue();
            ShowItemSelectionUI(nextSet);
        }
        else
        {
            Debug.Log("<color=green>[Reward]</color> 모든 보상 시퀀스가 종료되었습니다.");
            if (selectionUI != null) selectionUI.Hide();
        }
    }

    private void ShowCategorySelectionUI()
    {
        Debug.Log("<b>[UI Step 1]</b> 보상 카테고리를 선택하세요: [Minion, Metamorphosis, Gem, Treasure]");
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
        if (selectionUI != null)
        {
            selectionUI.Show(candidates);
        }
        else
        {
            Debug.LogWarning("[RewardManager] SelectionUI 참조가 없습니다. 로그로 대체합니다.");
            for (int i = 0; i < candidates.Count; i++)
            {
                Debug.Log($"[{i}] {candidates[i].displayData.itemName} - {candidates[i].displayData.description}");
            }
        }
    }

    // --- 실제 보상 적용 및 시퀀스 이어가기 ---

    public void ApplyReward(RewardCandidate candidate)
    {
        if (candidate.rawData == null) 
        {
            Debug.Log("<color=gray>[Reward]</color> 보상을 선택하지 않았거나 빈 슬롯입니다.");
        }
        else
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
                    // [수정] candidate에 저장된 구체적인 targetJob을 사용하여 보석 장착
                    inven.EquipGem(candidate.targetJob, (GemSO)candidate.rawData);
                    break;

                case RewardCategory.Treasure:
                    inven.AddTreasure((TreasureSO)candidate.rawData);
                    break;
            }

            Debug.Log($"<color=green>[Reward]</color> 획득 완료: {candidate.displayData.itemName}");
            GameManager.Instance.squadSpawner.RefreshFullSquad();
        }

        ProcessNextReward();
    }

    public void SkipReward()
    {
        Debug.Log("<color=orange>[Reward]</color> 보상을 건너뛰었습니다.");
        ProcessNextReward();
    }
}

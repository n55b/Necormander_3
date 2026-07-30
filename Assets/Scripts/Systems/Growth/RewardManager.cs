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

    [Header("엘리트 방 보상")]
    [Tooltip("엘리트 방을 깼을 때 바닥에 떨어뜨릴 주머니 아이템 개수. 0 이면 아이템을 안 준다.")]
    [SerializeField] private int eliteItemDropCount = 1;

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
            // [보상 개편 26/07/24] 일반방은 골드만 드랍한다(장비=상점, 메인=보상방). 카드/시간정지 없음.
            int goldAmount = 200; // ponytail: 고정값, 밸런싱 시 조정
            GameManager.Instance.inventoryManager.AddGold(goldAmount);
            Debug.Log($"<color=yellow>[Reward]</color> Normal Room Cleared! {goldAmount} Gold obtained (gold-only).");
        }
        else if (type == RoomType.Elite)
        {
            // [엘리트 보상 26/07/30] 아이템을 바닥에 떨어뜨린다. 카드 UI 는 띄우지 않는다 —
            // 예전엔 Metamorphosis 후보만 굴렸는데 그 시스템이 없어져서 빈 카드 3장이 뜨는 죽은 경로였다.
            var data = GameManager.Instance.dataManager;
            var pos = PlayerPosition();
            int dropped = 0;
            for (int i = 0; i < eliteItemDropCount; i++)
            {
                var itemSO = RewardProcessor.PickRandomItem(data);
                if (itemSO == null) break;
                GroundItem.Drop(itemSO, pos);
                dropped++;
            }
            Debug.Log($"<color=yellow>[Reward]</color> Elite Room Cleared! 아이템 {dropped}개를 바닥에 떨어뜨렸다.");
        }
        else
        {
            Debug.Log($"<color=yellow>[Reward]</color> {type} Room Cleared! Generating Mixed rewards...");

            var mixedCandidates = RewardProcessor.GenerateMixedCandidates(
                GameManager.Instance.inventoryManager, 
                GameManager.Instance.dataManager, 
                new List<RewardCategory> { 
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


            case RewardCategory.Metamorphosis:
                // 변이/진화 시스템 폐지로 동작 생략
                ProcessNextReward();
                break;

            case RewardCategory.Treasure:
                inven.AddTreasure((TreasureSO)candidate.rawData);
                ProcessNextReward();
                break;

            case RewardCategory.Equipment:
                // 장비를 착용한다(한 자루 원칙 → 기존 장비 교체). 후보는 뜰 때 이미 스킬이 굴려진 인스턴스다.
                var equipInst = (EquipmentInstance)candidate.rawData;
                PlayerSkillInventoryManager.Instance?.EquipEquipment(equipInst);
                ProcessNextReward();
                break;

            case RewardCategory.Item:
                // [아이템] 절대 즉시 장착하지 않는다 — 플레이어 발밑 바닥에 떨어뜨린다.
                // 거기서 F 짧게 누르면 습득, 길게 누르면 분해다(GroundItem).
                // 상점 구매(SellItem)도 이 경로를 타므로 SellItem 은 따로 손댈 게 없다.
                GroundItem.Drop((ItemSO)candidate.rawData, PlayerPosition());
                ProcessNextReward();
                break;

            case RewardCategory.EquipmentEnhance:
                // 소모성 강화 아이템 구매 → 착용 장비 +1강(SellItem 이 CanEnhanceEquipped 를 이미 확인함).
                PlayerSkillInventoryManager.Instance?.EnhanceEquipped();
                ProcessNextReward();
                break;

            // [폐지] 스킬 단독 획득 → 장비(Equipment)로 대체. 생성되지 않지만 구 경로 호환을 위해 남겨둔다.
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

    /// <summary>아이템을 떨어뜨릴 자리. 플레이어를 못 찾으면 원점.</summary>
    private static Vector3 PlayerPosition()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        return player != null ? player.transform.position : Vector3.zero;
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

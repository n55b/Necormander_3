using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어가 상호작용하여 엘리트 등급의 보상을 얻을 수 있는 상자입니다.
/// </summary>
public class EliteRewardBox : MonoBehaviour, IInteractable
{
    private const string OpenAnimName = "Chest_Open";

    [Header("보상 설정")]
    [Tooltip("체크 시 던지기 능력/환골탈태/보석이 모두 나옵니다.")]
    [SerializeField] private bool isSuperEliteBox = false;

    [Header("소환수 강화 (보상방)")]
    [Tooltip("메인 소환수가 최대 강화이고 더 진화할 갈래도 없어 카드를 못 줄 때 대신 지급할 골드.")]
    [SerializeField] private int maxedEnhanceGoldReward = 300;

    // [26/07/30] 아이템 드랍은 여기가 아니다 — 이 상자는 이름과 달리 '보상방'(RewardRoomEvent)의
    // 메인 소환수 상자다. 엘리트 방 아이템 드랍은 RewardManager.RequestClearReward(RoomType.Elite) 가 한다.

    // 여는 애니메이션 재생 중 중복 상호작용/파괴 방지
    private bool _isOpening = false;

    private Animator _animator;

    public string InteractionPrompt => "Open Elite Reward";

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    public bool Interact(GameObject interactor)
    {
        if (_isOpening) return false;

        var inven = InventoryManager.Instance;
        var data = GameManager.Instance.dataManager;

        // [26/10/02] 보상방 = 메인 소환수 '강화'. 소환수는 마을 NPC 에서 고르고 가져온다.
        //  · [26/10/03] 진화 조건(요구 강화 단계 + 진화 갈래)을 채웠으면 → 진화 갈래 카드(보통 2장). 강화보다 우선.
        //  · 소환수가 있고 강화 여지가 있으면 → 강화 카드 1장(+스킵).
        //  · 이미 최대 강화면 → 골드로 대체.
        //  · 소환수가 아예 없으면(마을에서 안 고르고 들어옴) → 예전처럼 메인 소환수 카드를 준다.
        List<RewardCandidate> rewards;
        if (inven != null && inven.MainSummon != null)
        {
            rewards = RewardProcessor.GenerateMinionEvolveRewards(inven);
            if (rewards.Count == 0) rewards = RewardProcessor.GenerateMinionEnhanceReward(inven);
            if (rewards.Count == 0)
            {
                _isOpening = true;
                StartCoroutine(PlayOpenThenGrantGold(maxedEnhanceGoldReward));
                return true;
            }
        }
        else
        {
            int cardCount = isSuperEliteBox ? 2 : 1;
            rewards = RewardProcessor.GenerateSummonRewards(inven, data, typeof(MainMinionDataSO), cardCount);
        }

        if (rewards.Count > 0)
        {
            _isOpening = true;
            StartCoroutine(PlayOpenThenShowRewards(rewards));
            return true;
        }

        Debug.LogWarning("[EliteRewardBox] No valid rewards to offer.");
        return false;
    }

    /// <summary>Chest.aseprite의 Chest_Open 애니메이션을 재생한 뒤, 재생 길이만큼 기다렸다가 보상 선택창을 띄우고 파괴합니다.</summary>
    private IEnumerator PlayOpenThenShowRewards(List<RewardCandidate> rewards)
    {
        float wait = 0f;
        if (_animator != null)
        {
            // 열기 전까지는 인스펙터에 세팅된 정적 스프라이트를 그대로 유지하기 위해
            // Animator는 평소 꺼둔 상태(m_Enabled: 0)다. 열 때만 켜서 재생한다.
            _animator.enabled = true;
            _animator.Play(OpenAnimName, 0, 0f);
            wait = GetClipLength(_animator, OpenAnimName);
        }

        if (wait > 0f) yield return new WaitForSeconds(wait);

        // RewardSelectionUI를 통해 보상 선택 창 표시
        RewardManager.Instance.ShowRewardSelection(rewards);
        Debug.Log($"<color=magenta>[EliteRewardBox]</color> Opened, presenting {rewards.Count} rewards.");

        // 상호작용 후 자기 자신을 파괴
        Destroy(gameObject);
    }

    /// <summary>강화가 이미 최대일 때: 상자를 열고 골드를 주고 사라진다(카드 없음).</summary>
    private IEnumerator PlayOpenThenGrantGold(int amount)
    {
        float wait = 0f;
        if (_animator != null)
        {
            _animator.enabled = true;
            _animator.Play(OpenAnimName, 0, 0f);
            wait = GetClipLength(_animator, OpenAnimName);
        }
        if (wait > 0f) yield return new WaitForSeconds(wait);

        if (InventoryManager.Instance != null) InventoryManager.Instance.GainGold(amount);
        Debug.Log($"<color=magenta>[EliteRewardBox]</color> 소환수가 최대 강화라 {amount}G 로 대체 지급.");
        Destroy(gameObject);
    }


    private static float GetClipLength(Animator animator, string clipName)
    {
        var controller = animator.runtimeAnimatorController;
        if (controller == null) return 0f;

        foreach (var clip in controller.animationClips)
        {
            if (clip != null && clip.name == clipName) return clip.length;
        }

        return 0f;
    }

    public void OnFocused(GameObject interactor){}
    public void OnLostFocus(GameObject interactor){}
}

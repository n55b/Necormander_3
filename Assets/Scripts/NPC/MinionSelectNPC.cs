using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 마을의 '소환수 선택' NPC. 말을 걸면(F) 메인 소환수 후보 카드가 뜨고, 하나를 고르면
/// 메인 슬롯에 장착된 채로 던전에 들고 간다(던전 포탈이 SaveCurrentState 로 넘겨준다).
///
/// · 후보: <see cref="choices"/> 를 채우면 그 목록, 비워두면 GrowthRegistry 의 메인 소환수 전부(현재 3종).
/// · UI : 보상창(RewardSelectionUI) 프리팹을 그대로 재사용한다 — 마을 씬에는 보상창이 없으므로
///        처음 말을 걸 때 화면 Canvas(UIPopUpManager) 밑에 한 번 만들어 두고 계속 쓴다.
///        RewardManager 를 거치지 않는 콜백 모드(ShowWithCallbacks)로 띄운다.
/// · 새로 고르면 강화 단계는 0 으로 돌아간다(강화는 런 진행도라 보상방에서 다시 쌓는다).
///
/// 상호작용 감지는 PlayerController.CheckForInteractable 이 한다 → 이 컴포넌트와 같은 오브젝트에
/// Interactable 레이어 콜라이더가 있어야 한다(기존 NPC 의 InteractRange 와 동일 구성).
/// </summary>
public class MinionSelectNPC : NPCBase
{
    [Header("소환수 선택")]
    [Tooltip("보상 선택창 프리팹(Assets/Prefabs/UI/Reward Selection/RewardSelectionUI.prefab).")]
    [SerializeField] private RewardSelectionUI selectionUIPrefab;

    [Tooltip("고를 수 있는 소환수. 비워두면 GrowthRegistry 의 메인 소환수 전부(최대 3장).")]
    [SerializeField] private List<MainMinionDataSO> choices = new List<MainMinionDataSO>();

    private RewardSelectionUI _ui;
    private bool _isOpen;

    public override string InteractionPrompt => "F : 소환수 선택";

    public override bool Interact(GameObject interactor)
    {
        if (_isOpen) return false;

        var candidates = BuildCandidates();
        if (candidates.Count == 0)
        {
            Debug.LogWarning("[MinionSelectNPC] 고를 수 있는 메인 소환수가 없습니다. GrowthRegistry 또는 choices 를 확인하세요.", this);
            return false;
        }

        var ui = EnsureUI();
        if (ui == null) return false;

        _isOpen = true;
        ui.ShowWithCallbacks(candidates, OnPicked, Close);
        return true;
    }

    // ─── 후보 ────────────────────────────────────────────────────────
    private List<RewardCandidate> BuildCandidates()
    {
        var pool = new List<MainMinionDataSO>();
        if (choices != null)
            foreach (var m in choices) if (m != null && !pool.Contains(m)) pool.Add(m);

        if (pool.Count == 0)
        {
            var gm = GameManager.Instance;
            var registry = (gm != null && gm.dataManager != null) ? gm.dataManager.GET_GROWTH_REGISTRY() : null;
            if (registry != null && registry.minionDatas != null)
                foreach (var m in registry.minionDatas)
                    if (m is MainMinionDataSO main && !main.isEvolvedForm && !pool.Contains(main)) pool.Add(main); // 진화형은 시작 소환수가 아니다
        }

        var equipped = InventoryManager.Instance != null ? InventoryManager.Instance.MainSummon : null;
        var result = new List<RewardCandidate>();
        for (int i = 0; i < pool.Count && i < 3; i++) // 보상창 카드가 3장 고정이다
            result.Add(BuildCandidate(pool[i], pool[i] == equipped));
        return result;
    }

    private static RewardCandidate BuildCandidate(MainMinionDataSO m, bool isEquipped)
    {
        var baseData = m.rewardItemData;
        string title = (baseData != null && !string.IsNullOrEmpty(baseData.itemName)) ? baseData.itemName
                     : !string.IsNullOrEmpty(m.minionName) ? m.minionName : m.name;
        if (isEquipped) title += " (장착 중)";

        var sb = new System.Text.StringBuilder();
        if (m.minionSkill != null)
        {
            sb.Append("[스킬] ").Append(string.IsNullOrEmpty(m.minionSkill.skillName) ? m.minionSkill.name : m.minionSkill.skillName);
            if (!string.IsNullOrEmpty(m.minionSkill.description)) sb.Append('\n').Append(m.minionSkill.description);
            sb.Append("\n\n");
        }
        if (m.finisher != null) sb.Append("[일반 공격] ").Append(m.finisher.Describe()).Append('\n');
        if (m.dashModifier != null) sb.Append("[대쉬] ").Append(m.dashModifier.Describe());

        return new RewardCandidate
        {
            category = RewardCategory.Minion,
            rawData = m,
            displayData = new GrowthItemData
            {
                itemName = title,
                description = sb.ToString(),
                icon = (baseData != null && baseData.icon != null) ? baseData.icon : m.minionIcon,
            }
        };
    }

    // ─── 선택 결과 ───────────────────────────────────────────────────
    private void OnPicked(RewardCandidate candidate)
    {
        var minion = candidate.rawData as MainMinionDataSO;
        var inv = InventoryManager.Instance;
        if (minion != null && inv != null)
        {
            // 같은 소환수를 다시 골라도 '새로 시작'이다 — 강화 단계는 런마다 보상방에서 쌓는다.
            if (inv.EquipMinion(minion)) inv.ResetMainSummonEnhance();
            Debug.Log($"<color=cyan>[MinionSelectNPC]</color> 시작 소환수 선택: {minion.minionName}");
            FloatingTextManager.ShowOnPlayer($"{(string.IsNullOrEmpty(minion.minionName) ? minion.name : minion.minionName)} 획득", Color.white);
        }
        Close();
    }

    private void Close()
    {
        _isOpen = false;
        if (_ui != null) _ui.Hide();
    }

    // ─── UI ──────────────────────────────────────────────────────────
    private RewardSelectionUI EnsureUI()
    {
        if (_ui != null) return _ui;

        // 씬에 이미 보상창이 있으면 그걸 쓴다(나중에 마을에 직접 배치해도 동작하도록).
        _ui = Object.FindFirstObjectByType<RewardSelectionUI>(FindObjectsInactive.Include);
        if (_ui != null) return _ui;

        if (selectionUIPrefab == null)
        {
            Debug.LogWarning("[MinionSelectNPC] selectionUIPrefab 이 비어 있습니다.", this);
            return null;
        }

        Transform parent = UIPopUpManager.Instance != null ? UIPopUpManager.Instance.transform : null;
        if (parent == null)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            parent = canvas != null ? canvas.rootCanvas.transform : null;
        }
        if (parent == null)
        {
            Debug.LogWarning("[MinionSelectNPC] 보상창을 붙일 Canvas 를 찾지 못했습니다.", this);
            return null;
        }

        _ui = Instantiate(selectionUIPrefab, parent, false);
        _ui.transform.SetAsLastSibling(); // 다른 HUD 위에 그린다
        return _ui;
    }


}

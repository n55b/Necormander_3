using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// [디버그] 마을 NPC 창(F로 뜨는 ShopUI 패널)에 붙여, 장착 가능한 모든 플레이어 스킬 + 미니언을 나열한다.
/// 항목을 누르면 보상 때와 '동일한' 장착 흐름을 띄운다:
///   - 플레이어 스킬 → Q/E/R 슬롯 선택 UI(PlayerStateUI.OpenChangeSkillUI)
///   - 미니언       → 핸드 슬롯 선택 UI(HandSlotSelectionUI). 마을 HUD에 없으면 빈 슬롯에 자동 장착.
/// 게임 시작 전에 원하는 조합을 미리 세팅해보는 디버그 용도.
///
/// UI를 '맨땅에서' 만들지 않는다 — 인스펙터에 지정한 기존 버튼(itemButtonTemplate)을 항목마다 복제만 한다.
/// 창(이 오브젝트)이 켜질 때(OnEnable) 목록을 새로 채운다.
/// </summary>
public class VillageDebugLoadout : MonoBehaviour
{
    [Tooltip("항목 버튼들이 들어갈 부모(보통 ScrollView의 Content, GridLayoutGroup이 붙은 곳). 비우면 템플릿의 부모를 사용.")]
    [SerializeField] private Transform contentParent;
    [Tooltip("복제해 항목 버튼으로 쓸 템플릿 버튼(창에 이미 있는 Button_SkillSelect 등). 원본 자신은 숨겨진다.")]
    [SerializeField] private Button itemButtonTemplate;
    [SerializeField] private bool includePlayerSkills = true;
    [SerializeField] private bool includeMinions = true;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private void OnEnable() => Populate();

    private void Populate()
    {
        if (itemButtonTemplate == null) { Debug.LogWarning("[VillageDebugLoadout] itemButtonTemplate이 지정되지 않았습니다."); return; }
        if (contentParent == null) contentParent = itemButtonTemplate.transform.parent;

        // 이전 복제본 정리 (창을 여러 번 열어도 중복 누적 방지)
        for (int i = 0; i < _spawned.Count; i++) if (_spawned[i] != null) Destroy(_spawned[i]);
        _spawned.Clear();

        // 템플릿 원본은 숨김(복제 소스로만 사용)
        itemButtonTemplate.gameObject.SetActive(false);

        var gm = GameManager.Instance;
        var registry = (gm != null && gm.dataManager != null) ? gm.dataManager.GET_GROWTH_REGISTRY() : null;
        if (registry == null) { Debug.LogWarning("[VillageDebugLoadout] GrowthRegistry를 가져오지 못했습니다."); return; }

        if (includePlayerSkills && registry.playerSkills != null)
        {
            foreach (var skill in registry.playerSkills)
            {
                if (skill == null) continue;
                var s = skill; // 클로저 캡처 고정
                AddButton($"[스킬] {(string.IsNullOrEmpty(s.skillName) ? s.name : s.skillName)}", s.icon, s.description, () => EquipPlayerSkill(s));
            }
        }

        if (includeMinions && registry.minionDatas != null)
        {
            foreach (var minion in registry.minionDatas)
            {
                if (minion == null) continue;
                var m = minion;
                AddButton($"[미니언] {(string.IsNullOrEmpty(m.minionName) ? m.name : m.minionName)}", m.minionIcon, $"미니언 · {m.minionType}", () => EquipMinion(m));
            }
        }
    }

    private void AddButton(string label, Sprite icon, string tooltipDesc, UnityAction onClick)
    {
        GameObject go = Instantiate(itemButtonTemplate.gameObject, contentParent);
        go.SetActive(true);
        _spawned.Add(go);

        var tmp = go.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) tmp.text = label;
        else { var t = go.GetComponentInChildren<Text>(true); if (t != null) t.text = label; }

        var btn = go.GetComponent<Button>();
        SetIcon(go, btn, icon);

        // 마우스 따라다니는 호버 툴팁 — 실제 HUD 스킬 슬롯과 동일하게 SkillTooltipTrigger + CommonTooltipUI 사용.
        var trigger = go.GetComponent<SkillTooltipTrigger>() ?? go.AddComponent<SkillTooltipTrigger>();
        trigger.SetData(label, tooltipDesc);

        if (btn != null)
        {
            // 템플릿에 인스펙터로 걸려 있던 onClick(창 닫기 등)은 복제본에도 붙어오므로 전부 끈다.
            for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                btn.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(onClick);
            btn.interactable = true;
        }
    }

    /// <summary>아이콘을 행에 표시. 'Icon' 자식 Image가 있으면 거기, 없으면 버튼 배경이 아닌 자식 Image, 그것도 없으면 버튼 그래픽에 넣는다.</summary>
    private void SetIcon(GameObject go, Button btn, Sprite icon)
    {
        if (icon == null) return;
        Image target = null;

        var iconTr = go.transform.Find("Icon");
        if (iconTr != null) target = iconTr.GetComponent<Image>();

        if (target == null)
        {
            Image btnGraphic = btn != null ? btn.targetGraphic as Image : null;
            foreach (var img in go.GetComponentsInChildren<Image>(true))
            {
                if (img != btnGraphic) { target = img; break; }
            }
        }

        if (target == null && btn != null) target = btn.targetGraphic as Image;

        if (target != null)
        {
            target.sprite = icon;
            target.enabled = true;
            target.preserveAspect = true;
            if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);
        }
    }

    /// <summary>NPC 창(ShopUI 팝업)을 닫는다. 슬롯 픽커(HUD)가 창에 가려지지 않도록.</summary>
    private void CloseWindow()
    {
        // 팝업 매니저를 거치지 않고 창이 SetActive(true)된 경우(_currentPopUp 불일치)에도 반드시 닫히도록,
        // 매니저 상태와 무관하게 이 창 자체를 직접 끈다. 그 다음 매니저 상태/시간도 정리.
        gameObject.SetActive(false);
        if (UIPopUpManager.Instance != null) UIPopUpManager.Instance.ClosePopUpUI();
    }

    /// <summary>보상에서 플레이어 스킬을 골랐을 때와 동일: 소유 목록에 넣고 Q/E/R 슬롯 선택 UI를 띄운다.</summary>
    private void EquipPlayerSkill(PlayerSkillSO skill)
    {
        CloseWindow(); // 창을 닫아 HUD의 Q/E/R 슬롯 픽커가 가려지지 않게

        if (PlayerSkillInventoryManager.Instance != null)
            PlayerSkillInventoryManager.Instance.AddOwnedSkill(skill);

        var gm = GameManager.Instance;
        if (gm != null && gm.playerStateUI != null)
            gm.playerStateUI.OpenChangeSkillUI(skill); // 여기서 시간 정지 + 슬롯 픽커, 슬롯 클릭 시 CloseChangeSkillUI가 시간 재개
        else if (PlayerSkillInventoryManager.Instance != null)
            PlayerSkillInventoryManager.Instance.Equip(0, skill); // 픽커가 없으면 Q에 바로 장착
    }

    /// <summary>보상에서 미니언을 골랐을 때와 동일: 핸드 슬롯 선택 UI를 띄운다. 없으면 빈 슬롯에 자동 장착.</summary>
    private void EquipMinion(MinionDataSO minion)
    {
        if (HandSlotSelectionUI.Instance != null)
        {
            CloseWindow(); // 창을 닫고 핸드 슬롯 픽커를 띄운다 (보상 흐름과 동일)
            var candidate = new RewardCandidate
            {
                category = RewardCategory.Minion,
                rawData = minion,
                displayData = minion.rewardItemData
            };
            HandSlotSelectionUI.Instance.Show(candidate);
        }
        else
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.inventoryManager != null)
                gm.inventoryManager.AddMinionOrIncreaseQuantity(minion.minionType, 1);
        }
    }
}

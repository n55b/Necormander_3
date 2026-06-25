using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 스킬(Q/E/R) 장착을 관리하는 매니저.
///
/// InventoryManager와는 별개의 자원/슬롯을 사용합니다 (미니언 장착과 무관하게 항상 3개 다 채울 수 있음).
/// 다만 확인 편의를 위해 InventoryManager와 같은 GameObject에 컴포넌트로 같이 붙여서 사용합니다.
///
/// 패턴은 InventoryManager의 미니언 슬롯과 동일하게 갑니다:
///   - 이 매니저가 장착 데이터의 소스(source of truth)
///   - OnPlayerSkillUpdated 이벤트로 변경 알림
///   - PlayerSkillController가 이 이벤트를 구독해서 자기 캐시(equippedPlayerSkills)를 동기화
/// </summary>
public class PlayerSkillInventoryManager : MonoBehaviour
{
    public static PlayerSkillInventoryManager Instance;

    [Header("장착된 플레이어 스킬 (Q/E/R, 3개 고정)")]
    [SerializeField] private PlayerSkillSO[] equippedSkills = new PlayerSkillSO[3];

    [Header("보유 중인 플레이어 스킬 풀 (장착 후보)")]
    [SerializeField] private List<PlayerSkillSO> ownedSkills = new List<PlayerSkillSO>();

    public System.Action OnPlayerSkillUpdated;

    /// <summary>
    /// Called directly by GameManager during its init sequence (same timing as InventoryManager.Initialize()).
    /// Relying on Awake() would leave script execution order undefined, so Instance could still be null
    /// when PlayerSkillController tries to sync. This keeps it consistent with the rest of the codebase.
    /// </summary>
    public void Initialize()
    {
        Instance = this;
    }

    public PlayerSkillSO GetEquipped(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= equippedSkills.Length) return null;
        return equippedSkills[slotIndex];
    }

    /// <summary>슬롯에 스킬을 장착합니다. UI(스킬 선택창 등)에서 호출하세요.</summary>
    public void Equip(int slotIndex, PlayerSkillSO skill)
    {
        if (slotIndex < 0 || slotIndex >= equippedSkills.Length) return;
        equippedSkills[slotIndex] = skill;
        OnPlayerSkillUpdated?.Invoke();
        Debug.Log($"<color=cyan>[PlayerSkillInventoryManager]</color> Slot {slotIndex} <- {(skill != null ? skill.skillName : "Empty")}");
    }

    public void Unequip(int slotIndex)
    {
        Equip(slotIndex, null);
    }

    /// <summary>보유 스킬 풀에 추가 (룸 보상/파밍 등으로 새 스킬을 얻었을 때 호출)</summary>
    public void AddOwnedSkill(PlayerSkillSO skill)
    {
        if (skill == null || ownedSkills.Contains(skill)) return;
        ownedSkills.Add(skill);
        OnPlayerSkillUpdated?.Invoke();
    }

    public List<PlayerSkillSO> GetOwnedSkills() => ownedSkills;

    public void SaveToData(SaveData data)
    {
        data.equippedPlayerSkillNames = new List<string>();
        foreach (var s in equippedSkills)
            data.equippedPlayerSkillNames.Add(s != null ? s.name : "");

        data.ownedPlayerSkillNames = new List<string>();
        foreach (var s in ownedSkills)
            if (s != null) data.ownedPlayerSkillNames.Add(s.name);
    }

    public void LoadFromData(SaveData data)
    {
        if (data == null) return;

        var registry = GameManager.Instance != null && GameManager.Instance.dataManager != null
            ? GameManager.Instance.dataManager.GET_GROWTH_REGISTRY()
            : null;

        if (registry == null)
        {
            Debug.LogError("[PlayerSkillInventoryManager] GrowthRegistrySO is missing during LoadFromData!");
            return;
        }

        if (data.equippedPlayerSkillNames != null)
        {
            for (int i = 0; i < equippedSkills.Length; i++)
            {
                string skillName = i < data.equippedPlayerSkillNames.Count ? data.equippedPlayerSkillNames[i] : "";
                equippedSkills[i] = string.IsNullOrEmpty(skillName)
                    ? null
                    : registry.playerSkills.Find(s => s != null && s.name == skillName);
            }
        }

        ownedSkills.Clear();
        if (data.ownedPlayerSkillNames != null)
        {
            foreach (var skillName in data.ownedPlayerSkillNames)
            {
                var found = registry.playerSkills.Find(s => s != null && s.name == skillName);
                if (found != null) ownedSkills.Add(found);
            }
        }

        OnPlayerSkillUpdated?.Invoke();
    }

}

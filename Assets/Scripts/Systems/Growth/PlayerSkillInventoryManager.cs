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

    // [장비] 착용 중인 한 자루(런타임 전용, Unity 직렬화 안 함 → 없으면 null). 저장은 EquipmentSaveData 로.
    private EquipmentInstance _equipped;
    public EquipmentInstance EquippedEquipment => _equipped;

    /// <summary>
    /// Called directly by GameManager during its init sequence (same timing as InventoryManager.Initialize()).
    /// Relying on Awake() would leave script execution order undefined, so Instance could still be null
    /// when PlayerSkillController tries to sync. This keeps it consistent with the rest of the codebase.
    /// </summary>
    public void Initialize()
    {
        Instance = this;

        // Inspector에 저장된 배열 사이즈가 작아도(예: 슬롯 1개) 강제로 슬롯 3개로 보장합니다.
        if (equippedSkills == null || equippedSkills.Length != 3)
        {
            Debug.LogWarning($"<color=orange>[PlayerSkillInventoryManager]</color> equippedSkills 배열 크기가 {(equippedSkills == null ? "null" : equippedSkills.Length.ToString())}이어서 3으로 재조정합니다.");
            var resized = new PlayerSkillSO[3];
            if (equippedSkills != null)
            {
                for (int i = 0; i < equippedSkills.Length && i < 3; i++)
                    resized[i] = equippedSkills[i];
            }
            equippedSkills = resized;
        }
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

    // ── 장비 ─────────────────────────────────────────────────────────
    private static CharacterStat PlayerStat()
        => GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null
            ? GameManager.Instance.PLAYERCONTROLLER.Stat : null;

    /// <summary>
    /// 장비 한 자루를 착용한다. 기존 장비는 버려진다(한 자루 원칙).
    /// 굴려나온 스킬 2개가 각각 Q(0)/E(1) 로 들어가고, 패시브가 적용된다.
    /// </summary>
    public void EquipEquipment(EquipmentInstance inst)
    {
        var stat = PlayerStat();

        // 이전 장비 패시브 회수 (스탯 보정은 소스 키로 통째로 걷어낸다).
        if (_equipped != null && stat != null)
        {
            stat.Mods.RemoveSource(_equipped);
            if (_equipped.baseData != null && _equipped.baseData.passives != null)
                foreach (var p in _equipped.baseData.passives) p?.Remove(stat, _equipped);
        }

        _equipped = inst;

        // 굴린 스킬 → Q/E. 부족하면 그 슬롯은 비운다.
        Equip(0, inst != null && inst.rolledSkills.Count > 0 ? inst.rolledSkills[0] : null);
        Equip(1, inst != null && inst.rolledSkills.Count > 1 ? inst.rolledSkills[1] : null);

        // 새 장비 패시브 적용 (플레이어 스탯이 아직 없으면 스탯형은 스킵됨 — 로드 순서 대비 가드).
        if (inst != null && stat != null && inst.baseData != null && inst.baseData.passives != null)
            foreach (var p in inst.baseData.passives) p?.Apply(stat, inst, inst.enhanceLevel);
    }

    /// <summary>
    /// 착용 장비의 스탯 패시브를 현재 플레이어에게 (다시) 적용한다.
    /// 로드/씬 전환 때 EquipEquipment 는 플레이어가 아직 스폰 전이라 스탯 패시브를 못 붙인다
    /// (EquipEquipment 의 stat==null 가드로 스킵됨). 그래서 플레이어 스폰 '직후'(GameManager.SpawnPlayer)
    /// 이걸 불러 재적용한다. RemoveSource 를 먼저 해서 중복 호출에도 안전(멱등)하다.
    /// </summary>
    public void ReapplyEquipmentPassives()
    {
        var stat = PlayerStat();
        if (stat == null || _equipped == null || _equipped.baseData == null
            || _equipped.baseData.passives == null) return;

        stat.Mods.RemoveSource(_equipped); // 혹시 이미 붙어 있으면 걷어내고 다시(멱등)
        foreach (var p in _equipped.baseData.passives)
            p?.Apply(stat, _equipped, _equipped.enhanceLevel);
    }

    /// <summary>착용 장비가 해당 추가능력을 가졌는가(추가능력형 패시브 질의).</summary>
    public bool HasEquippedAbility(EquipmentAbilityType ability)
        => GetEquippedAbilityMagnitude(ability) != 0f;

    /// <summary>착용 장비의 해당 추가능력 세기 합. 없으면 0. (디버프 데미지↑ 등 소비처가 질의.)</summary>
    public float GetEquippedAbilityMagnitude(EquipmentAbilityType ability)
    {
        if (_equipped == null || _equipped.baseData == null || _equipped.baseData.passives == null) return 0f;
        float sum = 0f;
        foreach (var p in _equipped.baseData.passives)
            if (p is EquipmentAbilityPassive ap && ap.ability == ability) sum += ap.magnitude;
        return sum;
    }

    public void SaveToData(SaveData data)
    {
        data.equippedPlayerSkillNames = new List<string>();
        foreach (var s in equippedSkills)
            data.equippedPlayerSkillNames.Add(s != null ? s.name : "");

        data.ownedPlayerSkillNames = new List<string>();
        foreach (var s in ownedSkills)
            if (s != null) data.ownedPlayerSkillNames.Add(s.name);

        // [장비] 착용 중인 한 자루. 굴린 스킬 이름까지 저장 — 같은 SO 라도 조합이 다를 수 있어 SO 이름만으론 부족.
        if (_equipped != null && _equipped.baseData != null)
        {
            data.equipment = new EquipmentSaveData
            {
                equipmentSOName = _equipped.baseData.name,
                enhanceLevel = _equipped.enhanceLevel,
                rolledSkillNames = new List<string>()
            };
            foreach (var s in _equipped.rolledSkills)
                if (s != null) data.equipment.rolledSkillNames.Add(s.name);
        }
        else data.equipment = null;
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

        _equipped = null;

        // [장비] 있으면 그게 Q/E 의 소스다. 굴린 스킬 이름을 되살려 재장착(패시브 포함).
        bool equipmentLoaded = false;
        if (data.equipment != null && !string.IsNullOrEmpty(data.equipment.equipmentSOName)
            && registry.equipments != null)
        {
            var so = registry.equipments.Find(e => e != null && e.name == data.equipment.equipmentSOName);
            if (so != null)
            {
                var inst = new EquipmentInstance { baseData = so, enhanceLevel = data.equipment.enhanceLevel };
                if (data.equipment.rolledSkillNames != null)
                    foreach (var n in data.equipment.rolledSkillNames)
                    {
                        var sk = registry.playerSkills.Find(s => s != null && s.name == n);
                        if (sk != null) inst.rolledSkills.Add(sk);
                    }
                EquipEquipment(inst); // Q/E 세팅 + 패시브
                equipmentLoaded = true;
            }
        }

        // 구 세이브 폴백: 장비 데이터가 없으면 옛 방식(equippedPlayerSkillNames)으로 Q/E 복원.
        if (!equipmentLoaded && data.equippedPlayerSkillNames != null)
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

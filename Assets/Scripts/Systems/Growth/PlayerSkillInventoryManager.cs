using System.Collections.Generic;
using UnityEngine;

/// <summary>장비 착용/강화/패시브/저장 관리. 기존 씬 배선을 위해 클래스 이름은 유지한다.</summary>
public class PlayerSkillInventoryManager : MonoBehaviour
{
    public static PlayerSkillInventoryManager Instance;

    public System.Action OnEquipmentUpdated;

    // [장비] 착용 중인 한 자루(런타임 전용, Unity 직렬화 안 함 → 없으면 null). 저장은 EquipmentSaveData 로.
    private EquipmentInstance _equipped;
    public EquipmentInstance EquippedEquipment => _equipped;

    // [조건형 버프] 착용 장비의 EquipmentConditionBuffPassive 상태 추적 + 플레이어 피격 훅.
    private class BuffState { public EquipmentConditionBuffPassive passive; public float noHitTimer; public bool active; }
    private readonly List<BuffState> _buffs = new List<BuffState>();
    private CharacterHealth _hookedHealth;

    // [발동형 버프] 이벤트 트리거(밀침/끌기) → 다음 타격 소비.
    private class TriggerState { public EquipmentTriggerBuffPassive passive; public float timer; public bool active; }
    private readonly List<TriggerState> _triggers = new List<TriggerState>();

    /// <summary>
    /// GameManager 초기화 순서에서 InventoryManager와 함께 등록한다.
    /// </summary>
    public void Initialize() => Instance = this;

    // ── 장비 ─────────────────────────────────────────────────────────
    private static CharacterStat PlayerStat()
        => GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null
            ? GameManager.Instance.PLAYERCONTROLLER.Stat : null;

    /// <summary>
    /// 장비 한 자루를 착용한다. 기존 장비는 버려진다(한 자루 원칙).
    /// 장비 패시브만 적용하며 개인 액티브 스킬은 부여하지 않는다.
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

        // 새 장비 패시브 적용 (플레이어 스탯이 아직 없으면 스탯형은 스킵됨 — 로드 순서 대비 가드).
        if (inst != null && stat != null && inst.baseData != null && inst.baseData.passives != null)
            foreach (var p in inst.baseData.passives) p?.Apply(stat, inst, inst.enhanceLevel);

        RebuildBuffs();  // 옛 버프 스탯 회수 + 새 장비 버프 트래커 구성
        HookPlayerHit(); // 현재 플레이어 피격 이벤트로 (재)구독
        OnEquipmentUpdated?.Invoke();
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
        if (stat == null || _equipped == null || _equipped.baseData == null) return;

        stat.Mods.RemoveSource(_equipped); // 혹시 이미 붙어 있으면 걷어내고 다시(멱등)
        if (_equipped.baseData.passives != null)
            foreach (var p in _equipped.baseData.passives)
                p?.Apply(stat, _equipped, _equipped.enhanceLevel);

        RebuildBuffs();  // 새 플레이어라 버프 스탯이 사라졌으니 트래커 리셋(드라이버가 조건 맞으면 재적용)
        HookPlayerHit(); // 새 플레이어 피격 이벤트로 재구독
    }

    // ── 장비 강화 ─────────────────────────────────────────────────────
    /// <summary>착용 장비를 1강 올린다(상점 강화 아이템 구매 시). 미착용이거나 최대치면 false.
    /// 패시브를 새 강화레벨로 재적용한다(멱등).</summary>
    public bool EnhanceEquipped()
    {
        if (!CanEnhanceEquipped()) return false;
        _equipped.enhanceLevel++;
        ReapplyEquipmentPassives();      // 새 강화레벨로 패시브 다시 붙임(RemoveSource 후 재적용)
        OnEquipmentUpdated?.Invoke();  // HUD 강화표시 등 갱신
        string eqName = _equipped.baseData != null && !string.IsNullOrEmpty(_equipped.baseData.equipmentName)
            ? _equipped.baseData.equipmentName : (_equipped.baseData != null ? _equipped.baseData.name : "?");
        Debug.Log($"<color=cyan>[Enhance]</color> '{eqName}' 강화레벨 → {_equipped.enhanceLevel}/{(_equipped.baseData != null ? _equipped.baseData.maxEnhanceLevel : 0)}. 장비 패시브 갱신.");
        return true;
    }

    /// <summary>강화 가능 여부: 장비를 끼고 있고 아직 최대 강화레벨(EquipmentSO.maxEnhanceLevel) 미만.</summary>
    public bool CanEnhanceEquipped()
        => _equipped != null && _equipped.baseData != null
           && _equipped.enhanceLevel < _equipped.baseData.maxEnhanceLevel;

    // ── 조건부 버프 드라이버 ───────────────────────────────────────────
    private void Update()
    {
        if (_equipped == null) return;

        // 발동형 버프 타이머(스탯 불필요 — 이벤트로 켜지고 다음 타격 or 만료로 꺼진다).
        for (int i = 0; i < _triggers.Count; i++)
        {
            var tr = _triggers[i];
            if (tr.active) { tr.timer -= Time.deltaTime; if (tr.timer <= 0f) tr.active = false; }
        }

        if (_buffs.Count == 0) return;
        var stat = PlayerStat();
        if (stat == null) return;

        int level = _equipped.enhanceLevel;
        bool inCombat = LiveEnemyExists();

        foreach (var b in _buffs)
        {
            if (b.passive == null) continue;

            if (!inCombat)
            {
                if (b.active) { stat.Mods.RemoveSource(b.passive); b.active = false; }
                b.noHitTimer = 0f;
                continue;
            }

            b.noHitTimer += Time.deltaTime;
            if (!b.active && b.noHitTimer >= b.passive.EffectiveDelay(level))
            {
                if (b.passive.effects != null)
                    foreach (var e in b.passive.effects) e?.Apply(stat, b.passive, level);
                b.active = true;
            }
        }
    }

    /// <summary>플레이어가 실제 피해를 입으면 모든 조건부 버프 해제 + 재부여 타이머 리셋.</summary>
    private void OnPlayerDamaged(float amount)
    {
        if (amount <= 0f) return;
        var stat = PlayerStat();
        foreach (var b in _buffs)
        {
            if (b.active && stat != null) stat.Mods.RemoveSource(b.passive);
            b.active = false;
            b.noHitTimer = 0f;
        }
    }

    /// <summary>착용 장비의 버프 트래커를 새로 구성한다(옛 활성 버프 스탯은 먼저 회수).</summary>
    private void RebuildBuffs()
    {
        var stat = PlayerStat();
        foreach (var b in _buffs)
            if (b.active && stat != null) stat.Mods.RemoveSource(b.passive);
        _buffs.Clear();
        _triggers.Clear(); // 발동형은 스탯 모드가 아니라(데미지 소비형) 그냥 리셋

        if (_equipped == null || _equipped.baseData == null || _equipped.baseData.passives == null) return;
        foreach (var p in _equipped.baseData.passives)
        {
            if (p is EquipmentConditionBuffPassive bp) _buffs.Add(new BuffState { passive = bp });
            else if (p is EquipmentTriggerBuffPassive tp) _triggers.Add(new TriggerState { passive = tp });
        }
    }

    /// <summary>현재 플레이어의 피격 이벤트로 (재)구독. 층 이동 시 플레이어가 새로 생겨 매번 다시 걸어야 한다.</summary>
    private void HookPlayerHit()
    {
        var stat = PlayerStat();
        var health = stat != null ? stat.Health : null;
        if (_hookedHealth == health) return;
        if (_hookedHealth != null) _hookedHealth.OnDamageTaken -= OnPlayerDamaged;
        _hookedHealth = health;
        if (_hookedHealth != null) _hookedHealth.OnDamageTaken += OnPlayerDamaged;
    }

    private void OnDisable()
    {
        if (_hookedHealth != null) { _hookedHealth.OnDamageTaken -= OnPlayerDamaged; _hookedHealth = null; }
        SkillCombatUtil.OnEnemyDisplaced -= OnEnemyDisplaced;
        DamageEventBus.OnBeforeDamageCalculated -= HandleTriggerBeforeDamage;
    }

    /// <summary>"전투 중" 판정 = 살아있는 적이 하나라도 있는가(방에 적 있으면).</summary>
    private static bool LiveEnemyExists()
    {
        var list = CharacterStatus.ActiveEnemies;
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++) if (list[i] != null) return true;
        return false;
    }

    // ── 발동형 버프(투지) + 상태이상 발동(얼음) ─────────────────────────
    private void OnEnable()
    {
        SkillCombatUtil.OnEnemyDisplaced += OnEnemyDisplaced;
        DamageEventBus.OnBeforeDamageCalculated += HandleTriggerBeforeDamage;
    }

    private static bool IsPlayerObject(GameObject go)
        => go != null && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null
           && go == GameManager.Instance.PLAYERCONTROLLER.gameObject;

    /// <summary>밀침/끌기 발생 → 착용 장비의 발동형 버프를 켠다(비중첩, 지속만 갱신).</summary>
    private void OnEnemyDisplaced()
    {
        for (int i = 0; i < _triggers.Count; i++)
        {
            _triggers[i].active = true;
            _triggers[i].timer = _triggers[i].passive.buffDuration;
        }
    }

    /// <summary>다음 플레이어 기본/스킬 타격에 발동형 버프 데미지 보너스를 태우고 소비한다(대쉬 제외).</summary>
    private void HandleTriggerBeforeDamage(CharacterHealth target, ref DamageInfo info)
    {
        if (_triggers.Count == 0 || info.category == DamageCategory.DashAttack) return;
        if (!IsPlayerObject(info.attacker)) return;
        int level = _equipped != null ? _equipped.enhanceLevel : 0;
        for (int i = 0; i < _triggers.Count; i++)
        {
            var t = _triggers[i];
            if (!t.active) continue;
            info.amount *= (1f + t.passive.EffectiveBonus(level));
            t.active = false;
            t.timer = 0f;
        }
    }

    /// <summary>플레이어 Q/E 스킬 타격 시 착용 장비의 상태이상 확률을 굴려 target 에 부여(얼음 건틀릿 등).
    /// CharacterHealth 가 '데미지 뒤'에 호출 → 빙결 자가붕괴 없음. 슈퍼아머/내성은 ApplyStatus 가 알아서 막는다.</summary>
    public void TryProcEquipmentStatus(CharacterStatus targetStatus)
    {
        if (targetStatus == null || _equipped == null || _equipped.baseData == null
            || _equipped.baseData.passives == null) return;
        int level = _equipped.enhanceLevel;
        foreach (var p in _equipped.baseData.passives)
            if (p is EquipmentStatusPassive sp && sp.status != StatusType.None)
            {
                float chance = sp.EffectiveChance(level);
                if (chance > 0f && Random.value < chance)
                    targetStatus.ApplyStatus(sp.status);
            }
    }

    /// <summary>Q/E 스킬 타격이 해당 상태이상을 부여할 확률 보너스 합(강화 반영). 스킬이 굴릴 때 질의.</summary>
    public float GetStatusApplyChanceBonus(StatusType status) => SumStatus(status, true);

    /// <summary>해당 상태이상의 강도 보너스 합(강화 반영). DoT/지속 계산이 질의.</summary>
    public float GetStatusStrengthBonus(StatusType status) => SumStatus(status, false);

    private float SumStatus(StatusType status, bool chance)
    {
        if (_equipped == null || _equipped.baseData == null || _equipped.baseData.passives == null) return 0f;
        int level = _equipped.enhanceLevel;
        float sum = 0f;
        foreach (var p in _equipped.baseData.passives)
            if (p is EquipmentStatusPassive sp && sp.status == status)
                sum += chance ? sp.EffectiveChance(level) : sp.EffectiveStrength(level);
        return sum;
    }

    public void SaveToData(SaveData data)
    {
        data.equipment = _equipped != null && _equipped.baseData != null
            ? new EquipmentSaveData { equipmentSOName = _equipped.baseData.name, enhanceLevel = _equipped.enhanceLevel }
            : null;
    }

    public void LoadFromData(SaveData data)
    {
        if (data == null) return;
        var registry = GameManager.Instance != null && GameManager.Instance.dataManager != null
            ? GameManager.Instance.dataManager.GET_GROWTH_REGISTRY() : null;
        if (registry == null) return;
        var saved = data.equipment;
        var so = saved != null ? registry.equipments.Find(e => e != null && e.name == saved.equipmentSOName) : null;
        EquipEquipment(so != null ? new EquipmentInstance {
            baseData = so, enhanceLevel = Mathf.Clamp(saved.enhanceLevel, 0, so.maxEnhanceLevel)
        } : null);
        // 옛 세이브의 Q/E/rolledSkillNames는 읽지 않는다. 장비와 강화 수치는 유지한다.
    }
}

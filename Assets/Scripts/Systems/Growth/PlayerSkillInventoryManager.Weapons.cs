using System.Collections.Generic;
using UnityEngine;

/// <summary>기존 장비 관리자에 속한 콤보 무기 효과. 독립 스킬과 일반 미니언 공격에는 평타 효과를 걸지 않는다.</summary>
public partial class PlayerSkillInventoryManager
{
    public EquipmentSO Weapon => _equipped != null ? _equipped.baseData : null;
    public int ShadowStacks => _shadowStacks;
    public int PassionStacks => _passionStacks;
    public float BasicSpeedMultiplier => Weapon != null && Weapon.isRunWeapon
        ? Weapon.SpeedMultiplier * (1f + _passionStacks * Weapon.passionSpeedPerStack) : 1f;
    private int _shadowStacks, _passionStacks, _nextWeaponAction;
    private float _lastPassionHit;
    private readonly HashSet<int> _passionActions = new HashSet<int>();
    private readonly HashSet<CharacterStatus> _auraTargets = new HashSet<CharacterStatus>();
    private readonly List<CharacterStatus> _auraScratch = new List<CharacterStatus>();

    private void EnsureStartingWeapon()
    {
        if (Weapon != null && Weapon.isRunWeapon) return;
        var data = GameManager.Instance != null ? GameManager.Instance.dataManager : null;
        var registry = data != null ? data.GET_GROWTH_REGISTRY() : null;
        var start = registry != null ? registry.equipments.Find(e => e != null && e.isRunWeapon && e.upgradeTier == 0) : null;
        if (start != null) EquipEquipment(EquipmentInstance.Roll(start));
    }

    public bool CanUpgradeTo(EquipmentSO next) => CanEnhanceEquipped() && next != null
        && System.Array.IndexOf(Weapon.upgrades, next) >= 0;

    public bool UpgradeTo(EquipmentSO next)
    {
        if (!CanUpgradeTo(next)) return false;
        EquipEquipment(new EquipmentInstance { baseData = next, enhanceLevel = next.upgradeTier });
        return true;
    }

    public int BeginWeaponAction() => ++_nextWeaponAction;

    /// <summary>타격 생성 시 스냅샷. 열정의 최대 스택 추가 피해는 다음 공격부터, 다단의 각 타격에 적용한다.</summary>
    public void ConfigureBasicHit(ref DamageInfo info, int actionId)
    {
        if (Weapon == null || !Weapon.isRunWeapon) return;
        info.weaponActionId = actionId;
        bool maximum = Weapon.effect == EquipmentSO.Effect.Passion && _passionStacks >= Weapon.passionMaxStacks;
        if (maximum) info.amount += Weapon.passionMaxFlatDamage;
        info.hitstunDuration = Weapon.StunDuration(maximum);
        info.causesHitstun = info.hitstunDuration > 0f;
        if (Weapon.hitstun == EquipmentSO.Stagger.None)
        {
            info.knockbackForce = 0f;
            info.superArmorDamage = 0f;
        }
    }

    private void OnWeaponHit(CharacterHealth target, DamageInfo info)
    {
        if (Weapon == null || !Weapon.isRunWeapon || info.weaponActionId == 0
            || info.category != DamageCategory.BasicAttack || !IsPlayerObject(info.attacker)
            || target == null || target.Stat == null || !target.Stat.IsEnemy) return;
        if (Weapon.effect == EquipmentSO.Effect.Passion)
        {
            _lastPassionHit = Time.time;
            if (_passionActions.Add(info.weaponActionId))
                _passionStacks = Mathf.Min(Weapon.passionMaxStacks, _passionStacks + 1);
        }
        if (Weapon.effect == EquipmentSO.Effect.Frost && target.CurHP > 0f)
            target.Stat.Status.AddWeaponFrostStack(Weapon, info.attacker);
    }

    private void OnWeaponKill(CharacterHealth target, DamageInfo info)
    {
        if (Weapon == null || Weapon.effect != EquipmentSO.Effect.Shadow || target == null
            || target.Stat == null || !target.Stat.IsEnemy) return;
        bool allied = DamageRules.IsPlayerSourced(info.category);
        var source = info.attacker != null ? SkillCombatUtil.ResolveEntityTransform(info.attacker.transform) : null;
        if (!allied && source != null)
            allied = source.gameObject.layer == Layers.Player || source.gameObject.layer == Layers.PlayerDash || source.GetComponent<BaseEntity>()?.team == Team.Ally;
        if (!allied) return;
        var tier = target.Stat.Status.Tier;
        var count = Weapon.shadowKillStacks;
        _shadowStacks += tier == EnemyTier.Boss ? count.z : tier == EnemyTier.Elite ? count.y : count.x;
        ApplyShadowStats();
    }

    public static int RemainingShadowStacks(int stacks, float lossRatio)
        => Mathf.Max(0, stacks - Mathf.FloorToInt(stacks * Mathf.Clamp01(lossRatio)));

    private void ApplyShadowStats()
    {
        var stat = PlayerStat();
        if (stat == null) return;
        stat.Mods.RemoveSource(this);
        if (Weapon != null && Weapon.effect == EquipmentSO.Effect.Shadow)
            stat.Mods.AddFlat(this, StatType.Attack, _shadowStacks * Weapon.shadowAttackPerStack);
    }

    private void UpdateWeapon()
    {
        if (Weapon == null) return;
        if (_passionStacks > 0 && Time.time - _lastPassionHit >= Weapon.passionDuration)
        { _passionStacks = 0; _passionActions.Clear(); }
        var player = PlayerStat();
        float radius = Weapon.frostAuraRadius;
        _auraScratch.Clear();
        foreach (var target in _auraTargets)
            if (target == null || !target.isActiveAndEnabled || player == null || radius <= 0f
                || ((Vector2)(target.transform.position - player.transform.position)).sqrMagnitude > radius * radius)
                _auraScratch.Add(target);
        foreach (var target in _auraScratch)
        { if (target != null) target.SetFrostAura(0f); _auraTargets.Remove(target); }
        if (player == null || radius <= 0f) return;
        foreach (var target in CharacterStatus.ActiveEnemies)
        {
            if (target == null || !target.isActiveAndEnabled
                || ((Vector2)(target.transform.position - player.transform.position)).sqrMagnitude > radius * radius) continue;
            if (_auraTargets.Add(target)) target.SetFrostAura(Weapon.frostAuraReduction);
        }
    }

    private void ClearFrostAura()
    {
        foreach (var target in _auraTargets) if (target != null) target.SetFrostAura(0f);
        _auraTargets.Clear();
    }
}

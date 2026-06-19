using System.Collections.Generic;
using UnityEngine;

public class CharacterStatus : MonoBehaviour
{
    private class SlowInstance { public string EffectId; public float Reduction; public float EndTime; }
    private class SpeedBuffInstance { public string EffectId; public float Increase; public float EndTime; }
    private class ShieldInstance { public float RemainingAmount; public float EndTime; public ShieldInstance(float amount, float duration){ RemainingAmount = amount; EndTime = Time.time + duration; }}

    private List<SlowInstance> _activeSlows = new List<SlowInstance>();
    private List<SpeedBuffInstance> _activeSpeedBuffs = new List<SpeedBuffInstance>();
    private List<ShieldInstance> _shieldInstances = new List<ShieldInstance>();
    private float _cachedMoveSpeedMultiplier = 1f;
    private float _cachedTotalShield = 0f;

    public bool LastAttackMissed = false; // [추가] 궁수 발이부중 기믹용 (이전 기본 공격이 빗나갔는지 여부)

    public float MoveSpeedMultiplier => _cachedMoveSpeedMultiplier;
    public float TotalShield => _cachedTotalShield;
    public bool IsElite { get; set; } // [추가] 엘리트 유닛 여부

    private Dictionary<DebuffBoolType, float> _boolTimers = new Dictionary<DebuffBoolType, float>();
    private Dictionary<DebuffBoolType, int> _boolTiers = new Dictionary<DebuffBoolType, int>();

    [Header("New Trigger System")]
    private int _vulnerabilityStacks = 0;
    private float _vulnerabilityTimer = 0f;

    private DebuffType _currentDebuffType = DebuffType.None;
    private int _debuffStackCount = 0;
    private float _debuffTimer = 0f;

    public int VulnerabilityStacks => _vulnerabilityStacks;
    public DebuffType CurrentDebuffType => _currentDebuffType;
    public int DebuffStackCount => _debuffStackCount;

    private const float TRIGGER_STACK_DURATION = 20.0f;

    private float _bleedTickTimer = 0f;

    private const float STACK_DURATION = 10.0f;
    [SerializeField] private Base_DebuffUITerminal debuffTerminal;
    
    private CharacterStat _stat;
    public static List<CharacterStatus> ActiveEnemies = new List<CharacterStatus>();
    public static List<CharacterStatus> ActiveAllies = new List<CharacterStatus>();

    // [유니크] 녹슬어 버린 갑옷 (RustedArmor) 타격 횟수 카운터
    public int CorrosionHitCount { get; set; } = 0;

    public void Init(CharacterStat stat)
    {
        _stat = stat;
        if (IsEnemyTarget)
        {
            if (!ActiveEnemies.Contains(this)) ActiveEnemies.Add(this);
        }
        else
        {
            if (!ActiveAllies.Contains(this)) ActiveAllies.Add(this);
        }
    }

    private void OnDestroy()
    {
        if (ActiveEnemies.Contains(this))
        {
            ActiveEnemies.Remove(this);
        }
        if (ActiveAllies.Contains(this))
        {
            ActiveAllies.Remove(this);
        }
    }

    private bool IsEnemyTarget => _stat != null && _stat.IsEnemy;

    private void Update()
    {
        UpdateInstances();
        UpdateDebuffs();
        UpdateTriggerStacks();
    }

    private void UpdateTriggerStacks()
    {
        if (_vulnerabilityTimer > 0)
        {
            _vulnerabilityTimer -= Time.deltaTime;
            if (_vulnerabilityTimer <= 0)
            {
                _vulnerabilityStacks = 0;
                debuffTerminal.RemoveIcon(DebuffStackType.Vulnerability);
            }
        }

        if (_debuffTimer > 0)
        {
            _debuffTimer -= Time.deltaTime;
            if (_debuffTimer <= 0)
            {
                if (debuffTerminal != null && _currentDebuffType != DebuffType.None)
                {
                    debuffTerminal.RemoveIcon(ConvertDebuffTypeToStackType(_currentDebuffType));
                }
                _debuffStackCount = 0;
                _currentDebuffType = DebuffType.None;
            }
        }
    }


    private void UpdateInstances()
    {
        float multiplier = 1.0f;
        for (int i = _activeSlows.Count - 1; i >= 0; i--)
        {
            if (Time.time > _activeSlows[i].EndTime) { _activeSlows.RemoveAt(i); continue; }
            multiplier *= (1.0f - _activeSlows[i].Reduction);
        }
        for (int i = _activeSpeedBuffs.Count - 1; i >= 0; i--)
        {
            if (Time.time > _activeSpeedBuffs[i].EndTime) { _activeSpeedBuffs.RemoveAt(i); continue; }
            multiplier *= (1.0f + _activeSpeedBuffs[i].Increase);
        }
        
        if (GetDebuffBool(DebuffBoolType.Fractured))
        {
            int tier = GetDebuffTier(DebuffBoolType.Fractured);
            float fracReduction = tier == 1 ? 0.12f : tier == 2 ? 0.24f : 0.36f;
            multiplier *= (1.0f - fracReduction);
        }

        _cachedMoveSpeedMultiplier = Mathf.Max(0.1f, multiplier);

        float sum = 0;
        for (int i = _shieldInstances.Count - 1; i >= 0; i--)
        {
            if (Time.time > _shieldInstances[i].EndTime || _shieldInstances[i].RemainingAmount <= 0) 
            { 
                if (Time.time > _shieldInstances[i].EndTime && _shieldInstances[i].RemainingAmount > 0)
                {
                    ExplodeExpiredShield(_shieldInstances[i].RemainingAmount);
                }
                _shieldInstances.RemoveAt(i); 
                continue; 
            }
            sum += _shieldInstances[i].RemainingAmount;
        }
        _cachedTotalShield = sum;
    }

    private void UpdateDebuffs()
    {
        float dt = Time.deltaTime;

        List<DebuffBoolType> boolKeys = new List<DebuffBoolType>(_boolTimers.Keys);
        foreach (var key in boolKeys)
        {
            if (_boolTimers[key] > 0)
            {
                _boolTimers[key] -= dt;
                if (_boolTimers[key] <= 0)
                {
                    _boolTimers[key] = 0f;
                    _boolTiers[key] = 0;
                    debuffTerminal.RemoveIcon(key); 
                }
            }
        }

        if (GetDebuffBool(DebuffBoolType.Bleeding))
        {
            _bleedTickTimer += dt;
            if (_bleedTickTimer >= 1.0f)
            {
                _bleedTickTimer -= 1.0f;
                int tier = GetDebuffTier(DebuffBoolType.Bleeding);
                float damage = 10f * (tier == 1 ? 0.24f : tier == 2 ? 0.36f : 0.48f);
                var health = GetComponentInChildren<CharacterHealth>();
                if (health != null) health.GetDamage(new DamageInfo(damage, DamageType.Bleed, null, false, 1f, false, "Bleed Tick"));
            }
        }
        else
        {
            _bleedTickTimer = 0f;
        }
    }

    public void ApplySlow(string id, float reduction, float duration)
    {
        var existing = _activeSlows.Find(s => s.EffectId == id);
        if (existing != null) { existing.Reduction = Mathf.Max(existing.Reduction, reduction); existing.EndTime = Time.time + duration; }
        else { _activeSlows.Add(new SlowInstance { EffectId = id, Reduction = reduction, EndTime = Time.time + duration }); }
    }

    public void ApplySpeedBuff(string id, float increase, float duration)
    {
        var existing = _activeSpeedBuffs.Find(s => s.EffectId == id);
        if (existing != null) { existing.Increase = Mathf.Max(existing.Increase, increase); existing.EndTime = Time.time + duration; }
        else { _activeSpeedBuffs.Add(new SpeedBuffInstance { EffectId = id, Increase = increase, EndTime = Time.time + duration }); }
    }

    public void AddShield(float amount, float duration) { _shieldInstances.Add(new ShieldInstance(amount, duration)); UpdateInstances(); }

    private void ExplodeExpiredShield(float amount)
    {
        if (IsEnemyTarget || InventoryManager.Instance == null) return;
        
        // [이벤트 버스] 쉴드 폭발 시 추가 이펙트 처리 (추후 연동 시 여기에 이벤트 추가)
    }

    public float ConsumeShield(float amount)
    {
        float remainingToConsume = amount;
        for (int i = 0; i < _shieldInstances.Count; i++)
        {
            float canTake = Mathf.Min(remainingToConsume, _shieldInstances[i].RemainingAmount);
            _shieldInstances[i].RemainingAmount -= canTake;
            remainingToConsume -= canTake;
            if (remainingToConsume <= 0) break;
        }
        UpdateInstances(); 
        return amount - remainingToConsume;
    }

    public void ApplyKnockback(Vector2 dir, float force, float duration = 0.15f, bool isIronMountain = false)
    {
        Rigidbody2D rb = GetComponentInParent<Rigidbody2D>();
        if (rb != null) StartCoroutine(KnockbackRoutine(rb, dir, force, duration, isIronMountain));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Rigidbody2D rb, Vector2 dir, float force, float duration, bool isIronMountain)
    {
        bool isPlayer = gameObject.CompareTag("Player");
        bool hasVanguard = false;
        
        // [이벤트 버스] 넉백 전처리: force, duration 변조 기능 추가 가능

        float knockbackSpeed = force * 2.0f;
        float elapsed = 0f;
        int wallMask = LayerMask.GetMask("Wall", "Obstacle");

        while (elapsed < duration)
        {
            if (rb == null) yield break;
            rb.linearVelocity = dir * knockbackSpeed;
            elapsed += Time.deltaTime;

            if (isIronMountain && !isPlayer)
            {
                Collider2D wallHit = Physics2D.OverlapCircle(transform.position, 0.4f, wallMask);
                if (wallHit != null)
                {
                    float damagePercent = IsElite ? 0.06f : 0.12f;
                    var health = GetComponentInChildren<CharacterHealth>();
                    if (health != null) health.GetDamage(new DamageInfo(health.CurHP * damagePercent, DamageType.Fixed, null));
                    break;
                }
            }

            if (hasVanguard)
            {
                Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, 1.0f);
                foreach(var hit in hits)
                {
                    if (hit.CompareTag("Enemy") && hit.TryGetComponent<CharacterStat>(out var enemyStat))
                    {
                        var pc = GameManager.Instance.PLAYERCONTROLLER;
                        float damage = 10f;
                        if (pc != null)
                        {
                            var pcStat = pc.GetComponent<CharacterStat>();
                            if (pcStat != null) damage = pcStat.ATK * 1.5f;
                        }
                        enemyStat.Health.GetDamage(new DamageInfo(damage, DamageType.Physical, gameObject));
                    }
                    if (hit.CompareTag("Ally") && hit.TryGetComponent<CharacterStatus>(out var allyStat))
                    {
                        // Vanguard 버프 로직 (간단히 이속 버프로 대체 가능)
                        // TODO: 회피율 및 이속 버프
                    }
                }
            }

            yield return null;
        }
        if (rb != null) rb.linearVelocity = Vector2.zero;
    }

    public void SetDebuffBool(DebuffBoolType type, float duration, int tier = 0)
    {
        if (!_boolTimers.ContainsKey(type)) _boolTimers[type] = 0f;
        _boolTimers[type] = Mathf.Max(_boolTimers[type], duration);
        _boolTiers[type] = tier;

        debuffTerminal.UpdateUI(type, tier); 
    }

    public bool GetDebuffBool(DebuffBoolType type)
    {
        return _boolTimers.ContainsKey(type) && _boolTimers[type] > 0;
    }

    public int GetDebuffTier(DebuffBoolType type)
    {
        return _boolTiers.ContainsKey(type) ? _boolTiers[type] : 0;
    }

    #region New Trigger System

        public void ApplyElementalDebuff(DebuffStackType type, int amount = 1, GameObject attacker = null)
    {
        if (_stat != null && _stat.IsDead) return;

        DebuffType newType = ConvertStackTypeToDebuffType(type);
        if (newType == DebuffType.None) return;

        if (_currentDebuffType != newType && _debuffStackCount > 0)
        {
            PopCurrentDebuff(attacker);
            // 부여된 다른 디버프는 스택을 터뜨리는 데 소모되고 증발함
            return;
        }
        else
        {
            if (_currentDebuffType == DebuffType.None)
            {
                _currentDebuffType = newType;
                _debuffStackCount = amount;
            }
            else
            {
                _debuffStackCount = Mathf.Min(3, _debuffStackCount + amount);
            }
            _debuffTimer = TRIGGER_STACK_DURATION;
        }

        if (debuffTerminal != null)
        {
            debuffTerminal.UpdateUI(ConvertDebuffTypeToStackType(_currentDebuffType), _debuffStackCount);
        }
    }

    private DebuffType ConvertStackTypeToDebuffType(DebuffStackType stackType)
    {
        switch(stackType)
        {
            case DebuffStackType.BloodPop: return DebuffType.BloodPop;
            case DebuffStackType.Bleed: return DebuffType.Bleed;
            case DebuffStackType.Wound: return DebuffType.Wound;
            case DebuffStackType.Corrosion: return DebuffType.Corrosion;
            case DebuffStackType.Fracture: return DebuffType.Fracture;
            default: return DebuffType.None;
        }
    }

    private DebuffStackType ConvertDebuffTypeToStackType(DebuffType type)
    {
        switch(type)
        {
            case DebuffType.BloodPop: return DebuffStackType.BloodPop;
            case DebuffType.Bleed: return DebuffStackType.Bleed;
            case DebuffType.Wound: return DebuffStackType.Wound;
            case DebuffType.Corrosion: return DebuffStackType.Corrosion;
            case DebuffType.Fracture: return DebuffStackType.Fracture;
            default: return DebuffStackType.BloodPop;
        }
    }

    private void PopCurrentDebuff(GameObject attacker)
    {
        if (_debuffStackCount == 0 || _currentDebuffType == DebuffType.None) return;

        float baseDmg = 10f;
        float burstDmg = 0f;
        int stacks = _debuffStackCount;

        switch (_currentDebuffType)
        {
            case DebuffType.BloodPop:
                burstDmg = baseDmg * (stacks == 1 ? 2.4f : stacks == 2 ? 3.6f : 4.8f);
                Collider2D[] cols = Physics2D.OverlapCircleAll(transform.position, 3f + (stacks * 0.5f), LayerMask.GetMask("Enemy"));
                foreach (var col in cols)
                {
                    var health = col.GetComponent<CharacterHealth>();
                    if (health != null) health.GetDamage(new DamageInfo(burstDmg, DamageType.BloodPop, attacker, false, 1f, false, "BloodPop Explosion"));
                }
                break;
            case DebuffType.Bleed:
                burstDmg = baseDmg * (stacks == 1 ? 1.6f : stacks == 2 ? 2.4f : 3.2f);
                GetComponent<CharacterHealth>().GetDamage(new DamageInfo(burstDmg, DamageType.Bleed, attacker, false, 1f, false, "Bleed Pop"));
                SetDebuffBool(DebuffBoolType.Bleeding, 10f, stacks);
                break;
            case DebuffType.Wound:
                burstDmg = baseDmg * (stacks == 1 ? 1.6f : stacks == 2 ? 2.4f : 3.2f);
                GetComponent<CharacterHealth>().GetDamage(new DamageInfo(burstDmg, DamageType.Wound, attacker, false, 1f, false, "Wound Pop"));
                SetDebuffBool(DebuffBoolType.Wounded, 10f, stacks);
                break;
            case DebuffType.Corrosion:
                burstDmg = baseDmg * (stacks == 1 ? 1.6f : stacks == 2 ? 2.4f : 3.2f);
                GetComponent<CharacterHealth>().GetDamage(new DamageInfo(burstDmg, DamageType.Corrosion, attacker, false, 1f, false, "Corrosion Pop"));
                SetDebuffBool(DebuffBoolType.Corroded, 15f, stacks);
                break;
            case DebuffType.Fracture:
                burstDmg = baseDmg * 1.3f;
                GetComponent<CharacterHealth>().GetDamage(new DamageInfo(burstDmg, DamageType.Fracture, attacker, false, 1f, false, "Fracture Pop"));
                float fracDur = stacks == 1 ? 6f : stacks == 2 ? 7f : 8f;
                SetDebuffBool(DebuffBoolType.Fractured, fracDur, stacks);
                break;
        }

        if (debuffTerminal != null && _currentDebuffType != DebuffType.None)
        {
            debuffTerminal.RemoveIcon(ConvertDebuffTypeToStackType(_currentDebuffType));
        }

        _debuffStackCount = 0;
        _currentDebuffType = DebuffType.None;
        _debuffTimer = 0f;
    }

    #endregion

    // === Restored Methods for Compatibility & Triggers ===
    public void ApplyVulnerability(bool isPlayerApplied = false)
    {
        if (_stat != null && _stat.IsDead) return;

        if (_vulnerabilityStacks < 3)
            _vulnerabilityStacks++;
        
        _vulnerabilityTimer = TRIGGER_STACK_DURATION;
        debuffTerminal.UpdateUI(DebuffStackType.Vulnerability, _vulnerabilityStacks);

        if (isPlayerApplied && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.Vulnerability, transform);
        }
    }

    public void ConsumeVulnerability(SkillKeyword consumeType, GameObject attacker = null, bool isPlayerApplied = false)
    {
        if (consumeType == SkillKeyword.Stun)
        {
            if (_vulnerabilityStacks == 0)
            {
                ApplyVulnerability(isPlayerApplied);
            }
            else
            {
                float dmg = 10f * (_vulnerabilityStacks == 1 ? 1.0f : _vulnerabilityStacks == 2 ? 1.5f : 2.0f);
                float duration = _vulnerabilityStacks == 1 ? 0.5f : _vulnerabilityStacks == 2 ? 1.0f : 1.5f;

                _vulnerabilityStacks = 0;
                _vulnerabilityTimer = 0f;
                debuffTerminal.RemoveIcon(DebuffStackType.Vulnerability);
                DamageInfo stunDmg = new DamageInfo(dmg, DamageType.Fixed, attacker, false, 1f, false, "Vulnerability Stun");
                GetComponent<CharacterHealth>().GetDamage(stunDmg);

                if (duration > 0f) SetDebuffBool(DebuffBoolType.Stunned, duration);

                if (isPlayerApplied && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
                {
                    GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(consumeType, transform);
                }
            }
        }
        else if (consumeType == SkillKeyword.Strike)
        {
            if (_vulnerabilityStacks > 0)
            {
                _vulnerabilityStacks = 0;
                _vulnerabilityTimer = 0f;
                debuffTerminal.RemoveIcon(DebuffStackType.Vulnerability);

                if (isPlayerApplied && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
                {
                    GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(consumeType, transform);
                }
            }
        }
        else if (consumeType == SkillKeyword.Smash)
        {
            if (_vulnerabilityStacks > 0)
            {
                float dmg = 10f * (_vulnerabilityStacks == 1 ? 3.0f : _vulnerabilityStacks == 2 ? 4.5f : 6.0f);
                DamageInfo smashDmg = new DamageInfo(dmg, DamageType.Fixed, attacker, false, 1f, false, "Vulnerability Smash");
                GetComponent<CharacterHealth>().GetDamage(smashDmg);

                _vulnerabilityStacks = 0;
                _vulnerabilityTimer = 0f;
                debuffTerminal.RemoveIcon(DebuffStackType.Vulnerability);

                if (isPlayerApplied && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
                {
                    GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(consumeType, transform);
                }
            }
        }
    }

    public void ApplyStatusEffect(SkillKeyword statusType, GameObject attacker = null, bool isPlayerApplied = false)
    {
        if (_debuffStackCount > 0)
        {
            PopCurrentDebuff(attacker);
        }

        if (statusType == SkillKeyword.Stun)
        {
            ConsumeVulnerability(SkillKeyword.Stun, attacker, isPlayerApplied);
        }

        if (isPlayerApplied && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(statusType, transform);
        }
    }

    public void ApplyDebuff(DebuffType type, GameObject attacker = null, bool isPlayerApplied = false)
    {
        // Redirect to ApplyElementalDebuff logic
        DebuffStackType stackType = DebuffStackType.BloodPop; // Default fallback
        if (type == DebuffType.BloodPop) stackType = DebuffStackType.BloodPop;
        else if (type == DebuffType.Bleed) stackType = DebuffStackType.Bleed;
        else if (type == DebuffType.Wound) stackType = DebuffStackType.Wound;
        else if (type == DebuffType.Corrosion) stackType = DebuffStackType.Corrosion;
        else if (type == DebuffType.Fracture) stackType = DebuffStackType.Fracture;
        
        ApplyElementalDebuff(stackType, 1, attacker);

        if (isPlayerApplied && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.Debuff, transform);
        }
    }

    // Temporary Obsolete Mappings to fix compile errors for deprecated scripts
    public void AddDebuffStack(DebuffStackType type, float amount)
    {
        // Just route it to ElementalDebuff if it's one of the new ones
        ApplyElementalDebuff(type, Mathf.CeilToInt(amount));
    }

    public int GetDebuffStack(DebuffStackType type)
    {
        // Return 0 for obsolete types to let them compile
        return 0;
    }
    // ==============================================================


    public void ClearStatus()
    {
        _activeSlows.Clear(); _shieldInstances.Clear(); _boolTimers.Clear();
        _cachedMoveSpeedMultiplier = 1f; _cachedTotalShield = 0f;

        // UI 갱신
        debuffTerminal.RemoveAll();
    }
}

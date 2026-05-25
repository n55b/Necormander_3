using System.Collections.Generic;
using UnityEngine;

public class CharacterStatus : MonoBehaviour
{
    private class SlowInstance { public string EffectId; public float Reduction; public float EndTime; }
    private class ShieldInstance { public float RemainingAmount; public float EndTime; public ShieldInstance(float amount, float duration){ RemainingAmount = amount; EndTime = Time.time + duration; }}

    private List<SlowInstance> _activeSlows = new List<SlowInstance>();
    private List<ShieldInstance> _shieldInstances = new List<ShieldInstance>();
    private float _cachedMoveSpeedMultiplier = 1f;
    private float _cachedTotalShield = 0f;

    public float MoveSpeedMultiplier => _cachedMoveSpeedMultiplier;
    public float TotalShield => _cachedTotalShield;
    public bool IsElite { get; set; } // [추가] 엘리트 유닛 여부

    private Dictionary<DebuffStackType, float> _debuffStacks = new Dictionary<DebuffStackType, float>();
    private Dictionary<DebuffStackType, float> _stackTimers = new Dictionary<DebuffStackType, float>();
    private Dictionary<DebuffBoolType, float> _boolTimers = new Dictionary<DebuffBoolType, float>();

    private const float STACK_DURATION = 10.0f;
    private float _poisonTimer = 0f;
    private const float POISON_INTERVAL = 3.0f;

    [SerializeField] private Base_DebuffUITerminal debuffTerminal;
    
    private CharacterStat _stat;

    public void Init(CharacterStat stat)
    {
        _stat = stat;
    }

    private bool IsEnemyTarget => _stat != null && _stat.IsEnemy;

    private void Update()
    {
        UpdateInstances();
        UpdateDebuffs();
    }


    private void UpdateInstances()
    {
        float multiplier = 1.0f;
        for (int i = _activeSlows.Count - 1; i >= 0; i--)
        {
            if (Time.time > _activeSlows[i].EndTime) { _activeSlows.RemoveAt(i); continue; }
            multiplier *= (1.0f - _activeSlows[i].Reduction);
        }
        _cachedMoveSpeedMultiplier = Mathf.Max(0.1f, multiplier);

        float sum = 0;
        for (int i = _shieldInstances.Count - 1; i >= 0; i--)
        {
            if (Time.time > _shieldInstances[i].EndTime || _shieldInstances[i].RemainingAmount <= 0) { _shieldInstances.RemoveAt(i); continue; }
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
                    _boolTimers[key] = 0;
                    // [추가] Bool 타입 디버프가 끝났을 때 UI 아이콘 제거
                    debuffTerminal.RemoveIcon(key); 
                }
            }
        }

        List<DebuffStackType> stackKeys = new List<DebuffStackType>(_stackTimers.Keys);
        foreach (var key in stackKeys)
        {
            if (_stackTimers[key] > 0)
            {
                _stackTimers[key] -= dt;
                if (_stackTimers[key] <= 0) 
                {
                    _stackTimers[key] = 0;
                    _debuffStacks[key] = 0;

                    // UI 갱신
                    debuffTerminal.RemoveIcon(key);
                }
            }
        }

        UpdatePoisonTick(dt);
    }

    private void UpdatePoisonTick(float dt)
    {
        int poisonStack = GetDebuffStack(DebuffStackType.Poison);
        if (poisonStack > 0)
        {
            float interval = GemRuleSystem.GetPoisonInterval(IsEnemyTarget);

            _poisonTimer += dt;
            if (_poisonTimer >= interval)
            {
                _poisonTimer = 0f;
                var health = GetComponentInChildren<CharacterHealth>();
                if (health != null) health.GetDamage(new DamageInfo(poisonStack, DamageType.Fixed, null));
            }
        }
        else { _poisonTimer = 0f; }
    }

    public void ApplySlow(string id, float reduction, float duration)
    {
        var existing = _activeSlows.Find(s => s.EffectId == id);
        if (existing != null) { existing.Reduction = Mathf.Max(existing.Reduction, reduction); existing.EndTime = Time.time + duration; }
        else { _activeSlows.Add(new SlowInstance { EffectId = id, Reduction = reduction, EndTime = Time.time + duration }); }
    }

    public void AddShield(float amount, float duration) { _shieldInstances.Add(new ShieldInstance(amount, duration)); UpdateInstances(); }

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

    public void ApplyKnockback(Vector2 dir, float force, float duration = 0.15f)
    {
        Rigidbody2D rb = GetComponentInParent<Rigidbody2D>();
        if (rb != null) StartCoroutine(KnockbackRoutine(rb, dir, force, duration));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Rigidbody2D rb, Vector2 dir, float force, float duration)
    {
        float knockbackSpeed = force * 2.0f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (rb == null) yield break;
            rb.linearVelocity = dir * knockbackSpeed;
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (rb != null) rb.linearVelocity = Vector2.zero;
    }

    public void AddDebuffStack(DebuffStackType type, float amount)
    {
        // [특수] 시리고 아린 뼈 (또는 기타 차단 로직)
        if (type == DebuffStackType.Chill && GemRuleSystem.ShouldBlockChill(GetDebuffBool(DebuffBoolType.Frozen), IsEnemyTarget))
        {
            return;
        }

        // [시너지] 중독 추가 스택 등 보정
        if (type == DebuffStackType.Poison)
        {
            amount = GemRuleSystem.ModifyIncomingPoisonStack(amount, IsEnemyTarget);
        }

        if (!_debuffStacks.ContainsKey(type)) _debuffStacks[type] = 0f;
        _debuffStacks[type] += amount;

        // [시너지] 유지시간 연장 보정
        float duration = STACK_DURATION;
        if (type == DebuffStackType.Poison) duration = GemRuleSystem.GetPoisonDuration(IsEnemyTarget);
        _stackTimers[type] = duration;
        
        float maxStack = GetMaxStack(type);
        _debuffStacks[type] = Mathf.Min(_debuffStacks[type], maxStack);

        // UI 업데이트
        debuffTerminal.UpdateUI(type, _debuffStacks[type]);

        // [로그 강화] 디버프 종류별 색상 지정
        string color = "white";
        switch (type)
        {
            case DebuffStackType.Poison: color = "#32CD32"; break; // LimeGreen
            case DebuffStackType.Chill: color = "#00BFFF"; break; // DeepSkyBlue
            case DebuffStackType.Execute: color = "#FF4500"; break; // OrangeRed
            case DebuffStackType.BloodPop: color = "#FF00FF"; break; // Magenta
            case DebuffStackType.Aging: color = "#BC8F8F"; break; // RosyBrown
        }

        Debug.Log($"<color={color}>[Debuff]</color> <b>{gameObject.name}</b>: {type} +{amount:F1} (Current: <b>{_debuffStacks[type]:F1}/{maxStack}</b>)");

        HandleStackTrigger(type);
    }

    private float GetMaxStack(DebuffStackType type)
    {
        switch (type)
        {
            case DebuffStackType.Poison: return 20f;
            case DebuffStackType.Chill: return GemRuleSystem.GetMaxChillStack(IsEnemyTarget);
            case DebuffStackType.Aging: return GemRuleSystem.GetMaxAgingStack(IsEnemyTarget);
            case DebuffStackType.BloodPop: return 1000f; 
            case DebuffStackType.Execute: return 1000f;
            default: return 999f;
        }
    }

    private void HandleStackTrigger(DebuffStackType type)
    {
        switch (type)
        {
            case DebuffStackType.Chill:
                float threshold = GemRuleSystem.GetMaxChillStack(IsEnemyTarget);
                if (_debuffStacks[type] >= threshold)
                {
                    SetDebuffBool(DebuffBoolType.Frozen, 3.0f);
                    
                    // [시너지] 환급 로직 적용
                    _debuffStacks[type] = GemRuleSystem.GetFreezeRefundStacks(IsEnemyTarget);
                    _stackTimers[type] = STACK_DURATION;

                    // [시너지] 동결 시 고정 피해 로직
                    if (GemRuleSystem.HasFreezeFixedDamage(IsEnemyTarget))
                    {
                        var health = GetComponentInChildren<CharacterHealth>();
                        if (health != null) health.GetDamage(new DamageInfo(threshold, DamageType.Fixed, null));
                    }
                }
                break;
            case DebuffStackType.Aging:
                // [유니크] 노인을 위한 나라는 없다: 즉사 체크
                if (GemRuleSystem.ShouldAgingInstaKill(_debuffStacks[type], IsEnemyTarget))
                {
                    if (!IsElite) 
                    {
                        var health = GetComponentInChildren<CharacterHealth>();
                        if (health != null) health.GetDamage(new DamageInfo(health.CurHP + 999f, DamageType.Fixed, null));
                    }
                }
                break;
        }
    }

    public int GetDebuffStack(DebuffStackType type)
    {
        return _debuffStacks.ContainsKey(type) ? Mathf.FloorToInt(_debuffStacks[type]) : 0;
    }

    public void SetDebuffBool(DebuffBoolType type, float duration)
    {
        if (!_boolTimers.ContainsKey(type)) _boolTimers[type] = 0f;
        _boolTimers[type] = Mathf.Max(_boolTimers[type], duration);

        // [추가] Bool 타입 디버프도 UI에 아이콘 표시 (부식 등)
        debuffTerminal.UpdateUI(type, 0f); 
        
        if (type == DebuffBoolType.Corroded)
        {
            Debug.Log($"<color=#FFD700>[Debuff]</color> <b>{gameObject.name}</b>: Corroded Applied! (Duration: {duration}s)");
        }
    }

    public bool GetDebuffBool(DebuffBoolType type)
    {
        return _boolTimers.ContainsKey(type) && _boolTimers[type] > 0;
    }

    public void ClearStatus()
    {
        _activeSlows.Clear(); _shieldInstances.Clear(); _debuffStacks.Clear(); _stackTimers.Clear(); _boolTimers.Clear();
        _cachedMoveSpeedMultiplier = 1f; _cachedTotalShield = 0f;

        // UI 갱신
        debuffTerminal.RemoveAll();
    }
}

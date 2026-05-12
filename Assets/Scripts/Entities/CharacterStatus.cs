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

    private Dictionary<DebuffStackType, float> _debuffStacks = new Dictionary<DebuffStackType, float>();
    private Dictionary<DebuffStackType, float> _stackTimers = new Dictionary<DebuffStackType, float>();
    private Dictionary<DebuffBoolType, float> _boolTimers = new Dictionary<DebuffBoolType, float>();

    private const float STACK_DURATION = 10.0f;
    private float _poisonTimer = 0f;
    private const float POISON_INTERVAL = 3.0f;

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
                if (_boolTimers[key] <= 0) _boolTimers[key] = 0;
            }
        }

        List<DebuffStackType> stackKeys = new List<DebuffStackType>(_stackTimers.Keys);
        foreach (var key in stackKeys)
        {
            if (_stackTimers[key] > 0)
            {
                _stackTimers[key] -= dt;
                if (_stackTimers[key] <= 0) { _stackTimers[key] = 0; _debuffStacks[key] = 0; }
            }
        }

        UpdatePoisonTick(dt);
    }

    private void UpdatePoisonTick(float dt)
    {
        int poisonStack = GetDebuffStack(DebuffStackType.Poison);
        if (poisonStack > 0)
        {
            float interval = POISON_INTERVAL;
            // [특수] 독의 치사량: 틱 횟수 증가 (인터벌 절반)
            if (InventoryManager.Instance != null && InventoryManager.Instance.HasSpecialTag("LethalDose"))
            {
                interval *= 0.5f;
            }

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
        // [특수] 시리고 아린 뼈: 동결 상태에서는 스택 추가되지 않음
        if (type == DebuffStackType.Chill && GetDebuffBool(DebuffBoolType.Frozen))
        {
            if (InventoryManager.Instance != null && InventoryManager.Instance.HasSpecialTag("AchingBones"))
                return;
        }

        if (!_debuffStacks.ContainsKey(type)) _debuffStacks[type] = 0f;
        _debuffStacks[type] += amount;

        _stackTimers[type] = STACK_DURATION;
        
        float maxStack = GetMaxStack(type);
        _debuffStacks[type] = Mathf.Min(_debuffStacks[type], maxStack);

        Debug.Log($"<color=green>[Debuff]</color> {gameObject.name}: {type} 스택 {GetDebuffStack(type)} 부여 (총 {Mathf.FloorToInt(_debuffStacks[type])} / {maxStack})");

        HandleStackTrigger(type);
    }

    private float GetMaxStack(DebuffStackType type)
    {
        switch (type)
        {
            case DebuffStackType.Poison: return 20f;
            case DebuffStackType.Chill: 
                float baseChill = 20f;
                if (InventoryManager.Instance != null && InventoryManager.Instance.HasSpecialTag("SlowlyFreezingFlower"))
                    baseChill += 10f;
                return baseChill;
            case DebuffStackType.Aging: 
                if (InventoryManager.Instance != null && InventoryManager.Instance.HasSpecialTag("NoCountryForOldMen"))
                    return 100f;
                return 25f;
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
                float threshold = GetMaxStack(DebuffStackType.Chill);
                if (_debuffStacks[type] >= threshold)
                {
                    SetDebuffBool(DebuffBoolType.Frozen, 3.0f);
                    
                    float resetStacks = 10f;
                    // [특수] 시리고 아린 뼈: 동결 시 한기 스택이 10부터 시작
                    _debuffStacks[type] = resetStacks;
                    _stackTimers[type] = STACK_DURATION;
                }
                break;
            case DebuffStackType.Aging:
                // [특수] 노인을 위한 나라는 없다: 100스택 시 즉사
                if (_debuffStacks[type] >= 100f && InventoryManager.Instance != null && InventoryManager.Instance.HasSpecialTag("NoCountryForOldMen"))
                {
                    if (!CompareTag("Boss")) 
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
    }

    public bool GetDebuffBool(DebuffBoolType type)
    {
        return _boolTimers.ContainsKey(type) && _boolTimers[type] > 0;
    }

    public void ClearStatus()
    {
        _activeSlows.Clear(); _shieldInstances.Clear(); _debuffStacks.Clear(); _stackTimers.Clear(); _boolTimers.Clear();
        _cachedMoveSpeedMultiplier = 1f; _cachedTotalShield = 0f;
    }
}

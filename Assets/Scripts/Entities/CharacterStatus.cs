using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 유닛의 상태 이상(보호막, 슬로우, 넉백 등)을 개별적으로 관리하는 컴포넌트입니다.
/// </summary>
public class CharacterStatus : MonoBehaviour
{
    private class SlowInstance
    {
        public string EffectId;
        public float Reduction;
        public float EndTime;
    }

    private class ShieldInstance
    {
        public float RemainingAmount;
        public float EndTime;

        public ShieldInstance(float amount, float duration)
        {
            RemainingAmount = amount;
            EndTime = Time.time + duration;
        }
    }

    private List<SlowInstance> _activeSlows = new List<SlowInstance>();
    private List<ShieldInstance> _shieldInstances = new List<ShieldInstance>();

    private float _cachedMoveSpeedMultiplier = 1f;
    private float _cachedTotalShield = 0f;

    public float MoveSpeedMultiplier => _cachedMoveSpeedMultiplier;
    public float TotalShield => _cachedTotalShield;

    // --- [신규] 스택 및 상태형 디버프 시스템 데이터 ---
    private Dictionary<DebuffStackType, float> _debuffStacks = new Dictionary<DebuffStackType, float>();
    private Dictionary<DebuffBoolType, float> _boolTimers = new Dictionary<DebuffBoolType, float>();

    private float _poisonTimer = 0f;
    private const float POISON_INTERVAL = 3.0f;

    private void Update()
    {
        UpdateInstances();
        UpdateDebuffs();
    }

    private void UpdateInstances()
    {
        // 1. 슬로우 만료 체크 및 캐싱
        float multiplier = 1.0f;
        for (int i = _activeSlows.Count - 1; i >= 0; i--)
        {
            if (Time.time > _activeSlows[i].EndTime)
            {
                _activeSlows.RemoveAt(i);
                continue;
            }
            multiplier *= (1.0f - _activeSlows[i].Reduction);
        }
        _cachedMoveSpeedMultiplier = Mathf.Max(0.1f, multiplier);

        // 2. 보호막 만료 및 수치 고갈 체크
        float sum = 0;
        for (int i = _shieldInstances.Count - 1; i >= 0; i--)
        {
            if (Time.time > _shieldInstances[i].EndTime)
            {
                _shieldInstances.RemoveAt(i);
                continue;
            }
            if (_shieldInstances[i].RemainingAmount <= 0)
            {
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

        // 1. 상태형 디버프(BoolType) 타이머 업데이트
        List<DebuffBoolType> boolKeys = new List<DebuffBoolType>(_boolTimers.Keys);
        foreach (var key in boolKeys)
        {
            if (_boolTimers[key] > 0)
            {
                _boolTimers[key] -= dt;
                if (_boolTimers[key] <= 0)
                {
                    _boolTimers[key] = 0;
                    Debug.Log($"<color=white>[Status]</color> {gameObject.name}: {key} 상태 해제.");
                }
            }
        }

        // 2. 중독 주기적 데미지 처리
        int poisonStack = GetDebuffStack(DebuffStackType.Poison);
        if (poisonStack > 0)
        {
            _poisonTimer += dt;
            if (_poisonTimer >= POISON_INTERVAL)
            {
                _poisonTimer = 0f;
                var health = GetComponentInChildren<CharacterHealth>();
                if (health != null)
                {
                    health.GetDamage(new DamageInfo(poisonStack, DamageType.Fixed, null));
                    Debug.Log($"<color=green>[Poison]</color> {gameObject.name}: 중독 데미지 {poisonStack} 입음.");
                }
            }
        }
        else { _poisonTimer = 0f; }
    }

    public void ApplySlow(string id, float reduction, float duration)
    {
        var existing = _activeSlows.Find(s => s.EffectId == id);
        if (existing != null)
        {
            existing.Reduction = Mathf.Max(existing.Reduction, reduction);
            existing.EndTime = Time.time + duration;
        }
        else
        {
            _activeSlows.Add(new SlowInstance { EffectId = id, Reduction = reduction, EndTime = Time.time + duration });
        }
    }

    public void AddShield(float amount, float duration)
    {
        _shieldInstances.Add(new ShieldInstance(amount, duration));
        UpdateInstances(); 
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

    // --- Public API: 스택형 디버프 ---

    public void AddDebuffStack(DebuffStackType type, float amount)
    {
        if (!_debuffStacks.ContainsKey(type)) _debuffStacks[type] = 0f;
        _debuffStacks[type] += amount;

        switch (type)
        {
            case DebuffStackType.Poison:
                _debuffStacks[type] = Mathf.Min(_debuffStacks[type], 20f);
                break;
            case DebuffStackType.Chill:
                if (_debuffStacks[type] >= 20f)
                {
                    SetDebuffBool(DebuffBoolType.Frozen, 3.0f);
                    _debuffStacks[type] = 10f; 
                }
                break;
            case DebuffStackType.Aging:
                _debuffStacks[type] = Mathf.Min(_debuffStacks[type], 25f);
                break;
        }
    }

    public int GetDebuffStack(DebuffStackType type)
    {
        return _debuffStacks.ContainsKey(type) ? Mathf.FloorToInt(_debuffStacks[type]) : 0;
    }

    // --- Public API: 상태형 디버프 (Bool) ---

    public void SetDebuffBool(DebuffBoolType type, float duration)
    {
        if (!_boolTimers.ContainsKey(type)) _boolTimers[type] = 0f;
        _boolTimers[type] = Mathf.Max(_boolTimers[type], duration);
        Debug.Log($"<color=magenta>[Status]</color> {gameObject.name}: {type} 상태 부여 ({duration}s)");
    }

    public bool GetDebuffBool(DebuffBoolType type)
    {
        return _boolTimers.ContainsKey(type) && _boolTimers[type] > 0;
    }

    public void ClearStatus()
    {
        _activeSlows.Clear();
        _shieldInstances.Clear();
        _debuffStacks.Clear();
        _boolTimers.Clear();
        _cachedMoveSpeedMultiplier = 1f;
        _cachedTotalShield = 0f;
    }
}

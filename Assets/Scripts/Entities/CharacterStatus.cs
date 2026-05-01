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

    private void Update()
    {
        UpdateInstances();
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

        // 2. 보호막 만료 체크 및 캐싱
        float sum = 0;
        for (int i = _shieldInstances.Count - 1; i >= 0; i--)
        {
            if (Time.time > _shieldInstances[i].EndTime)
            {
                _shieldInstances.RemoveAt(i);
                continue;
            }
            sum += _shieldInstances[i].RemainingAmount;
        }
        _cachedTotalShield = sum;
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

    public void ClearStatus()
    {
        _activeSlows.Clear();
        _shieldInstances.Clear();
        _cachedMoveSpeedMultiplier = 1f;
        _cachedTotalShield = 0f;
    }
}

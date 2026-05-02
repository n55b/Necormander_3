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

        // 2. 보호막 만료 및 수치 고갈 체크
        float sum = 0;
        for (int i = _shieldInstances.Count - 1; i >= 0; i--)
        {
            // 시간 만료
            if (Time.time > _shieldInstances[i].EndTime)
            {
                Debug.Log($"<color=orange>[Shield]</color> {gameObject.name}: 보호막 시간 만료로 소멸 (남았던 수치: {_shieldInstances[i].RemainingAmount:F1})");
                _shieldInstances.RemoveAt(i);
                continue;
            }
            // 수치 고갈
            if (_shieldInstances[i].RemainingAmount <= 0)
            {
                Debug.Log($"<color=red>[Shield]</color> {gameObject.name}: 보호막 파괴됨 (수치 고갈)");
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
        Debug.Log($"<color=cyan>[Shield]</color> {gameObject.name}: 보호막 부여됨. 수치: {amount:F1}, 지속시간: {duration}s");
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

    // [복구] 기존 리지드바디 직접 제어 방식으로 롤백
    public void ApplyKnockback(Vector2 dir, float force, float duration = 0.15f)
    {
        Rigidbody2D rb = GetComponentInParent<Rigidbody2D>();
        if (rb != null) StartCoroutine(KnockbackRoutine(rb, dir, force, duration));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Rigidbody2D rb, Vector2 dir, float force, float duration)
    {
        // [원복] 배율을 다시 2.0f로 되돌림
        float knockbackSpeed = force * 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (rb == null) yield break;
            
            // 매 프레임 속도를 강제로 고정하여 다른 이동 스크립트와의 간섭 방지
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

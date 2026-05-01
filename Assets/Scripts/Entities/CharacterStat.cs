using UnityEngine;

public class CharacterStat : MonoBehaviour
{
    [Header("캐릭터 기본 스탯")]
    [SerializeField] float MaxHP = 100f;
    [SerializeField] float curHP;
    [SerializeField] float Atk = 10f;
    [SerializeField] float AtkSpd = 1f;
    [SerializeField] float AtkRange = 2f;
    [SerializeField] float Def = 0f;
    [SerializeField] float MoveSpeed = 5f;
    
    // --- 슬로우 시스템 ---
    private class SlowInstance
    {
        public string EffectId;
        public float Reduction; // 감소율 (0.2 = 20% 감소)
        public float EndTime;
    }
    private System.Collections.Generic.List<SlowInstance> _activeSlows = new System.Collections.Generic.List<SlowInstance>();

    [SerializeField] bool isDead = false;
    [SerializeField] bool invincible = false;

    public event System.Action OnDamageTaken;

    public float MAXHP => MaxHP;
    public float CURHP => curHP;
    public float ATK => Atk;
    public float ATKSPD => AtkSpd;
    public float ATKRANGE => AtkRange;
    public float DEF => Def;
    
    public float MOVESPEED 
    {
        get 
        {
            float finalMultiplier = 1.0f;
            for (int i = _activeSlows.Count - 1; i >= 0; i--)
            {
                if (Time.time > _activeSlows[i].EndTime)
                {
                    _activeSlows.RemoveAt(i);
                    continue;
                }
                finalMultiplier *= (1.0f - _activeSlows[i].Reduction);
            }
            return MoveSpeed * Mathf.Max(0.1f, finalMultiplier);
        }
    }

    public bool IsDead => isDead;
    public bool Invincible { get { return invincible; } set { invincible = value; } }

    public void ApplySlow(string effectId, float reduction, float duration)
    {
        if (isDead) return;

        var existing = _activeSlows.Find(s => s.EffectId == effectId);
        if (existing != null)
        {
            existing.Reduction = Mathf.Max(existing.Reduction, reduction);
            existing.EndTime = Time.time + duration;
        }
        else
        {
            _activeSlows.Add(new SlowInstance 
            { 
                EffectId = effectId, 
                Reduction = reduction, 
                EndTime = Time.time + duration 
            });
        }
    }

    private SpriteRenderer _spriteRenderer;
    private Color _originalColor;
    private Coroutine _flashCoroutine;

    public Color OriginalColor => _originalColor;

    void Awake()
    {
        InitializeStats();
        _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (_spriteRenderer != null) _originalColor = _spriteRenderer.color;
    }

    void Update()
    {
        UpdateVFXStates();
    }

    public void InitializeStats(MinionDataSO data)
    {
        if (data != null)
        {
            MaxHP = data.maxHP;
            Atk = data.attack;
            AtkSpd = data.attackSpeed;
            AtkRange = data.attackRange;
            Def = data.defense;
            MoveSpeed = data.moveSpeed;
        }
        
        curHP = MaxHP;
    }

    public void ApplySplitStats()
    {
        MaxHP *= 0.5f;
        curHP *= 0.5f;
        Atk *= 0.5f;
        Debug.Log($"<color=orange>[CharacterStat]</color> 분신화 적용: MaxHP={MaxHP}, HP={curHP}, ATK={Atk}");
    }

    private void InitializeStats()
    {
        if (TryGetComponent<AllyController>(out var ally) && ally.MinionData != null)
        {
            InitializeStats(ally.MinionData);
        }
        else
        {
            curHP = MaxHP;
        }
    }

    // --- 보호막 시스템 (개별 관리형) ---
    [System.Serializable]
    private class ShieldInstance
    {
        public float OriginalAmount;
        public float RemainingAmount;
        public float EndTime;

        public ShieldInstance(float amount, float duration)
        {
            OriginalAmount = amount;
            RemainingAmount = amount;
            EndTime = Time.time + duration;
        }
    }
    private System.Collections.Generic.List<ShieldInstance> _shieldInstances = new System.Collections.Generic.List<ShieldInstance>();

    [SerializeField] float totalShieldAmount = 0f; 
    private GameObject _shieldVFXInstance;
    private GameObject _ccVFXInstance;

    public float SHIELDAMOUNT 
    {
        get 
        {
            float sum = 0;
            for (int i = _shieldInstances.Count - 1; i >= 0; i--)
            {
                if (Time.time <= _shieldInstances[i].EndTime)
                    sum += _shieldInstances[i].RemainingAmount;
            }
            return sum;
        }
    }

    public void SetShieldVFX(GameObject vfx)
    {
        if (_shieldVFXInstance != null) Destroy(_shieldVFXInstance);
        _shieldVFXInstance = vfx;
    }

    public void SetCCVFX(GameObject vfx)
    {
        if (_ccVFXInstance != null) Destroy(_ccVFXInstance);
        _ccVFXInstance = vfx;
    }

    public void ApplyKnockback(Vector2 dir, float force, float duration = 0.15f)
    {
        if (isDead) return;
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = GetComponentInChildren<Rigidbody2D>();
        if (rb != null) StartCoroutine(KnockbackRoutine(rb, dir, force, duration));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Rigidbody2D rb, Vector2 dir, float force, float duration)
    {
        float knockbackSpeed = force * 2.0f; 
        float elapsed = 0f;
        while (elapsed < duration)
        {
            rb.linearVelocity = dir * knockbackSpeed;
            elapsed += Time.deltaTime;
            yield return null;
        }
        rb.linearVelocity = Vector2.zero;
    }

    private void UpdateVFXStates()
    {
        float currentShield = SHIELDAMOUNT;
        totalShieldAmount = currentShield;

        if (currentShield < 0.01f && _shieldVFXInstance != null)
        {
            Destroy(_shieldVFXInstance);
            _shieldVFXInstance = null;
        }
        
        if (_activeSlows.Count == 0 && _ccVFXInstance != null)
        {
            Destroy(_ccVFXInstance);
            _ccVFXInstance = null;
        }
    }

    public void AddShield(float amount, float duration)
    {
        if (isDead) return;
        ShieldInstance newShield = new ShieldInstance(amount, duration);
        _shieldInstances.Add(newShield);
        Debug.Log($"<color=cyan>[Shield]</color> {gameObject.name} 보호막 {amount} 추가! (현재 총 합계: {SHIELDAMOUNT})");
        StartCoroutine(RemoveShieldAfterTime(newShield, duration));
    }

    private System.Collections.IEnumerator RemoveShieldAfterTime(ShieldInstance instance, float duration)
    {
        yield return new UnityEngine.WaitForSeconds(duration);
        if (_shieldInstances.Contains(instance))
        {
            float expiredAmount = instance.RemainingAmount;
            _shieldInstances.Remove(instance);
            UpdateVFXStates();
            if (expiredAmount > 0)
            {
                Debug.Log($"<color=gray>[Shield]</color> {gameObject.name} 보호막 만료로 {expiredAmount} 소멸. (남은 총량: {SHIELDAMOUNT})");
            }
        }
    }

    private System.Collections.IEnumerator Debug_FlashBlue()
    {
        _spriteRenderer.color = Color.cyan;
        yield return new UnityEngine.WaitForSeconds(0.1f);
        _spriteRenderer.color = _originalColor;
        _flashCoroutine = null;
    }

    public void BreakShield(float amount)
    {
        if (isDead) return;
        float remainingToBreak = amount;
        for (int i = 0; i < _shieldInstances.Count; i++)
        {
            float canTake = Mathf.Min(remainingToBreak, _shieldInstances[i].RemainingAmount);
            _shieldInstances[i].RemainingAmount -= canTake;
            remainingToBreak -= canTake;
            if (remainingToBreak <= 0) break;
        }
        if (_spriteRenderer != null)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(Debug_FlashBlue());
        }
        UpdateVFXStates();
        Debug.Log($"<color=cyan>[Shield]</color> {gameObject.name}의 보호막 {amount} 차감 시도! 남은 총 합계: {SHIELDAMOUNT}");
    }

    public void GetDamage(DamageInfo info)
    {
        if (isDead || invincible) return;
        float remainingDamage = info.amount;
        float currentShield = SHIELDAMOUNT;
        if (currentShield > 0 && info.type != DamageType.Fixed)
        {
            float absorbed = Mathf.Min(remainingDamage, currentShield);
            float tempDamage = absorbed;
            for (int i = 0; i < _shieldInstances.Count; i++)
            {
                float canTake = Mathf.Min(tempDamage, _shieldInstances[i].RemainingAmount);
                _shieldInstances[i].RemainingAmount -= canTake;
                tempDamage -= canTake;
                if (tempDamage <= 0) break;
            }
            remainingDamage -= absorbed;
            if (_spriteRenderer != null)
            {
                if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
                _flashCoroutine = StartCoroutine(Debug_FlashBlue());
            }
            UpdateVFXStates();
        }

        if (remainingDamage > 0)
        {
            float finalDamage = remainingDamage;
            if (info.type != DamageType.Fixed)
            {
                finalDamage = Mathf.Max(remainingDamage - Def, 1f);
            }
            curHP -= finalDamage;
            OnDamageTaken?.Invoke();
            if (_spriteRenderer != null)
            {
                if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
                _flashCoroutine = StartCoroutine(Debug_FlashBlack());
            }
        }

        if (curHP <= 0.0f)
        {
            curHP = 0;
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (isDead) return;
        curHP = Mathf.Min(curHP + amount, MaxHP);
        if (_spriteRenderer != null)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(Debug_FlashGreen());
        }
    }

    private System.Collections.IEnumerator Debug_FlashGreen()
    {
        _spriteRenderer.color = Color.green;
        yield return new UnityEngine.WaitForSeconds(0.1f);
        _spriteRenderer.color = _originalColor;
        _flashCoroutine = null;
    }

    private System.Collections.IEnumerator Debug_FlashBlack()
    {
        _spriteRenderer.color = Color.black;
        yield return new UnityEngine.WaitForSeconds(0.1f);
        _spriteRenderer.color = _originalColor;
        _flashCoroutine = null;
    }

    public void ResetVisualFeedback()
    {
        if (_flashCoroutine != null)
        {
            StopCoroutine(_flashCoroutine);
            _flashCoroutine = null;
        }
        if (_spriteRenderer != null) _spriteRenderer.color = _originalColor;
    }

    private void Die()
    {
        isDead = true;
        if (GameManager.Instance != null && GameManager.Instance.economyManager != null)
        {
            if (TryGetComponent<BaseEntity>(out var entity))
            {
                bool isMinion = entity.team == Team.Ally && GetComponent<PlayerController>() == null;
                bool isEnemy = entity.team == Team.Enemy;
                if (isMinion || isEnemy)
                {
                    GameManager.Instance.economyManager.AddBonePoint(1);
                }
            }
        }
        Destroy(this.gameObject);
    }
}

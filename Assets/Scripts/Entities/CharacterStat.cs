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
                // [수정] 합연산이 아닌 곱연산 적용: 100%에서 Reduction만큼 차감한 비율을 계속 곱함
                finalMultiplier *= (1.0f - _activeSlows[i].Reduction);
            }
            // 최종 배율 (최소 0.1배속 보장)
            return MoveSpeed * Mathf.Max(0.1f, finalMultiplier);
        }
    }

    public bool IsDead => isDead;
    public bool Invincible { get { return invincible; } set { invincible = value; } }

    // --- 상태 이상 (슬로우 등) ---
    public void ApplySlow(string effectId, float reduction, float duration)
    {
        if (isDead) return;

        // 동일 ID 효과가 이미 있는지 확인
        var existing = _activeSlows.Find(s => s.EffectId == effectId);
        if (existing != null)
        {
            // 더 강한 수치로 갱신하거나, 지속 시간만 갱신
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

    // [DEBUG] Damage Flash 관련 변수
    private SpriteRenderer _spriteRenderer;
    private Color _originalColor;
    private Coroutine _flashCoroutine;

    void Awake()
    {
        InitializeStats();
        
        // [DEBUG] 자식 오브젝트를 포함하여 SpriteRenderer를 찾습니다.
        _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (_spriteRenderer != null) _originalColor = _spriteRenderer.color;
    }

    void Update()
    {
        // 매 프레임 효과 상태와 VFX를 동기화
        UpdateVFXStates();
    }

    /// <summary>
    /// 외부(AllyController 등)에서 데이터를 주입받아 스탯을 초기화합니다.
    /// </summary>
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

    /// <summary>
    /// 도적 전용 분신 기능: 체력과 공격력을 절반으로 낮춥니다.
    /// </summary>
    public void ApplySplitStats()
    {
        MaxHP *= 0.5f;
        curHP *= 0.5f;
        Atk *= 0.5f;
        
        Debug.Log($"<color=orange>[CharacterStat]</color> 분신화 적용: MaxHP={MaxHP}, HP={curHP}, ATK={Atk}");
    }

    private void InitializeStats()
    {
        // 1. 이미 인스펙터에 할당되어 있는 경우 (예비용)
        if (TryGetComponent<AllyController>(out var ally) && ally.MinionData != null)
        {
            InitializeStats(ally.MinionData);
        }
        else
        {
            curHP = MaxHP;
        }
    }

    [SerializeField] float shieldAmount = 0f; // 보호막 잔량 (임시 체력)
    private GameObject _shieldVFXInstance;
    private GameObject _ccVFXInstance;

    public float SHIELDAMOUNT => shieldAmount;

    /// <summary>
    /// 외부에서 생성한 VFX를 등록하여 상태와 동기화시킵니다.
    /// </summary>
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

    /// <summary>
    /// 외부의 물리적 충격에 의해 짧고 강하게 밀려납니다. (대시/넉백 통합)
    /// </summary>
    public void ApplyKnockback(Vector2 dir, float force, float duration = 0.15f)
    {
        if (isDead) return;
        
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = GetComponentInChildren<Rigidbody2D>();

        if (rb != null)
        {
            StartCoroutine(KnockbackRoutine(rb, dir, force, duration));
        }
    }

    private System.Collections.IEnumerator KnockbackRoutine(Rigidbody2D rb, Vector2 dir, float force, float duration)
    {
        // 1. 기존 속도 무시하고 강제 속도 설정
        float knockbackSpeed = force * 2.0f; 
        float elapsed = 0f;

        // 적군 AI 등이 속도를 방해하지 못하도록 짧은 시간 동안 제어권 획득
        while (elapsed < duration)
        {
            rb.linearVelocity = dir * knockbackSpeed;
            elapsed += Time.deltaTime;
            yield return null;
        }

        // 2. 종료 후 즉시 정지하여 미끄러짐 방지
        rb.linearVelocity = Vector2.zero;
    }

    private void UpdateVFXStates()
    {
        if (shieldAmount <= 0 && _shieldVFXInstance != null)
        {
            Destroy(_shieldVFXInstance);
        }
        
        // CC의 경우 현재 _activeSlows 리스트가 비어있으면 VFX 제거
        if (_activeSlows.Count == 0 && _ccVFXInstance != null)
        {
            Destroy(_ccVFXInstance);
        }
    }

    /// <summary>
    /// 일정 시간 동안 유지되는 보호막(임시 체력)을 추가합니다.
    /// </summary>
    public void AddShield(float amount, float duration)
    {
        if (isDead) return;

        shieldAmount += amount;
        Debug.Log($"<color=cyan>[Shield]</color> {gameObject.name} 보호막 {amount} 추가! (현재 총량: {shieldAmount})");

        // 지정된 시간 뒤에 추가된 만큼의 보호막을 제거하는 코루틴 시작
        StartCoroutine(RemoveShieldAfterTime(amount, duration));
    }

    private System.Collections.IEnumerator RemoveShieldAfterTime(float amount, float duration)
    {
        yield return new UnityEngine.WaitForSeconds(duration);
        
        if (!isDead && shieldAmount > 0)
        {
            float before = shieldAmount;
            // 추가했던 양만큼 제거하되, 이미 데미지를 입어 깎였다면 남은 양만 제거
            shieldAmount = Mathf.Max(0, shieldAmount - amount);
            
            UpdateVFXStates();

            if (before != shieldAmount)
            {
                Debug.Log($"<color=gray>[Shield]</color> {gameObject.name} 보호막 {amount} 만료. (남은 총량: {shieldAmount})");
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

    /// <summary>
    /// 외부에서 보호막을 강제로 제거할 때 호출합니다. (임시 체력 차감)
    /// </summary>
    public void BreakShield(float amount)
    {
        if (isDead) return;
        shieldAmount = Mathf.Max(0, shieldAmount - amount);
        
        // 시각 피드백
        if (_spriteRenderer != null)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(Debug_FlashBlue());
        }
        
        Debug.Log($"<color=cyan>[Shield]</color> {gameObject.name}의 보호막 {amount} 차감! 남은 총량: {shieldAmount}");
    }

    public void GetDamage(DamageInfo info)
    {
        if (isDead || invincible) return;

        float remainingDamage = info.amount;

        // 1. 보호막(방어도) 처리: 체력보다 먼저 차감
        if (shieldAmount > 0 && info.type != DamageType.Fixed)
        {
            if (shieldAmount >= remainingDamage)
            {
                // 보호막이 데미지를 모두 흡수
                shieldAmount -= remainingDamage;
                remainingDamage = 0;
                Debug.Log($"<color=cyan>[Shield]</color> 보호막이 모든 데미지를 흡수! (남은 보호막: {shieldAmount})");
            }
            else
            {
                // 보호막이 일부 흡수하고 파괴됨
                remainingDamage -= shieldAmount;
                shieldAmount = 0;
                Debug.Log($"<color=cyan>[Shield]</color> 보호막 파괴! 남은 데미지 {remainingDamage}가 체력에 적용됩니다.");
            }

            // 보호막 작동 시각 피드백 (파란색 깜빡임)
            if (_spriteRenderer != null)
            {
                if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
                _flashCoroutine = StartCoroutine(Debug_FlashBlue());
            }
        }

        // 2. 남은 데미지가 있다면 체력 차감
        if (remainingDamage > 0)
        {
            float finalDamage = remainingDamage;
            if (info.type != DamageType.Fixed)
            {
                finalDamage = Mathf.Max(remainingDamage - Def, 1f);
            }
            
            curHP -= finalDamage;
            Debug.Log($"<color=red>[Damage]</color> {gameObject.name} took {finalDamage:F1} damage. (HP: {curHP:F1}/{MaxHP})");
            OnDamageTaken?.Invoke();

            // 피해 시각 피드백 (검은색 깜빡임)
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
        Debug.Log($"<color=green>[Heal]</color> {gameObject.name} healed {amount:F1}. (HP: {curHP:F1}/{MaxHP})");
        
        // [DEBUG] 회복 시각 피드백 (녹색 깜빡임)
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

    // [DEBUG] 피해 발생 시 잠깐 검은색으로 변하게 하는 코루틴
    private System.Collections.IEnumerator Debug_FlashBlack()
    {
        _spriteRenderer.color = Color.black;
        yield return new UnityEngine.WaitForSeconds(0.1f);
        _spriteRenderer.color = _originalColor;
        _flashCoroutine = null;
    }

    private void Die()
    {
        isDead = true;

        // Bonepoint 재충전 로직 (아군 미니언 사망 시 +1, 적군 사망 시 +1)
        if (GameManager.Instance != null && GameManager.Instance.dataManager != null)
        {
            if (TryGetComponent<BaseEntity>(out var entity))
            {
                // 플레이어가 아닌 아군(미니언)이거나, 적군인 경우에만 Bonepoint 지급
                bool isMinion = entity.team == Team.Ally && GetComponent<PlayerController>() == null;
                bool isEnemy = entity.team == Team.Enemy;

                if (isMinion || isEnemy)
                {
                    GameManager.Instance.dataManager.AddBonePoint(1);
                    Debug.Log($"<color=white>[Bonepoint]</color> {gameObject.name} 사망으로 인해 Bonepoint 1 충전! (현재: {GameManager.Instance.dataManager.BONEPOINT})");
                }
            }
        }

        Destroy(this.gameObject);
    }
}

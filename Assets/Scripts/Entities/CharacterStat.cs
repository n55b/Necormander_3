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
            float totalReduction = 0f;
            for (int i = _activeSlows.Count - 1; i >= 0; i--)
            {
                if (Time.time > _activeSlows[i].EndTime)
                {
                    _activeSlows.RemoveAt(i);
                    continue;
                }
                totalReduction += _activeSlows[i].Reduction;
            }
            // 최종 배율 = 1.0 - 총 감소율 (최소 0.1배속 보장)
            float finalMultiplier = Mathf.Max(0.1f, 1.0f - totalReduction);
            return MoveSpeed * finalMultiplier;
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

    public void GetDamage(DamageInfo info)
    {
        if (isDead || invincible) return;

        float finalDamage = Mathf.Max(info.amount - Def, 1f);
        curHP -= finalDamage;

        OnDamageTaken?.Invoke();

        // [DEBUG] 피해 시각 피드백 (검은색 깜빡임)
        if (_spriteRenderer != null)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(Debug_FlashBlack());
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
        Destroy(this.gameObject);
    }
}

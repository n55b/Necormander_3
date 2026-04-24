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

    [SerializeField] int shieldCount = 0; // 보호막 스택 개수

    public int SHIELDCOUNT => shieldCount;

    // 보호막을 특정 개수만큼 깎는 함수
    public void BreakShield(int amount)
    {
        if (isDead) return;
        shieldCount = Mathf.Max(0, shieldCount - amount);
        
        // 보호막 파괴 시각 피드백 (파란색 깜빡임)
        if (_spriteRenderer != null)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(Debug_FlashBlue());
        }
        
        Debug.Log($"<color=cyan>[Shield]</color> {gameObject.name}의 보호막 {amount}개 파괴! 남은 개수: {shieldCount}");
    }

    private System.Collections.IEnumerator Debug_FlashBlue()
    {
        _spriteRenderer.color = Color.cyan;
        yield return new UnityEngine.WaitForSeconds(0.1f);
        _spriteRenderer.color = _originalColor;
        _flashCoroutine = null;
    }

    public void GetDamage(DamageInfo info)
    {
        if (isDead || invincible) return;

        // 보호막이 남아있다면 데미지를 1로 고정 (또는 무시 가능)
        float damageToDeal = info.amount;
        if (shieldCount > 0)
        {
            damageToDeal = 1f; // 보호막이 있으면 데미지를 최소화
            Debug.Log($"<color=cyan>[Shield]</color> 보호막이 데미지를 흡수합니다!");
        }

        float finalDamage = Mathf.Max(damageToDeal - Def, 1f);
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

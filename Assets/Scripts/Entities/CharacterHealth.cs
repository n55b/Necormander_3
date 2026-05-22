using UnityEngine;
using System;

/// <summary>
/// 유닛의 체력 관리와 데미지 계산, 사망 처리를 담당하는 컴포넌트입니다.
/// </summary>
public class CharacterHealth : MonoBehaviour
{
    private CharacterStat _stat;
    private CharacterStatus _status;

    [SerializeField] private float curHP;
    [SerializeField] private bool isDead = false;
    [SerializeField] private bool invincible = false;

    public event Action<float> OnDamageTaken;
    public event Action OnHeal;
    public event Action UpdateHPBar;
    public event Action OnDeath;

    public float CurHP => curHP;
    public float MaxHP => (_stat != null) ? _stat.MAXHP : 0f; // [추가] 최대 체력 정보 노출
    public bool IsDead => isDead;
    public bool Invincible { get { return invincible; } set { invincible = value; } }

    public event Action<int, string, bool> TakeDamageEvent;

    public void Init(CharacterStat stat, CharacterStatus status)
    {
        _stat = stat;
        _status = status;
        curHP = _stat.MAXHP;
        isDead = false;
    }

    public void GetDamage(DamageInfo info)
    {
        if (isDead || invincible) return;

        float remainingDamage = info.amount;
        string str = "";                             // 데미지 타입

        // [부식] 시너지에 따른 데미지 증가
        bool isEnemyTarget = (_stat != null && _stat.IsEnemy);
        float corrosionAmp = GemRuleSystem.GetCorrosionDamageAmp(isEnemyTarget);
        if (_status.GetDebuffBool(DebuffBoolType.Corroded))
        {
            // 부식 상태인 경우 시너지 보너스(25%, 40% 등)만큼 데미지 증폭
            remainingDamage *= (1.0f + corrosionAmp); 
            str = "Corroded";
        }

        // [쉴드] 적용
        if (info.type != DamageType.Fixed && _status != null && _status.TotalShield > 0)
        {
            float absorbed = _status.ConsumeShield(remainingDamage);
            remainingDamage -= absorbed;
            if (absorbed > 0) OnDamageTaken?.Invoke(0f);
            // 쉴드 데미지 별개로 표시
            TakeDamageEvent?.Invoke((int)absorbed, "Shield", false);
        }

        // [최종 데미지 및 체력 차감]
        if (remainingDamage > 0)
        {
            float finalDamage = (info.type != DamageType.Fixed) ? Mathf.Max(remainingDamage - _stat.DEF, 1f) : remainingDamage;

            // [추가] 플레이어 전용 규칙: 어떤 데미지를 입든 무조건 1씩 차감
            // (CharacterHealth가 자식 오브젝트에 있을 수 있으므로 root 태그 확인)
            if (gameObject.CompareTag("Player") || transform.root.CompareTag("Player"))
            {
                finalDamage = 1.0f;
            }

            Debug.Log($"{gameObject.name} took {finalDamage} damage. HP: {curHP} -> {curHP - finalDamage}");
            curHP -= finalDamage;
            OnDamageTaken?.Invoke(finalDamage);

            if(info.type == DamageType.Fixed)
                str = "Poison";
            
            TakeDamageEvent?.Invoke((int)finalDamage, str, false);
        }

        // [처형] 체크
        if (_status != null && !isDead)
        {
            int executeThreshold = _status.GetDebuffStack(DebuffStackType.Execute);
            if (executeThreshold > 0 && curHP > 0 && curHP <= executeThreshold)
            {
                TakeDamageEvent?.Invoke(executeThreshold, "Execution", false);

                curHP = 0;
            }
        }

        // [사망] 체크
        if (curHP <= 0.0f && !isDead) // isDead 체크로 중복 호출 방지
        {
            Die();
        }

        UpdateHPBar?.Invoke(); // HPBar 업데이트
    }

    public void Heal(float amount)
    {
        if (isDead) return;
        float oldHP = curHP;
        curHP = Mathf.Min(curHP + amount, _stat.MAXHP);
        Debug.Log($"{gameObject.name} healed for {amount}. HP: {oldHP} -> {curHP}");
        OnHeal?.Invoke();
        UpdateHPBar?.Invoke(); // [추가] HPBar 업데이트
    }

    private void Die()
    {
        if (isDead) return; // 중복 실행 방지
        isDead = true;

        // [비폭] 사망 시 최우선 발동
        if (_status != null)
        {
            int bloodPopStack = _status.GetDebuffStack(DebuffStackType.BloodPop);
            if (bloodPopStack > 0)
            {
                ExecuteBloodPop(bloodPopStack);
            }
        }

        OnDeath?.Invoke();

        BaseEntity rootEntity = GetComponentInParent<BaseEntity>();
        bool isPlayer = (rootEntity != null && rootEntity.CompareTag("Player")) || CompareTag("Player");
        
        if (rootEntity != null && rootEntity.team == Team.Ally && !isPlayer)
        {
            ReportDeathToManager(rootEntity);
        }
        
        if (!isPlayer)
        {
            Destroy(rootEntity != null ? rootEntity.gameObject : gameObject);
        }
    }

    private void ReportDeathToManager(BaseEntity rootEntity)
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            var allyManager = pc.GetComponentInChildren<AllyManager>() ?? FindFirstObjectByType<AllyManager>();
            if (allyManager != null && rootEntity != null) 
            {
                allyManager.ReportDeath(rootEntity.gameObject.GetInstanceID());
            }
        }
    }

    private void ExecuteBloodPop(int stacks)
    {
        bool isEnemyTarget = (_stat != null && _stat.IsEnemy);

        float baseRadius = 2.0f;
        float radiusMult = GemRuleSystem.GetBloodPopRadiusMultiplier(isEnemyTarget);
        float explosionRadius = baseRadius * radiusMult;

        float finalDamage = GemRuleSystem.GetBloodPopDamage(stacks, isEnemyTarget);

        // Bloodpop은 무조건 'Enemy' 레이어의 유닛에게만 데미지를 줍니다. (아군과 플레이어 제외)
        LayerMask bloodPopTargetLayer = LayerMask.GetMask("Enemy");

        var registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        if (registry != null && registry.bloodPopVFX != null)
        {
            GameObject vfx = Instantiate(registry.bloodPopVFX, transform.position, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * (explosionRadius * 2f);
            Destroy(vfx, 1.0f);
        }

        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, explosionRadius, bloodPopTargetLayer); 

        foreach (var col in colls)
        {
            var targetHealth = col.GetComponentInChildren<CharacterHealth>();
            if (targetHealth != null && !targetHealth.isDead)
            {
                // 데미지 팝업 등을 위해 이벤트 호출
                targetHealth.TakeDamageEvent?.Invoke((int)finalDamage, "BloodPop", false);
                targetHealth.GetDamage(new DamageInfo(finalDamage, DamageType.Fixed, this.gameObject));

                // [유니크] 살덩이가 폭발하는 것: 비폭 피해 대상에게 데미지의 일부만큼 비폭 스택 부여
                float chainRatio = GemRuleSystem.GetBloodPopChainRatio(isEnemyTarget);
                if (chainRatio > 0)
                {
                    var targetStatus = col.GetComponentInChildren<CharacterStatus>();
                    if (targetStatus != null)
                    {
                        targetStatus.AddDebuffStack(DebuffStackType.BloodPop, finalDamage * chainRatio);
                    }
                }
            }
        }
    }

    public void ResetHP()
    {
        curHP = _stat.MAXHP;
        isDead = false;
    }

    public void SetHP(float hp)
    {
        curHP = Mathf.Clamp(hp, 0f, _stat != null ? _stat.MAXHP : hp);
        isDead = curHP <= 0f;
        UpdateHPBar?.Invoke();
    }
}

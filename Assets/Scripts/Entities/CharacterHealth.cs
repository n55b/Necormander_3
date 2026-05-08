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
    public event Action OnDeath;

    public float CurHP => curHP;
    public bool IsDead => isDead;
    public bool Invincible { get { return invincible; } set { invincible = value; } }

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

        // [부식] 스택에 비례한 데미지 증가
        int corrodedStacks = _status.GetDebuffStack(DebuffStackType.Corroded);
        if (corrodedStacks > 0)
        {
            remainingDamage *= (1.0f + corrodedStacks * 0.01f); // 스택당 1%씩 피해량 증가
        }

        // [쉴드] 적용
        if (info.type != DamageType.Fixed && _status != null && _status.TotalShield > 0)
        {
            float absorbed = _status.ConsumeShield(remainingDamage);
            remainingDamage -= absorbed;
            if (absorbed > 0) OnDamageTaken?.Invoke(0f);
        }

        // [최종 데미지 및 체력 차감]
        if (remainingDamage > 0)
        {
            float finalDamage = (info.type != DamageType.Fixed) ? Mathf.Max(remainingDamage - _stat.DEF, 1f) : remainingDamage;
            Debug.Log($"{gameObject.name} took {finalDamage} damage. HP: {curHP} -> {curHP - finalDamage}");
            curHP -= finalDamage;
            OnDamageTaken?.Invoke(finalDamage);
        }

        // [처형] 체크
        if (_status != null && !isDead)
        {
            int executeThreshold = _status.GetDebuffStack(DebuffStackType.Execute);
            if (executeThreshold > 0 && curHP > 0 && curHP <= executeThreshold)
            {
                curHP = 0;
            }
        }

        // [사망] 체크
        if (curHP <= 0.0f && !isDead) // isDead 체크로 중복 호출 방지
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (isDead) return;
        float oldHP = curHP;
        curHP = Mathf.Min(curHP + amount, _stat.MAXHP);
        Debug.Log($"{gameObject.name} healed for {amount}. HP: {oldHP} -> {curHP}");
        OnHeal?.Invoke();
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

    private void ExecuteBloodPop(int damage)
    {
        float explosionRadius = 2.0f;
        // Bloodpop은 무조건 'Enemy' 레이어의 유닛에게만 데미지를 줍니다. (아군과 플레이어 제외)
        LayerMask bloodPopTargetLayer = LayerMask.GetMask("Enemy");

        var registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        if (registry != null && registry.bloodPopVFX != null)
        {
            GameObject vfx = Instantiate(registry.bloodPopVFX, transform.position, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * (explosionRadius * 2f);
            Destroy(vfx, 1.0f);
        }

        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, explosionRadius, bloodPopTargetLayer); // 변경된 LayerMask 사용
        foreach (var col in colls)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health != null)
            {
                health.GetDamage(new DamageInfo(damage, DamageType.Fixed, this.gameObject));
            }
        }
    }

    public void ResetHP()
    {
        curHP = _stat.MAXHP;
        isDead = false;
    }
}

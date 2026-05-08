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

        float startHP = curHP;
        float remainingDamage = info.amount;

        if (_status != null && _status.GetDebuffBool(DebuffBoolType.Corroded))
        {
            remainingDamage *= 1.25f;
        }

        float totalAbsorbed = 0f;
        if (info.type != DamageType.Fixed && _status != null && _status.TotalShield > 0)
        {
            totalAbsorbed = _status.ConsumeShield(remainingDamage);
            remainingDamage -= totalAbsorbed;
            
            if (totalAbsorbed > 0)
            {
                OnDamageTaken?.Invoke(0f);
            }
        }

        if (remainingDamage > 0)
        {
            float finalDamage = remainingDamage;
            if (info.type != DamageType.Fixed)
            {
                finalDamage = Mathf.Max(remainingDamage - _stat.DEF, 1f);
            }
            curHP -= finalDamage;
            OnDamageTaken?.Invoke(finalDamage);
        }

        if (_status != null && !isDead)
        {
            int executeThreshold = _status.GetDebuffStack(DebuffStackType.Execute);
            if (executeThreshold > 0 && curHP > 0 && curHP <= executeThreshold)
            {
                curHP = 0;
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
        curHP = Mathf.Min(curHP + amount, _stat.MAXHP);
        OnHeal?.Invoke();
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

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
        string entityName = (rootEntity != null) ? rootEntity.gameObject.name : gameObject.name;
        
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
            var allyManager = pc.GetComponentInChildren<AllyManager>() ?? UnityEngine.Object.FindFirstObjectByType<AllyManager>();
            if (allyManager != null && rootEntity != null) 
            {
                allyManager.ReportDeath(rootEntity.gameObject.GetInstanceID());
            }
        }
    }

    private void ExecuteBloodPop(int damage)
    {
        float explosionRadius = 2.0f;
        LayerMask opponentLayer = (GetComponentInParent<BaseEntity>() != null) ? GetComponentInParent<BaseEntity>().opponentLayer : LayerMask.GetMask("Enemy");

        var registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        if (registry != null && registry.bloodPopVFX != null)
        {
            GameObject vfx = Instantiate(registry.bloodPopVFX, transform.position, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * (explosionRadius * 2f);
            Destroy(vfx, 1.0f);
        }

        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, explosionRadius, opponentLayer);
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

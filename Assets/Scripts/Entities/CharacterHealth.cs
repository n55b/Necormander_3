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

    public event Action<float> OnDamageTaken; // [수정] 데미지 수치 전달 가능하게 변경
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

        // [추가] 부식(Corroded) 상태라면 받는 피해량 25% 증가
        if (_status != null && _status.GetDebuffBool(DebuffBoolType.Corroded))
        {
            remainingDamage *= 1.25f;
        }

        float totalAbsorbed = 0f;

        // 1. 보호막 흡수 로직
        if (info.type != DamageType.Fixed && _status != null && _status.TotalShield > 0)
        {
            totalAbsorbed = _status.ConsumeShield(remainingDamage);
            remainingDamage -= totalAbsorbed;
            
            if (totalAbsorbed > 0)
            {
                Debug.Log($"<color=cyan>[Damage-Shield]</color> {gameObject.name}: 보호막이 {totalAbsorbed:F1} 데미지 흡수.");
                OnDamageTaken?.Invoke(0f); // 0 데미지 피격 이벤트
            }
        }

        // 2. 실제 체력 차감
        if (remainingDamage > 0)
        {
            float finalDamage = remainingDamage;
            if (info.type != DamageType.Fixed)
            {
                finalDamage = Mathf.Max(remainingDamage - _stat.DEF, 1f);
            }
            curHP -= finalDamage;
            
            Debug.Log($"<color=red>[Damage-HP]</color> {gameObject.name}: {finalDamage:F1} 피해 입음. 체력: {startHP:F1} -> {curHP:F1}");
            OnDamageTaken?.Invoke(finalDamage);
        }

        // [추가] 처형(Execute) 체크: 현재 체력이 처형 스택 이하인가?
        if (_status != null && !isDead)
        {
            int executeThreshold = _status.GetDebuffStack(DebuffStackType.Execute);
            if (executeThreshold > 0 && curHP > 0 && curHP <= executeThreshold)
            {
                Debug.Log($"<color=purple>[Execute]</color> {gameObject.name}: 처형 임계점({executeThreshold}) 도달로 즉시 사망.");
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

        // [추가] 비폭(BloodPop) 처리: 사망 시 주변 폭발
        if (_status != null)
        {
            int bloodPopStack = _status.GetDebuffStack(DebuffStackType.BloodPop);
            if (bloodPopStack > 0)
            {
                ExecuteBloodPop(bloodPopStack);
            }
        }

        OnDeath?.Invoke();

        // 본체(Root)를 찾아 사망 보고 및 로그 출력
        BaseEntity rootEntity = GetComponentInParent<BaseEntity>();
        bool isPlayer = (rootEntity != null && rootEntity.CompareTag("Player")) || CompareTag("Player");
        string entityName = (rootEntity != null) ? rootEntity.gameObject.name : gameObject.name;
        
        if (rootEntity != null)
        {
            if (rootEntity.team == Team.Ally && !isPlayer)
            {
                ReportDeathToManager(rootEntity);
            }
        }

        Debug.Log($"<color=red>[Death]</color> {entityName} 사망. (Player 여부: {isPlayer})");
        
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
        LayerMask opponentLayer;
        
        BaseEntity myEntity = GetComponentInParent<BaseEntity>();
        opponentLayer = (myEntity != null) ? myEntity.opponentLayer : LayerMask.GetMask("Enemy");

        Debug.Log($"<color=red>[BloodPop]</color> {gameObject.name} 폭발! 주변에 {damage} 데미지.");

        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, explosionRadius, opponentLayer);
        foreach (var col in colls)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health != null)
            {
                // 주변에 고정 데미지 입힘
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

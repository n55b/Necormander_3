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

    public event Action OnDamageTaken;
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
        float totalAbsorbed = 0f;

        // 1. 보호막 흡수 로직
        if (info.type != DamageType.Fixed && _status != null && _status.TotalShield > 0)
        {
            totalAbsorbed = _status.ConsumeShield(remainingDamage);
            remainingDamage -= totalAbsorbed;
            OnDamageTaken?.Invoke(); 
            
            if (totalAbsorbed > 0)
                Debug.Log($"<color=cyan>[Damage-Shield]</color> {gameObject.name}: 보호막이 {totalAbsorbed:F1} 데미지 흡수. (남은 데미지: {remainingDamage:F1})");
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
            OnDamageTaken?.Invoke();
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
        OnDeath?.Invoke();

        // 본체(Root)를 찾아 사망 보고 및 로그 출력
        BaseEntity rootEntity = GetComponentInParent<BaseEntity>();
        string entityName = (rootEntity != null) ? rootEntity.gameObject.name : gameObject.name;
        
        if (rootEntity != null)
        {
            if (rootEntity.team == Team.Ally && !rootEntity.CompareTag("Player"))
            {
                ReportDeathToManager(rootEntity);
            }
        }

        Debug.Log($"<color=red>[Death]</color> {entityName} 사망 및 오브젝트 파괴.");
        
        // 본체 전체를 파괴 (부모가 없으면 자신만 파괴)
        Destroy(rootEntity != null ? rootEntity.gameObject : gameObject);
    }

    private void ReportDeathToManager(BaseEntity rootEntity)
    {
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null)
        {
            var allyManager = pc.GetComponentInChildren<AllyManager>() ?? UnityEngine.Object.FindFirstObjectByType<AllyManager>();
            
            if (allyManager != null && rootEntity != null) 
            {
                // 본체의 정확한 InstanceID를 전달
                allyManager.ReportDeath(rootEntity.gameObject.GetInstanceID());
            }
        }
    }

    public void ResetHP()
    {
        curHP = _stat.MAXHP;
        isDead = false;
    }
}

using UnityEngine;
using System;

/// <summary>
/// 유닛의 체력 관리와 데미지 계산, 사망 처리를 담당하는 컴포넌트입니다.
/// </summary>
public class CharacterHealth : MonoBehaviour, IDamageable
{
    private CharacterStat _stat;
    private CharacterStatus _status;

    [SerializeField] private float curHP;
    [SerializeField] private bool isDead = false;
    [SerializeField] private bool invincible = false;

    public event Action<float> OnDamageTaken;
    public event Action<DamageInfo> OnDamageReceived; // [추가] 전체 데미지 정보 전달용
    public event Action OnHeal;
    public event Action UpdateHPBar;
    public event Action OnDeath;
    public event Func<CharacterHealth, bool> OnBeforeDeath; // [추가] 부활/페이즈 전환 시 사망 취소용

    public float CurHP => curHP;
    public float MaxHP => (_stat != null) ? _stat.MAXHP : 0f; // [추가] 최대 체력 정보 노출
    public bool IsDead => isDead;
    public bool Invincible { get { return invincible; } set { invincible = value; } }

    public event Action<int, DamageType, string, bool> TakeDamageEvent;
    public event Action<float> TakeHealEvent;

    public void Init(CharacterStat stat, CharacterStatus status)
    {
        _stat = stat;
        _status = status;
        curHP = _stat.MAXHP;
        isDead = false;
    }

    public void TakeDamage(DamageInfo info)
    {
        GetDamage(info);
    }

    public void GetDamage(DamageInfo info)
    {
        OnDamageReceived?.Invoke(info); // [추가] AI 측에서 피격 상세 정보(Throw 여부 등)를 파악하기 위함

        if (isDead || invincible) return;

        // [추가] 회피 / 미스 판정 (평타인 경우에만 적용)
        if (info.isBasicAttack && info.attacker != null)
        {
            var attackerStat = info.attacker.GetComponent<CharacterStat>();
            if (attackerStat != null && _stat != null)
            {
                float totalMissChance = attackerStat.MISS_CHANCE + _stat.EVASION;
                if (UnityEngine.Random.value <= totalMissChance)
                {
                    // 회피 이벤트 트리거
                    DamageEventBus.TriggerEvasionOccurred(this, attackerStat);

                    // 회피 성공
                    TakeDamageEvent?.Invoke(0, info.type, "MISS", false);
                    return;
                }
                // else (명중 성공) 시 아래의 데미지 파이프라인으로 계속 진행됨
            }
        }

        // [추가] 슈퍼아머 게이지 차감 처리
        if (_status != null && _status.HasSuperArmor && info.superArmorDamage > 0f)
        {
            _status.DamageSuperArmor(info.superArmorDamage);
        }

        // [추가] 경직(Hitstun) 및 넉백(Knockback) 처리
        bool hasSuperArmor = (_status != null && _status.HasSuperArmor);
        if ((info.causesHitstun || info.knockbackForce > 0f) && !hasSuperArmor)
        {
            var rootEntity = GetComponentInParent<BaseEntity>();
            if (rootEntity != null)
            {
                if (info.causesHitstun)
                {
                    rootEntity.CancelAttack();
                    // Status(디버프)에 Stunned 등을 0.2초 정도 추가해 경직을 줄 수도 있습니다.
                    if (_status != null)
                    {
                        _status.SetDebuffBool(DebuffBoolType.Stunned, 0.2f); // 0.2초 경직
                    }
                }

                if (info.knockbackForce > 0f && info.attacker != null)
                {
                    Vector2 dir = (transform.position - info.attacker.transform.position).normalized;
                    rootEntity.ApplyKnockback(dir * info.knockbackForce);
                }
            }
        }

        // 데미지 파이프라인: 계산 전 증폭/변형 이벤트
        DamageEventBus.TriggerBeforeDamageCalculated(this, ref info);

        // [추가] 상처(Wounded) 부여 시 받피증 (Damage Amp) 적용
        if (_status != null && _status.GetDebuffBool(DebuffBoolType.Wounded))
        {
            int woundTier = _status.GetDebuffTier(DebuffBoolType.Wounded);
            float damageAmp = woundTier == 1 ? 0.15f : woundTier == 2 ? 0.30f : 0.45f; // 15%, 30%, 45%
            info.amount *= (1f + damageAmp);
        }

        float remainingDamage = info.amount;

        // [기초 개선안] 방패병 데미지 대신 받기 (15%)
        // 아군 타겟(플레이어 포함), 리다이렉트된 데미지가 아닐 때
        if (_stat != null && !_stat.IsEnemy && !info.isRedirected && remainingDamage > 0)
        {
            // 본인이 방패병이 아닐 경우에만 주변 2반경 내의 방패병 탐색
            if (_stat.jobType != CommandData.SkeletonShieldbearer)
            {
                Collider2D[] allies = Physics2D.OverlapCircleAll(transform.position, 2f, LayerMask.GetMask("Army"));
                CharacterHealth bestShieldbearer = null;
                float minDist = float.MaxValue;

                foreach (var allyCol in allies)
                {
                    var allyStat = allyCol.GetComponent<CharacterStat>();
                    var allyHealth = allyCol.GetComponent<CharacterHealth>();
                    if (allyStat != null && allyHealth != null && !allyHealth.isDead && allyStat.jobType == CommandData.SkeletonShieldbearer)
                    {
                        float d = Vector2.Distance(transform.position, allyCol.transform.position);
                        if (d < minDist) { minDist = d; bestShieldbearer = allyHealth; }
                    }
                }

                if (bestShieldbearer != null)
                {
                    float redirectAmount = remainingDamage * 0.15f;
                    remainingDamage -= redirectAmount;

                    DamageInfo redirectInfo = info;
                    redirectInfo.amount = redirectAmount;
                    redirectInfo.isRedirected = true;
                    redirectInfo.popupText = "Guard";
                    bestShieldbearer.GetDamage(redirectInfo);
                }
            }
        }

        // [공용 시너지 연산 (미니언 물리 피해(평타 전용) && 적군 타겟 && 공격자가 아군)]
        bool isEnemyTarget = (_stat != null && _stat.IsEnemy);
        if (info.type == DamageType.Physical && isEnemyTarget && info.attacker != null && !info.isThrowDamage)
        {
            var attackerStat = info.attacker.GetComponentInParent<CharacterStat>();
            if (attackerStat == null) attackerStat = info.attacker.GetComponentInChildren<CharacterStat>();
            
            if (attackerStat != null && !attackerStat.IsEnemy)
            {
                var inven = InventoryManager.Instance;
                if (inven != null)
                {
                    int poisonSynergy = inven.GetSynergyCount(GemSynergyGroup.Poison);
                    int bloodPopSynergy = inven.GetSynergyCount(GemSynergyGroup.BloodPop);
                    int executionSynergy = inven.GetSynergyCount(GemSynergyGroup.Execution);

                    // Obsolete Poison synergy removed
                        
                    // Obsolete BloodPop synergy removed
                        
                    if (executionSynergy >= 2)
                        _status.AddDebuffStack(DebuffStackType.Wound, 1f);
                }
            }
        }

        // [쉴드] 적용
        if (info.type != DamageType.Fixed && _status != null && _status.TotalShield > 0)
        {
            float absorbed = _status.ConsumeShield(remainingDamage);
            remainingDamage -= absorbed;
            if (absorbed > 0) OnDamageTaken?.Invoke(0f);
            // 쉴드 데미지 별개로 표시
            TakeDamageEvent?.Invoke((int)absorbed, info.type, "Shield", false);
        }

        // [최종 데미지 및 체력 차감]
        if (remainingDamage > 0)
        {
            float finalDamage = remainingDamage;
            if (info.type != DamageType.Fixed)
            {
                // [신규 방어력 로직 적용]
                // 최종 데미지 = (총 데미지 * (100 - 일반 방어력) / 100) - 고정 수치 방어력
                float dmgAfterPercentDef = remainingDamage * ((100f - _stat.DEF) / 100f);
                finalDamage = Mathf.Max(dmgAfterPercentDef - _stat.FLAT_DEF, 1f);
            }

            curHP -= finalDamage;
            OnDamageTaken?.Invoke(finalDamage);

            string popupStr = "Normal";
            if (!string.IsNullOrEmpty(info.popupText))
                popupStr = info.popupText;

            TakeDamageEvent?.Invoke((int)finalDamage, info.type, popupStr, false);

            // 실제 데미지 피격 후 이벤트 트리거
            DamageEventBus.TriggerDamageReceived(this, info);
        }

        // [처형] 체크
        if (_status != null && !isDead)
        {
            float executeThreshold = _status.GetDebuffStack(DebuffStackType.Wound);

            // 처형 계산 전 이벤트 트리거 (단두대 등에서 Threshold 증폭)
            if (executeThreshold > 0)
            {
                DamageEventBus.TriggerBeforeExecutionCalculated(this, ref executeThreshold);
            }

            if (executeThreshold > 0 && curHP > 0 && curHP <= executeThreshold)
            {
                TakeDamageEvent?.Invoke((int)executeThreshold, DamageType.Fixed, "Execution", false);

                // 처형 트리거 히트스탑 (강타와 동일한 구조, 다만 스킬 에셋과 별건로 직접 호출)
                if (HitStopManager.Instance != null)
                    HitStopManager.Instance.DoHitStop(0.1f);

                curHP = 0;

                // 처형 완료 이벤트 트리거 (공포 등 발동)
                DamageEventBus.TriggerEntityExecuted(this);
            }
        }

        // [사망] 체크
        if (curHP <= 0.0f && !isDead) // isDead 체크로 중복 호출 방지
        {
            if (OnBeforeDeath != null && OnBeforeDeath.Invoke(this))
            {
                return; // 페이즈 전환 등으로 죽음을 회피함
            }
            // 사망 직전 이벤트
            DamageEventBus.TriggerEntityDied(this, info);
            Die();
        }

        UpdateHPBar?.Invoke(); // HPBar 업데이트
    }

    public void Heal(float amount)
    {
        if (isDead) return;

        float healAmount = amount;

        // 힐 파이프라인: 힐량 증폭/계산 전 이벤트
        DamageEventBus.TriggerBeforeHealCalculated(this, ref healAmount);

        float oldHP = curHP;
        float excessHeal = (curHP + healAmount) - _stat.MAXHP;
        
        curHP = Mathf.Min(curHP + healAmount, _stat.MAXHP);

        // [시너지] 수호신(Shield_Guardian) (6) 스택: 체력 회복 초과양 15%가 보호막으로 전환 (최대 체력의 15% 제한)
        if (excessHeal > 0 && _stat != null)
        {
            var inven = InventoryManager.Instance;
            if (inven != null)
            {
                int guardianLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Shield_Guardian));
                if (guardianLevel >= 3 && _status != null) // (6) 스택
                {
                    float shieldToAdd = excessHeal * 0.15f;
                    float maxShieldLimit = _stat.MAXHP * 0.15f;
                    
                    // 기존 보호막이 최대 한도를 넘지 않도록 제한적으로 추가
                    if (_status.TotalShield < maxShieldLimit)
                    {
                        float allowedToAdd = Mathf.Min(shieldToAdd, maxShieldLimit - _status.TotalShield);
                        if (allowedToAdd > 0)
                        {
                            _status.AddShield(allowedToAdd, 10.0f); // 임시 10초
                        }
                    }
                }
            }
        }

        Debug.Log($"{gameObject.name} healed for {healAmount}. HP: {oldHP} -> {curHP}");
        TakeHealEvent?.Invoke(healAmount); // [추가] 힐 텍스트 띄우기
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
                TriggerBloodPopExplosion(bloodPopStack);
            }
        }

        OnDeath?.Invoke();

        BaseEntity rootEntity = GetComponentInParent<BaseEntity>();
        bool isPlayer = gameObject.layer == LayerMask.NameToLayer("Player"); // (추가) 여기 뭔가 긴데 Player 인식 못 하길래 현재 오브젝트 Layer로 바꿈
        
        if (rootEntity != null && rootEntity.team == Team.Ally && !isPlayer)
        {
            ReportDeathToManager(rootEntity);
        }
        
        if (!isPlayer)
        {
            Destroy(rootEntity != null ? rootEntity.gameObject : gameObject);
        }

        if(isPlayer)
        {
            GameManager.Instance.Gameover(); // 플레이어 사망 처리 (리스폰 등)
        }
    }

    public void TriggerBloodPopExplosion(float stacks)
    {
        float explosionRadius = 2.5f;
        float baseDamage = stacks * 10f; // 비폭 1스택당 10의 데미지로 가정
        float finalDamage = baseDamage;

        // [이벤트 버스] 폭발 전 이벤트: 폭발 반경 증폭, 당기기, 기본 데미지 증폭 등 적용 가능
        StatusEventBus.TriggerBloodPopExplodeBeforeDamage(this, ref finalDamage, ref explosionRadius);

        // Bloodpop은 무조건 'Enemy' 레이어의 유닛에게만 데미지를 줍니다. (아군과 플레이어 제외)
        var registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        if (registry != null && registry.bloodPopVFX != null)
        {
            GameObject vfx = Instantiate(registry.bloodPopVFX, transform.position, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * (explosionRadius * 2f);
            Destroy(vfx, 1.0f);
        }

        LayerMask enemyLayer = LayerMask.GetMask("Enemy");
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, explosionRadius, enemyLayer); 

        foreach (var col in colls)
        {
            var targetHealth = col.GetComponentInChildren<CharacterHealth>();
            if (targetHealth != null && !targetHealth.isDead)
            {
                float damageToApply = finalDamage;
                var targetStatus = col.GetComponentInChildren<CharacterStatus>();

                // 타겟 종속 데미지 증폭 등은 향후 핸들러 내에서 colls를 루프돌며 개별 처리하거나 OnBloodPopTargetImpact 같은 세분화된 이벤트로 분리 가능.
                // 현재는 폭발 자체 이벤트에서 통합 처리하도록 권장합니다.

                // 데미지 팝업 등을 위해 이벤트 호출 (GetDamage 내부에서 팝업을 띄우므로 여기서는 직접 호출 생략)
                targetHealth.GetDamage(new DamageInfo(damageToApply, DamageType.Fixed, this.gameObject, false, 1f, false, "BloodPop"));
            }
        }

        // [이벤트 버스] 폭발 후 이벤트: 아군 쉴드 부여, 장판 생성 등 사후 처리
        StatusEventBus.TriggerBloodPopExplodeAfterDamage(this, finalDamage, explosionRadius, colls);
    }

    public void TriggerDamagePopup(int amount, DamageType dmgType, string type, bool isCritical)
    {
        TakeDamageEvent?.Invoke(amount, dmgType, type, isCritical);
    }

    public void ApplyFearToSurroundingEnemies()
    {
        LayerMask enemyLayer = LayerMask.GetMask("Enemy");
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, 5f, enemyLayer);
        foreach (var col in colls)
        {
            var targetStatus = col.GetComponentInChildren<CharacterStatus>();
            if (targetStatus != null && targetStatus != _status && !targetStatus.IsElite)
            {
                // 주변 일반 적에게만 공포 1초 부여
                // targetStatus.SetDebuffBool(DebuffBoolType.Wounded, 1.0f);
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
}

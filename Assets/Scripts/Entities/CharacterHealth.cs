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
    public event Action<DamageInfo> OnDamageReceived; // [추가] 전체 데미지 정보 전달용
    public event Action OnHeal;
    public event Action UpdateHPBar;
    public event Action OnDeath;
    public event Func<CharacterHealth, bool> OnBeforeDeath; // [추가] 부활/페이즈 전환 시 사망 취소용

    public float CurHP => curHP;
    public float MaxHP => (_stat != null) ? _stat.MAXHP : 0f; // [추가] 최대 체력 정보 노출
    public bool IsDead => isDead;
    public bool Invincible { get { return invincible; } set { invincible = value; } }

    public event Action<int, string, bool> TakeDamageEvent;
    public event Action<float> TakeHealEvent;

    public void Init(CharacterStat stat, CharacterStatus status)
    {
        _stat = stat;
        _status = status;
        curHP = _stat.MAXHP;
        isDead = false;
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
                    // [유니크] 발이부중 (UnseenMiss): 빗나갈 경우 상태 기록 (반구저기 필수)
                    if (attackerStat != null && !attackerStat.IsEnemy && attackerStat.jobType == CommandData.SkeletonArcher)
                    {
                        var attackerStatus = info.attacker.GetComponent<CharacterStatus>();
                        var inven = InventoryManager.Instance;
                        if (attackerStatus != null && inven != null && inven.HasUniqueEffect(GemUniqueType.UnseenMiss) && inven.HasUniqueEffect(GemUniqueType.ReflectingNature))
                        {
                            attackerStatus.LastAttackMissed = true;
                        }
                    }

                    // 회피 성공
                    TakeDamageEvent?.Invoke(0, "MISS", false);
                    return;
                }
                else
                {
                    // 명중 성공
                    
                    // [방패병 유니크] 가시 갑옷 (ThornArmor)
                    // 기본 공격 피격 시 적 현재 체력 2% 고정 피해 반사
                    if (_stat != null && !_stat.IsEnemy && _stat.jobType == CommandData.SkeletonShieldbearer)
                    {
                        var inven = InventoryManager.Instance;
                        if (inven != null && inven.HasUniqueEffect(GemUniqueType.ThornArmor))
                        {
                            var attackerHealth = info.attacker.GetComponent<CharacterHealth>();
                            if (attackerHealth != null && !attackerHealth.IsDead)
                            {
                                float thornDamage = attackerHealth.CurHP * 0.02f;
                                DamageInfo thornInfo = new DamageInfo(thornDamage, DamageType.Fixed, this.gameObject);
                                attackerHealth.GetDamage(thornInfo);
                                attackerHealth.TakeDamageEvent?.Invoke((int)thornDamage, "Fixed", false);
                            }
                        }
                    }

                    if (attackerStat != null && !attackerStat.IsEnemy && attackerStat.jobType == CommandData.SkeletonArcher)
                    {
                        var attackerStatus = info.attacker.GetComponent<CharacterStatus>();
                        var inven = InventoryManager.Instance;
                        if (attackerStatus != null && attackerStatus.LastAttackMissed && inven != null && inven.HasUniqueEffect(GemUniqueType.UnseenMiss) && inven.HasUniqueEffect(GemUniqueType.ReflectingNature))
                        {
                            info.amount *= 2.0f; // 다음 공격 피해 100% 증가
                            attackerStatus.LastAttackMissed = false; // 소모
                        }
                    }
                }
            }
        }

        // [유니크] 녹슬어 버린 갑옷 (RustedArmor): 부식된 적이 아군 미니언 평타 5대 피격 시 현재 체력 5% 고정 피해
        if (info.isBasicAttack && info.attacker != null && _stat != null && _stat.IsEnemy)
        {
            var attackerStat = info.attacker.GetComponent<CharacterStat>();
            // 플레이어가 아닌 아군 미니언 평타인 경우 (혹은 플레이어도 포함시킬지 여부 - "미니언의 기본 공격" 조건)
            if (attackerStat != null && !attackerStat.IsEnemy)
            {
                var inven = InventoryManager.Instance;
                if (inven != null && inven.HasUniqueEffect(GemUniqueType.RustedArmor) && _status != null && _status.GetDebuffBool(DebuffBoolType.Corroded))
                {
                    _status.CorrosionHitCount++;
                    if (_status.CorrosionHitCount >= 5)
                    {
                        _status.CorrosionHitCount = 0;
                        float percentDamage = curHP * 0.05f; // 현재 체력 5%
                        
                        // 즉시 고정 데미지 적용
                        GetDamage(new DamageInfo(percentDamage, DamageType.Fixed, info.attacker));
                        TakeDamageEvent?.Invoke((int)percentDamage, "Fixed", false);
                    }
                }
            }
        }

        float remainingDamage = info.amount;
        string str = "";                             // 데미지 색상타입

        // [부식/노쇠] 시너지에 따른 대미지 증폭
        bool isEnemyTarget = (_stat != null && _stat.IsEnemy);
        float corrosionAmp = GemRuleSystem.GetCorrosionDamageAmp(isEnemyTarget);
        if (_status.GetDebuffBool(DebuffBoolType.Corroded))
        {
            // 부식 상태일 경우 시너지 보너스(25%, 40% 등)만큼 대미지 증폭
            remainingDamage *= (1.0f + corrosionAmp); 
            str = "Corroded";
        }
        
        // [노쇠] 대미지 증폭
        if (_status.GetDebuffBool(DebuffBoolType.Senility))
        {
            float senilityAmp = GemRuleSystem.GetSenilityDamageAmp(isEnemyTarget);
            remainingDamage *= (1.0f + senilityAmp);
        }

        // [유니크] 동상 파괴자 (Frostbreaker) - 한기 걸린 적에게 5% 추가 피해
        if (isEnemyTarget && _status.GetDebuffStack(DebuffStackType.Chill) > 0)
        {
            if (InventoryManager.Instance != null)
            {
                int count = InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.Frostbreaker);
                if (count > 0)
                {
                    remainingDamage *= (1.0f + 0.05f * count);
                }
            }
        }

        // [유니크] 고드름 부시기 (ShatterIcicle) - 동결된 적을 투척 공격으로 맞출 시 동결 해제 및 50% 추가 피해
        if (isEnemyTarget && _status.GetDebuffBool(DebuffBoolType.Frozen) && info.isThrowDamage)
        {
            if (InventoryManager.Instance != null)
            {
                int count = InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.ShatterIcicle);
                if (count > 0)
                {
                    remainingDamage *= (1.0f + 0.50f * count);
                    // 강제 해제
                    _status.ForceUnfreeze();
                }
            }
        }

        // [공용 시너지 연산 (미니언 물리 피해(평타 전용) && 적군 타겟 && 공격자가 아군)]
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

                    if (poisonSynergy >= 2)
                        _status.AddDebuffStack(DebuffStackType.Poison, 1f);
                        
                    if (bloodPopSynergy >= 2)
                        _status.AddDebuffStack(DebuffStackType.BloodPop, 1f);
                        
                    if (executionSynergy >= 2)
                        _status.AddDebuffStack(DebuffStackType.Execute, 1f);
                        
                    // [유니크] 상처 감염 (WoundInfection): 평타 명중 시 중독 틱 타이머 단축
                    if (inven.HasUniqueEffect(GemUniqueType.WoundInfection))
                    {
                        _status.AdvancePoisonTimer(0.1f);
                    }
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

            string popupStr = "Normal";
            if (!string.IsNullOrEmpty(info.popupText))
                popupStr = info.popupText;
            else if (info.type == DamageType.Fixed)
                popupStr = "Poison"; // 하위 호환성을 위해 Fixed 데미지이고 지정된 텍스트가 없으면 Poison으로 처리

            TakeDamageEvent?.Invoke((int)finalDamage, popupStr, false);

            // [유니크] 광적인 분노 (FanaticRage): 기본 공격 피해의 3%만큼 흡혈
            if (info.isBasicAttack && info.attacker != null)
            {
                var attackerStat = info.attacker.GetComponentInChildren<CharacterStat>();
                if (attackerStat == null) attackerStat = info.attacker.GetComponentInParent<CharacterStat>();
                
                if (attackerStat != null && !attackerStat.IsEnemy && attackerStat.jobType == CommandData.SkeletonWarrior)
                {
                    var inven = InventoryManager.Instance;
                    if (inven != null && inven.HasUniqueEffect(GemUniqueType.FanaticRage))
                    {
                        var attackerHealth = info.attacker.GetComponentInChildren<CharacterHealth>();
                        if (attackerHealth == null) attackerHealth = info.attacker.GetComponentInParent<CharacterHealth>();
                        
                        if (attackerHealth != null)
                        {
                            attackerHealth.Heal(finalDamage * 0.03f);
                        }
                    }
                }
            }
        }

        // [처형] 체크
        if (_status != null && !isDead)
        {
            float executeThreshold = _status.GetDebuffStack(DebuffStackType.Execute);

            // [유니크] 단두대 (Guillotine): 처형 스택 기준치 10% 완화
            if (InventoryManager.Instance != null)
            {
                int count = InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.Guillotine);
                if (count > 0)
                {
                    executeThreshold *= (1.0f + 0.1f * count);
                }
            }

            if (executeThreshold > 0 && curHP > 0 && curHP <= executeThreshold)
            {
                TakeDamageEvent?.Invoke((int)executeThreshold, "Execution", false);
                curHP = 0;

                // [유니크] 공포 (Fear): 처형 당한 적 주변 일반 적에게 1초간 공포 상태 부여
                if (InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.Fear))
                {
                    ApplyFearToSurroundingEnemies();
                }
            }
        }

        // [사망] 체크
        if (curHP <= 0.0f && !isDead) // isDead 체크로 중복 호출 방지
        {
            if (OnBeforeDeath != null && OnBeforeDeath.Invoke(this))
            {
                return; // 페이즈 전환 등으로 죽음을 회피함
            }
            Die();
        }

        UpdateHPBar?.Invoke(); // HPBar 업데이트
    }

    public void Heal(float amount)
    {
        if (isDead) return;

        float healAmount = amount;

        // [유니크] 사제는 공격을 할 수 없어! (PriestsCantAttack): 부식 시너지 활성화 시 아군 치유량 20% 증가
        if (_stat != null && !_stat.IsEnemy)
        {
            var inven = InventoryManager.Instance;
            if (inven != null)
            {
                int pcCount = inven.GetUniqueEffectCount(GemUniqueType.PriestsCantAttack);
                if (pcCount > 0)
                {
                    healAmount *= (1.0f + 0.20f * pcCount);
                }

                int corrosionLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Priest_Corrosion));
                if (corrosionLevel > 0)
                {
                    healAmount *= 1.2f;
                }
            }
        }

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
                ExecuteBloodPop(bloodPopStack);
            }
            
            // [유니크] 중독 전염 (PoisonContagion): 사망 시 주변 적 1명에게 남은 독 스택의 50% 전이
            var inven = InventoryManager.Instance;
            if (inven != null && inven.HasUniqueEffect(GemUniqueType.PoisonContagion) && _stat != null && _stat.IsEnemy)
            {
                int poisonStack = _status.GetDebuffStack(DebuffStackType.Poison);
                if (poisonStack > 0)
                {
                    LayerMask enemyLayer = LayerMask.GetMask("Enemy");
                    Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, 5f, enemyLayer);
                    foreach (var col in colls)
                    {
                        var tHealth = col.GetComponentInChildren<CharacterHealth>();
                        if (tHealth != null && !tHealth.isDead && tHealth.gameObject != this.gameObject)
                        {
                            var tStatus = col.GetComponentInChildren<CharacterStatus>();
                            if (tStatus != null)
                            {
                                tStatus.AddDebuffStack(DebuffStackType.Poison, poisonStack * 0.5f);
                                break; // 주변 1명에게만 부여 후 종료
                            }
                        }
                    }
                }
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

        var inven = InventoryManager.Instance;
        bool hasInven = inven != null;

        // [유니크] 급조 폭팔물 (ImprovisedExplosive): 데미지 10% 증가
        if (hasInven)
        {
            int ieCount = inven.GetUniqueEffectCount(GemUniqueType.ImprovisedExplosive);
            if (ieCount > 0)
            {
                finalDamage *= (1.0f + 0.10f * ieCount);
            }
        }

        // [유니크] 동귀어진 (MutualDestruction): 폭발 직전 반경 1.5배 내의 적들을 폭발 중심점으로 끌어당김
        LayerMask enemyLayer = LayerMask.GetMask("Enemy");
        if (hasInven && inven.HasUniqueEffect(GemUniqueType.MutualDestruction))
        {
            Collider2D[] pullColls = Physics2D.OverlapCircleAll(transform.position, explosionRadius * 1.5f, enemyLayer);
            foreach (var col in pullColls)
            {
                if (col.gameObject != this.gameObject)
                {
                    Vector3 dirToCenter = (transform.position - col.transform.position).normalized;
                    float dist = Vector2.Distance(transform.position, col.transform.position);
                    if (dist > 0.1f)
                    {
                        col.transform.position += dirToCenter * (dist * 0.5f); // 중심부를 향해 절반만큼 물리적 이동
                    }
                }
            }
        }

        // Bloodpop은 무조건 'Enemy' 레이어의 유닛에게만 데미지를 줍니다. (아군과 플레이어 제외)
        var registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        if (registry != null && registry.bloodPopVFX != null)
        {
            GameObject vfx = Instantiate(registry.bloodPopVFX, transform.position, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * (explosionRadius * 2f);
            Destroy(vfx, 1.0f);
        }

        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, explosionRadius, enemyLayer); 

        foreach (var col in colls)
        {
            var targetHealth = col.GetComponentInChildren<CharacterHealth>();
            if (targetHealth != null && !targetHealth.isDead)
            {
                float damageToApply = finalDamage;
                var targetStatus = col.GetComponentInChildren<CharacterStatus>();

                // [유니크] 나도 폭발하는걸까? (AmIExplodingToo) 약점 상태 시 데미지 장착 개수 비례 증폭
                if (hasInven && targetStatus != null && targetStatus.GetDebuffBool(DebuffBoolType.BloodPopVulnerable))
                {
                    int amICount = inven.GetUniqueEffectCount(GemUniqueType.AmIExplodingToo);
                    if (amICount > 0)
                    {
                        damageToApply *= (1.0f + 0.20f * amICount);
                    }
                    else
                    {
                        damageToApply *= 1.20f; // 보석을 뺀 상태라도 디버프가 남아있다면 기본 20%는 적용되게 유지
                    }
                }

                // 데미지 팝업 등을 위해 이벤트 호출 (GetDamage 내부에서 팝업을 띄우므로 여기서는 직접 호출 생략)
                targetHealth.GetDamage(new DamageInfo(damageToApply, DamageType.Fixed, this.gameObject, false, 1f, false, "BloodPop"));

                // [유니크] 나도 폭발하는걸까? (AmIExplodingToo) 타격 후 약점 상태 영구 부여
                if (hasInven && inven.HasUniqueEffect(GemUniqueType.AmIExplodingToo) && targetStatus != null)
                {
                    targetStatus.SetDebuffBool(DebuffBoolType.BloodPopVulnerable, 9999f);
                }

                // [유니크] 살덩이가 폭발하는 것: 비폭 피해 대상에게 데미지의 일부만큼 비폭 스택 부여
                float chainRatio = GemRuleSystem.GetBloodPopChainRatio(isEnemyTarget);
                if (chainRatio > 0)
                {
                    if (targetStatus != null)
                    {
                        targetStatus.AddDebuffStack(DebuffStackType.BloodPop, damageToApply * chainRatio);
                    }
                }
            }
        }

        // [유니크] 내장 파티 (GoreParty) & 피철갑 (BloodArmor): 아군 회복 및 쉴드
        if (hasInven && (inven.HasUniqueEffect(GemUniqueType.GoreParty) || inven.HasUniqueEffect(GemUniqueType.BloodArmor)))
        {
            LayerMask allyLayer = LayerMask.GetMask("Army", "Player");
            Collider2D[] allyColls = Physics2D.OverlapCircleAll(transform.position, explosionRadius, allyLayer);
            foreach (var col in allyColls)
            {
                var allyHealth = col.GetComponentInChildren<CharacterHealth>();
                var allyStatus = col.GetComponentInChildren<CharacterStatus>();

                if (allyHealth != null && !allyHealth.isDead)
                {
                    int goreCount = inven.GetUniqueEffectCount(GemUniqueType.GoreParty);
                    if (goreCount > 0)
                    {
                        allyHealth.Heal(stacks * goreCount);
                    }
                }
                
                if (allyStatus != null)
                {
                    int bloodArmorCount = inven.GetUniqueEffectCount(GemUniqueType.BloodArmor);
                    if (bloodArmorCount > 0)
                    {
                        allyStatus.AddShield(stacks * 2.0f * bloodArmorCount, 5.0f); // 쉴드 5초 지속으로 가정
                        
                        if (registry != null && registry.shieldAttachVFX != null)
                        {
                            var allyStat = col.GetComponentInChildren<CharacterStat>();
                            if (allyStat != null && allyStat.Visual != null)
                            {
                                GameObject vfx = Instantiate(registry.shieldAttachVFX, col.transform.position, Quaternion.identity, col.transform);
                                allyStat.Visual.SetShieldVFX(vfx);
                            }
                        }
                    }
                }
            }
        }

        // [유니크] 녹아내리는 시체 (MeltingCorpse): 5초간 장판 생성
        if (hasInven && inven.HasUniqueEffect(GemUniqueType.MeltingCorpse))
        {
            if (registry != null && registry.meltingCorpsePuddlePrefab != null)
            {
                // 프리팹이 연결되어 있으면 프리팹 인스턴스화
                GameObject puddleObj = Instantiate(registry.meltingCorpsePuddlePrefab, transform.position, Quaternion.identity);
                var puddle = puddleObj.GetComponent<MeltingCorpsePuddle>();
                if (puddle == null) puddle = puddleObj.AddComponent<MeltingCorpsePuddle>();
                puddle.Init(finalDamage * 0.1f, explosionRadius);
            }
            else
            {
                // 연결 안 된 경우 임시 장판 생성
                GameObject puddleObj = new GameObject("MeltingCorpsePuddle_Temp");
                puddleObj.transform.position = transform.position;
                var puddle = puddleObj.AddComponent<MeltingCorpsePuddle>();
                puddle.Init(finalDamage * 0.1f, explosionRadius);
            }
        }
    }

    private void ApplyFearToSurroundingEnemies()
    {
        LayerMask enemyLayer = LayerMask.GetMask("Enemy");
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, 5f, enemyLayer);
        foreach (var col in colls)
        {
            var targetStatus = col.GetComponentInChildren<CharacterStatus>();
            if (targetStatus != null && targetStatus != _status && !targetStatus.IsElite)
            {
                // 주변 일반 적에게만 공포 1초 부여
                targetStatus.SetDebuffBool(DebuffBoolType.Feared, 1.0f);
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

using UnityEngine;
using System;
using AstroNuts.Monsters;


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

    /// <summary>
    /// 갈래가 안 붙은(None) 피해를 공격자를 보고 자동 분류한다.
    ///  · 적 공격 → 티어(보스/엘리트/미니언)   · 플레이어 공격 → 스킬
    /// 평타/대쉬/패링/디버프/함정은 소스에서 명시 태그되므로 여기로 안 온다.
    /// </summary>
    private static DamageCategory ResolveCategoryFromAttacker(GameObject attacker)
    {
        if (attacker == null) return DamageCategory.None;

        if (attacker.CompareTag("Boss") || attacker.name.Contains("Boss")) return DamageCategory.EnemyBoss;
        var status = attacker.GetComponentInParent<CharacterStatus>();
        if (status != null && status.IsElite) return DamageCategory.EnemyElite;
        if (attacker.layer == Layers.Enemy) return DamageCategory.EnemyMinion;

        if (attacker.layer == Layers.Player || attacker.layer == Layers.PlayerDash) return DamageCategory.Skill;

        return DamageCategory.None;
    }

    public void GetDamage(DamageInfo info)
    {
        // 갈래가 안 붙은 피해는 공격자를 보고 자동 분류(적 티어 / 플레이어=스킬).
        if (info.category == DamageCategory.None)
            info.category = ResolveCategoryFromAttacker(info.attacker);

        OnDamageReceived?.Invoke(info); // [추가] AI 측에서 피격 상세 정보를 파악하기 위함

        if (isDead || invincible) return;

        // [추가] 회피 / 명중 판정 (평타인 경우에만 적용)
        // 기본값(적중률 1, 회피 0)이면 명중률 1.0 이라 Random.value(<1.0)가 절대 못 넘는다 = 무조건 명중.
        // 예전엔 '빗나갈 확률 <= Random.value' 라 회피 0% 여도 0.0 이 뜨면 빗나갔다. 뒤집으면서 같이 해결됨.
        if ((info.category == DamageCategory.BasicAttack || DamageRules.IsEnemyTier(info.category)) && info.attacker != null)
        {
            var attackerStat = info.attacker.GetComponent<CharacterStat>();
            if (attackerStat != null && _stat != null)
            {
                float hitChance = attackerStat.ACCURACY - _stat.EVASION;
                if (UnityEngine.Random.value >= hitChance)
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

        // 슈퍼아머 여부는 '깎기 전'에 읽는다. 이 순서가 중요하다 —
        // 예전엔 깎은 뒤에 읽어서, 슈퍼아머를 부순 바로 그 일격이 넉백/경직까지 같이 넣었다.
        // (게이지가 0이 되는 순간 _hasSuperArmor 가 false 로 바뀌므로)
        // 슈퍼아머는 '부서지기 전까지' 막아주는 것이니, 부순 타격 자체는 아직 막혀야 한다.
        bool hasSuperArmor = (_status != null && _status.HasSuperArmor);

        // [추가] 슈퍼아머 게이지 차감 처리
        if (hasSuperArmor && info.superArmorDamage > 0f)
        {
            _status.DamageSuperArmor(info.superArmorDamage);
        }

        // [추가] 경직(Hitstun) 및 넉백(Knockback) 처리
        if ((info.causesHitstun || info.knockbackForce > 0f) && !hasSuperArmor)
        {
            var rootEntity = GetComponentInParent<BaseEntity>();
            if (rootEntity != null)
            {
                if (info.causesHitstun)
                {
                    rootEntity.CancelAttack();
                    // Status(디버프)에 Hitstunned 등을 0.2초 정도 추가해 경직을 줄 수도 있습니다.
                    if (_status != null)
                    {
                        _status.ApplyStatus(StatusType.Hitstun, 0.2f); // 0.2초 경직
                    }
                }

                if (info.knockbackForce > 0f && info.attacker != null)
                {
                    Vector2 dir = (transform.position - info.attacker.transform.position).normalized;
                    rootEntity.ApplyKnockback(dir * info.knockbackForce);
                }
            }
        }

        // [추가] 공격자(info.attacker)가 사자탈(DualSplitAIPatternSO)을 쓰는 몹이고 타격이 정상 명중한 경우 슬로우 디버프 인젝션
        if (info.attacker != null && _status != null)
        {
            var attackerEntity = info.attacker.GetComponent<BaseEntity>();
            if (attackerEntity == null)
            {
                attackerEntity = info.attacker.GetComponentInParent<BaseEntity>();
            }

            if (attackerEntity != null && attackerEntity.Brain is DualSplitAIPatternSO lionMaskPattern)
            {
                _status.ApplySlow("LionMaskSlow", lionMaskPattern.slowReduction, lionMaskPattern.slowDuration);
                Debug.Log($"<color=cyan>[LionMaskSlow]</color> Hit by {info.attacker.name}. Applied slow ({lionMaskPattern.slowReduction * 100}% for {lionMaskPattern.slowDuration}s)");
            }
        }

        // 데미지 파이프라인: 계산 전 증폭/변형 이벤트
        DamageEventBus.TriggerBeforeDamageCalculated(this, ref info);

        float remainingDamage = info.amount;

        // [치명타] 방어력보다 먼저 굴린다 — 기획: "치명타 판정 끝난 최종 데미지에서 방어력 감소율을 뺀다".
        // 상태이상 고정 피해(출혈/중독/빙결/비폭)엔 안 붙는다. '고정'이니까.
        bool isCritical = false;
        if (DamageRules.CanCrit(info) && info.attacker != null)
        {
            var attackerStat = info.attacker.GetComponent<CharacterStat>();
            if (attackerStat == null) attackerStat = info.attacker.GetComponentInParent<CharacterStat>();
            if (attackerStat != null && attackerStat.CRIT_CHANCE > 0f
                && UnityEngine.Random.value * 100f < attackerStat.CRIT_CHANCE)
            {
                isCritical = true;
                remainingDamage *= attackerStat.CRIT_DAMAGE / 100f;
            }
        }

        // [쉴드] 적용.
        // Fixed(고정 피해)도 쉴드는 막는다. 쉴드는 임시 체력에 가까운 물건이라 '방어력 무시'와
        // 같은 취급을 하면 안 된다. Fixed 가 무시하는 건 아래의 방어력뿐이다.
        if (_status != null && _status.TotalShield > 0)
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
            if (!DamageRules.IgnoresDefense(info))
            {
                // 방어력 % 차감형. DEF 자체가 감소율이고, 상한 75% 는 게터에서 이미 잘려서 온다.
                // [26/07/17] 고정 방어력(FLAT_DEF)은 삭제 — 방어력은 % 하나로 일원화했다.
                finalDamage = Mathf.Max(remainingDamage * ((100f - _stat.DEF) / 100f), 1f);
            }

            curHP -= finalDamage;
            OnDamageTaken?.Invoke(finalDamage);

            string popupStr = "Normal";
            if (!string.IsNullOrEmpty(info.popupText))
                popupStr = info.popupText;

            TakeDamageEvent?.Invoke((int)finalDamage, info.type, popupStr, isCritical);

            // 상태이상 반응: 빙결 해제(+고정 피해), 출혈 추가 피해.
            // 상태이상 피해는 여기 못 들어온다 — 그래야 출혈의 +2 가 스스로를 트리거하는
            // 무한 재귀가 안 난다. 판정은 DamageRules.TriggersBleed 하나가 쥔다.
            if (_status != null && !isDead && DamageRules.TriggersBleed(info))
            {
                _status.OnDirectDamageTaken();
            }

            // 실제 데미지 피격 후 이벤트 트리거
            DamageEventBus.TriggerDamageReceived(this, info);

            // 연출 신호. '지금 맞았다'를 아는 건 여기뿐이라 여기서 쏜다. 듣는 쪽(카메라/체력바)이 알아서 반응한다.
            // 태그는 반드시 '루트'에서 본다 — CharacterHealth는 자식 CharacterStatStuff(Untagged)에 붙어 있어서
            // 이 오브젝트의 태그를 보면 영원히 false가 된다.
            if (transform.root.CompareTag("Player"))
                Signal.Fire(ShakeSignal.플레이어피격);
            else if (info.attacker != null && info.attacker.CompareTag("Player"))
                Signal.Fire(ShakeSignal.적피격);
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
        OnDeath?.Invoke();

        BaseEntity rootEntity = GetComponentInParent<BaseEntity>();
        bool isPlayer = gameObject.layer == Layers.Player; // (추가) 여기 뭔가 긴데 Player 인식 못 하길래 현재 오브젝트 Layer로 바꿈

        if (!isPlayer)
        {
            GameObject targetObj = rootEntity != null ? rootEntity.gameObject : gameObject;
            var deathHandler = targetObj.GetComponent<MonsterDeathHandler>();
            if (deathHandler != null)
            {
                deathHandler.Die(); // 죽음 애니메이션 재생 후 알아서 제거됨
            }
            else
            {
                Destroy(targetObj); // MonsterDeathHandler가 없는 오브젝트는 기존처럼 즉시 제거
            }
        }

        if(isPlayer)
        {
            GameManager.Instance.Gameover(); // 플레이어 사망 처리 (리스폰 등)
        }
    }

    public void TriggerDamagePopup(int amount, DamageType dmgType, string type, bool isCritical)
    {
        TakeDamageEvent?.Invoke(amount, dmgType, type, isCritical);
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

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
    private SpriteRenderer hiddenFinisherPose;
    private SpriteRenderer hitBody;

    internal SpriteRenderer HitBody
    {
        get
        {
            if (hitBody != null) return hitBody;
            var depth = WorldDepthOrder.FindParticipant(transform);
            if (depth != null) hitBody = depth.Renderer != null ? depth.Renderer : depth.GetComponent<SpriteRenderer>();
            if (hitBody == null && GetComponentInParent<BaseEntity>() is { } actor)
                hitBody = actor.SpriteRenderer != null ? actor.SpriteRenderer : actor.GetComponentInChildren<SpriteRenderer>(true);
            if (hitBody == null && GetComponentInParent<PlayerController>() is { } player)
                hitBody = player.transform.Find("Body")?.GetComponent<SpriteRenderer>();
            return hitBody;
        }
    }

    private void OnEnable()
    {
        if (hiddenFinisherPose != null) hiddenFinisherPose.forceRenderingOff = false;
        hiddenFinisherPose = null;
    }

    [SerializeField] private float curHP;
    [SerializeField] private bool isDead = false;
    [SerializeField] private bool invincible = false;

    public event Action<float> OnDamageTaken;
    public event Action<DamageInfo> OnDamageReceived; // 전체 데미지 정보 전달용
    public event Action OnHeal;
    /// <summary>최대 체력 제한까지 적용한 실제 회복량. 체력 변경 후 HUD 갱신 전에 알린다.</summary>
    public event Action<float> OnHealthRestored;
    public event Action UpdateHPBar;
    public event Action OnDeath;
    public event Func<CharacterHealth, bool> OnBeforeDeath; // 부활/페이즈 전환 시 사망 취소용

    public float CurHP => curHP;
    public float MaxHP => (_stat != null) ? _stat.MAXHP : 0f; // 최대 체력 정보 노출
    /// <summary>이 유닛의 스탯. 데미지 훅에서 "이 대상이 적인가"를 물어보려고 노출한다(ItemPouch 등).
    /// GetComponent 를 매 피해마다 돌리는 걸 피하려는 목적이니, 쓰기는 Init 을 통해서만 한다.</summary>
    public CharacterStat Stat => _stat;
    public bool IsDead => isDead;
    internal RoomFinalBlow Encounter { get; set; }

    private void OnDestroy()
    {
        Encounter?.Leave();
        Encounter = null;
    }
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
    ///  · 적 공격 → 등급(EnemyMinionDataSO.tier)   · 플레이어 공격 → 스킬
    /// 평타/대쉬/패링/디버프/함정은 소스에서 명시 태그되므로 여기로 안 온다.
    /// </summary>
    /// (public 인 이유: 우클릭 카운터/가드가 아직 GetDamage 에 들어오지 않은 히트박스의 갈래를
    ///  미리 알아야 한다. 판정 규칙이 두 벌로 갈리지 않게 반드시 이 함수를 쓴다.)
    public static DamageCategory ResolveCategoryFromAttacker(GameObject attacker)
    {
        if (attacker == null) return DamageCategory.None;

        // 플레이어 진영을 먼저 걸러낸다 — 플레이어에게도 CharacterStatus 가 붙어 있어서,
        // 등급부터 보면 플레이어 피해가 적 갈래로 새어 들어간다.
        if (attacker.layer == Layers.Player || attacker.layer == Layers.PlayerDash) return DamageCategory.Skill;

        // 적의 피해 갈래는 CharacterStatus의 등급(EnemyTier)에서 결정한다.
        var status = ResolveStatus(attacker);
        if (status != null)
        {
            if (status.Tier == EnemyTier.Boss) return DamageCategory.EnemyBoss;
            if (status.Tier == EnemyTier.Elite) return DamageCategory.EnemyElite;
        }

        if (attacker.layer == Layers.Enemy) return DamageCategory.EnemyMinion;

        return DamageCategory.None;
    }

    /// <summary>
    /// 공격자 오브젝트에서 CharacterStatus 를 찾는다. 적 프리팹은 스탯 3종을 루트가 아니라 자식
    /// 'CharacterStatStuff' 에 달고 있는데 공격자로 넘어오는 건 루트라서, GetComponentInParent
    /// 하나만으로는 <b>항상 null</b> 이었다 — 등급 판정이 죽어 있던 진짜 이유가 이것이다.
    /// 부모·자식 양방향으로 찾는다(프로젝트의 IDamageable 탐색과 같은 순서).
    /// </summary>
    private static CharacterStatus ResolveStatus(GameObject attacker)
        => attacker.GetComponent<CharacterStatus>()
        ?? attacker.GetComponentInParent<CharacterStatus>()
        ?? attacker.GetComponentInChildren<CharacterStatus>(true);

    /// <summary>
    /// 이 피해의 '공격 스탯' 주인을 찾는다 — 크리 확률/피해량, 물리·마법 증폭이 여기서 나온다.
    /// 보통은 공격자(또는 그 부모)의 CharacterStat 이지만, 미니언 스킬은 시전자(caster)가
    /// 필드에 스탯 없는 임시 오브젝트라 못 찾는다. 그럴 땐 플레이어 진영 피해에 한해
    /// 플레이어 스탯을 빌린다 — ATK 를 플레이어에서 빌리는 소환수 설계와 같은 이유다.
    /// </summary>
    private CharacterStat ResolveAttackerStat(DamageInfo info)
    {
        if (info.attacker != null)
        {
            var s = info.attacker.GetComponent<CharacterStat>() ?? info.attacker.GetComponentInParent<CharacterStat>();
            if (s != null) return s;
        }
        if (DamageRules.IsPlayerSourced(info.category)
            && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
            return GameManager.Instance.PLAYERCONTROLLER.Stat;
        return null;
    }

    public void GetDamage(DamageInfo info) => ApplyDamage(info);

    // 판정·이벤트 순서는 그대로 두고, 무적/회피로 끝난 접촉의 일반 타격 이펙트만 생략한다.
    internal bool ApplyDamage(DamageInfo info)
    {
        // 갈래가 안 붙은 피해는 공격자를 보고 자동 분류(적 티어 / 플레이어=스킬).
        if (info.category == DamageCategory.None)
            info.category = ResolveCategoryFromAttacker(info.attacker);

        OnDamageReceived?.Invoke(info); // AI 측에서 피격 상세 정보를 파악하기 위함

        if (isDead || invincible) return false;

        // 회피 / 명중 판정 (평타인 경우에만 적용)
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
                    return false;
                }
                // else (명중 성공) 시 아래의 데미지 파이프라인으로 계속 진행됨
            }
        }

        // 슈퍼아머 여부는 '깎기 전'에 읽는다. 이 순서가 중요하다 —
        // 예전엔 깎은 뒤에 읽어서, 슈퍼아머를 부순 바로 그 일격이 넉백/경직까지 같이 넣었다.
        // (게이지가 0이 되는 순간 _hasSuperArmor 가 false 로 바뀌므로)
        // 슈퍼아머는 '부서지기 전까지' 막아주는 것이니, 부순 타격 자체는 아직 막혀야 한다.
        bool hasSuperArmor = (_status != null && _status.HasSuperArmor);

        // 슈퍼아머 게이지 차감 처리
        if (hasSuperArmor && info.superArmorDamage > 0f)
        {
            _status.DamageSuperArmor(info.superArmorDamage);
        }

        // 경직(Hitstun) 및 넉백(Knockback) 처리
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
                    float force = info.knockbackForce;
                    if (info.playerBasicAttack != null && info.attacker.TryGetComponent<MeleeCombatController>(out var melee))
                    {
                        var settings = CameraSettingsSO.Instance;
                        var shape = rootEntity.GetComponent<Collider2D>();
                        Vector2 origin = info.attacker.transform.position;
                        float gap = Vector2.Distance(origin, shape != null ? shape.ClosestPoint(origin) : (Vector2)transform.position);
                        float radius = settings != null ? settings.singleEnemyContactRadius : .42f;
                        if (radius > 0f) force *= 1f + (settings != null ? settings.contactKnockbackBonus : .15f) * Mathf.Clamp01(1f - gap / radius);
                        if (dir.sqrMagnitude < .0001f) dir = melee.CurrentAttackDir;
                    }
                    rootEntity.ApplyKnockback(dir * force);
                }
            }
        }

        // 공격자(info.attacker)가 사자탈(DualSplitAIPatternSO)을 쓰는 몹이고 타격이 정상 명중한 경우 슬로우 디버프 인젝션
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

        float remainingDamage = CalculateIncomingDamage(ref info, out bool isCritical, out var attackStat);
        if (remainingDamage > 0f)
            DamageEventBus.TriggerAttackHitResolved(this, attackStat, info, isCritical);

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
            float finalDamage = DamageRules.ApplyDefense(remainingDamage, info, _stat != null ? _stat.DEF : 0f);

            if (finalDamage > 0f && !hasSuperArmor && DamageRules.IsEnemyTier(info.category) &&
                info.category != DamageCategory.EnemyDebuff && GetComponentInParent<PlayerController>() is { } player)
            {
                Vector2? from = info.hitFrom ?? (info.attacker != null ? (Vector2?)info.attacker.transform.position : null);
                if (from.HasValue && player.ReactToHit((Vector2)player.transform.position - from.Value))
                    _status?.ApplyStatus(StatusType.Hitstun, .245f);
            }
            curHP -= finalDamage;
            OnDamageTaken?.Invoke(finalDamage);

            string popupStr = info.feedbackComboFinish ? "Combo" : "Normal";
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

        // [상태이상 부여] 소환수 대쉬/피니셔/R 이 onHitStatus 를 DamageInfo.applyStatus 로 실어 보내면 여기서 건다.
        // ⚠ 반드시 위 데미지 반응(OnDirectDamageTaken) '뒤'다 — 앞에 두면 방금 건 빙결을 같은 타격이 곧바로
        // 깨버린다(적용 히트가 자기 빙결을 산산조각). 여기 오면: 이전 타격의 빙결은 위에서 정상 shatter 되고,
        // 이번에 새로 거는 빙결만 남는다. 슈퍼아머는 이동방해 CC(기절/빙결) 차단, 출혈/중독은 통과.
        // curHP>0: 이 타격에 죽은 대상은 안 건다. 내성 중이면 ApplyStatus 내부에서 씹힌다.
        if (info.applyStatus.HasValue && _status != null && curHP > 0f)
        {
            StatusType st = info.applyStatus.Value;
            if (st != StatusType.None && !(hasSuperArmor && StatusRules.BlockedBySuperArmor(st)))
            {
                // 출혈은 공격자의 공격력(물리 증폭 포함)을 기억한다. 미니언 스킬은 플레이어 스탯을 빌린다(ResolveAttackerStat).
                float power = attackStat != null ? attackStat.ATK * (1f + attackStat.PHYS_AMP) : 0f;
                _status.ApplyStatus(st, info.statusDuration, power: power);
            }
        }

        // [장비 상태이상 발동] 플레이어 Q/E 스킬 타격이면 착용 장비의 상태이상 확률을 굴려 부여(얼음 건틀릿 등).
        // 스킬만(category==Skill) + 플레이어 것만(attacker==플레이어; 미니언 R 은 attacker=caster 라 제외). 위 블록과 같은 '데미지 뒤' 위치라 빙결 자가붕괴 없음.
        if (info.category == DamageCategory.Skill && _status != null && curHP > 0f
            && info.attacker != null && GameManager.Instance != null
            && GameManager.Instance.PLAYERCONTROLLER != null
            && info.attacker == GameManager.Instance.PLAYERCONTROLLER.gameObject
            && EquipmentInventoryManager.Instance != null)
        {
            EquipmentInventoryManager.Instance.TryProcEquipmentStatus(_status);
        }

        // [사망] 체크
        if (curHP <= 0.0f && !isDead) // isDead 체크로 중복 호출 방지
        {
            if (OnBeforeDeath != null && OnBeforeDeath.Invoke(this))
            {
                return true; // 페이즈 전환 등으로 죽음을 회피함
            }
            // 사망 직전 이벤트
            GetComponentInParent<MonsterDeathHandler>()?.BeginDeathPositionTrace();
            hiddenFinisherPose = FinisherFlight.Play(this, info, isCritical || info.feedbackComboFinish || remainingDamage >= MaxHP * .4f);
            DamageEventBus.TriggerEntityDied(this, info);
            Die(info);
        }

        UpdateHPBar?.Invoke(); // HPBar 업데이트
        return true;
    }

    private float CalculateIncomingDamage(ref DamageInfo info, out bool isCritical, out CharacterStat attacker)
    {
        DamageEventBus.TriggerBeforeDamageCalculated(this, ref info);
        attacker = ResolveAttackerStat(info);
        return DamageRules.CalculateBeforeDefense(info, attacker,
            _status != null && _status.HasStatus(StatusType.Vulnerable), out isCritical);
    }

    public void Heal(float amount)
    {
        if (isDead) return;

        float healAmount = amount;

        // 힐 파이프라인: 힐량 증폭/계산 전 이벤트
        DamageEventBus.TriggerBeforeHealCalculated(this, ref healAmount);

        float oldHP = curHP;

        curHP = Mathf.Min(curHP + healAmount, _stat.MAXHP);
        if(curHP>oldHP)
        {
            PolishFeedback.Play(FeedbackCue.Heal,transform.position,Vector2.up)?.FollowOrigin(transform);
            OnHealthRestored?.Invoke(curHP-oldHP);
        }

        Debug.Log($"{gameObject.name} healed for {healAmount}. HP: {oldHP} -> {curHP}");
        TakeHealEvent?.Invoke(healAmount); // 힐 텍스트 띄우기
        OnHeal?.Invoke();
        UpdateHPBar?.Invoke(); // HPBar 업데이트
    }

    private void Die(DamageInfo hit)
    {
        if (isDead) return; // 중복 실행 방지
        isDead = true;

        // [비폭] 사망 시 최우선 발동
        OnDeath?.Invoke();
        // Split enemies register their children during OnDeath.
        BaseEntity rootEntity = GetComponentInParent<BaseEntity>();
        if (Encounter != null)
        {
            Vector2 point = HitBody != null ? (Vector2)HitBody.bounds.center : (Vector2)transform.position;
            Vector2 from = hit.hitFrom ?? (hit.attacker != null ? (Vector2)hit.attacker.transform.position : point - Vector2.up);
            Encounter.Killed(point, point - from, rootEntity != null && rootEntity.MinionData != null && rootEntity.MinionData.tier == EnemyTier.Boss);
            Encounter = null;
        }
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

using System.Collections.Generic;
using UnityEngine;

public class CharacterStatus : MonoBehaviour
{
    private class SlowInstance { public string EffectId; public float Reduction; public float EndTime; }
    private class SpeedBuffInstance { public string EffectId; public float Increase; public float EndTime; }
    private class ShieldInstance { public float RemainingAmount; public float EndTime; public ShieldInstance(float amount, float duration){ RemainingAmount = amount; EndTime = Time.time + duration; }}

    private List<SlowInstance> _activeSlows = new List<SlowInstance>();
    private List<SpeedBuffInstance> _activeSpeedBuffs = new List<SpeedBuffInstance>();
    private List<ShieldInstance> _shieldInstances = new List<ShieldInstance>();
    private float _cachedMoveSpeedMultiplier = 1f;
    private float _cachedTotalShield = 0f;

    public bool LastAttackMissed = false; // [추가] 궁수 발이부중 기믹용 (이전 기본 공격이 빗나갔는지 여부)
    public float MoveSpeedMultiplier => _cachedMoveSpeedMultiplier;
    public float TotalShield => _cachedTotalShield;
    public bool IsElite { get; set; } // [추가] 엘리트 유닛 여부

    [Header("Super Armor Settings")]
    [SerializeField] private bool _hasSuperArmor = false;
    [SerializeField] private float _superArmorGauge = 0f;
    [SerializeField] private float _maxSuperArmorGauge = 100f;

    public bool HasSuperArmor => _hasSuperArmor;
    public float SuperArmorGauge => _superArmorGauge;
    public float MaxSuperArmorGauge => _maxSuperArmorGauge;

    public void ApplySuperArmor(float amount = 100f)
    {
        _hasSuperArmor = true;
        _superArmorGauge = amount;
        _maxSuperArmorGauge = amount;
    }

    /// <summary>
    /// 슈퍼아머 게이지를 깎는다. 0 이 되면 그대로 부서지고 끝 — 추가 보상은 없다.
    /// 재생도 없다. 한 번 부서지면 이 유닛이 죽을 때까지 돌아오지 않는다(설계 확정).
    /// </summary>
    public void DamageSuperArmor(float amount)
    {
        if (!_hasSuperArmor) return;
        _superArmorGauge = Mathf.Max(0f, _superArmorGauge - amount);
        if (_superArmorGauge <= 0f)
        {
            _hasSuperArmor = false;
            Debug.Log($"<color=red>[Super Armor]</color> {gameObject.name}의 슈퍼아머 파괴!");
        }
    }

    // ── 상태이상 컨테이너 ─────────────────────────────────────────────
    // 5종이 독립적으로 동시 존재한다. 구 구조는 슬롯이 하나(_currentDebuffType)라 다른 걸
    // 걸면 기존 게 터지고 새 건 증발했는데, 그 규칙이 통째로 사라졌다.
    private class StatusInstance
    {
        public float EndTime;       // 만료 시각. 재적중하면 갱신된다.
        public int Stacks;          // 비폭만 쓴다.
        public float NextTickTime;  // 중독 전용. '절대 격자' — 아래 UpdateStatuses 주석 참조.
    }
    private readonly Dictionary<StatusType, StatusInstance> _statuses = new Dictionary<StatusType, StatusInstance>();

    // 상태이상별 '이 시각까지 다시 안 걸림' 내성. 기절은 자연 종료 후, 빙결은 피해로 깨진 후에만 채워진다.
    private readonly Dictionary<StatusType, float> _immuneUntil = new Dictionary<StatusType, float>();

    /// <summary>상태이상이 터졌을 때 띄울 한글 라벨. FloatingTextSpawner 가 듣는다.</summary>
    public event System.Action<StatusVisual> OnDebuffPopped;

    [Header("비폭 폭발")]
    [Tooltip("비폭이 터질 때 쓸 원형 히트박스. 비우면 폭발이 피해를 못 준다. " +
             "(Center Skill Hitbox Circle Prefab — 콜라이더 반지름 0.5 전제)")]
    [SerializeField] private BaseHitBox bloodPopExplosionPrefab;

    [SerializeField] private Base_DebuffUITerminal debuffTerminal;

    private CharacterStat _stat;
    public static List<CharacterStatus> ActiveEnemies = new List<CharacterStatus>();
    public static List<CharacterStatus> ActiveAllies = new List<CharacterStatus>();

    public void Init(CharacterStat stat)
    {
        _stat = stat;
        if (IsEnemyTarget)
        {
            if (!ActiveEnemies.Contains(this)) ActiveEnemies.Add(this);
        }
        else
        {
            if (!ActiveAllies.Contains(this)) ActiveAllies.Add(this);
        }
    }

    private void OnDestroy()
    {
        if (ActiveEnemies.Contains(this))
        {
            ActiveEnemies.Remove(this);
        }
        if (ActiveAllies.Contains(this))
        {
            ActiveAllies.Remove(this);
        }
    }

    private bool IsEnemyTarget => _stat != null && _stat.IsEnemy;

    private void Update()
    {
        UpdateInstances();
        UpdateStatuses();
    }

    private void UpdateInstances()
    {
        float multiplier = 1.0f;
        for (int i = _activeSlows.Count - 1; i >= 0; i--)
        {
            if (Time.time > _activeSlows[i].EndTime) { _activeSlows.RemoveAt(i); continue; }
            multiplier *= (1.0f - _activeSlows[i].Reduction);
        }
        for (int i = _activeSpeedBuffs.Count - 1; i >= 0; i--)
        {
            if (Time.time > _activeSpeedBuffs[i].EndTime) { _activeSpeedBuffs.RemoveAt(i); continue; }
            multiplier *= (1.0f + _activeSpeedBuffs[i].Increase);
        }

        _cachedMoveSpeedMultiplier = Mathf.Max(0.1f, multiplier);

        float sum = 0;
        for (int i = _shieldInstances.Count - 1; i >= 0; i--)
        {
            if (Time.time > _shieldInstances[i].EndTime || _shieldInstances[i].RemainingAmount <= 0) 
            { 
                if (Time.time > _shieldInstances[i].EndTime && _shieldInstances[i].RemainingAmount > 0)
                {
                    ExplodeExpiredShield(_shieldInstances[i].RemainingAmount);
                }
                _shieldInstances.RemoveAt(i); 
                continue; 
            }
            sum += _shieldInstances[i].RemainingAmount;
        }
        _cachedTotalShield = sum;
    }

    private static readonly List<StatusType> _statusScratch = new List<StatusType>();

    private void UpdateStatuses()
    {
        if (_statuses.Count == 0) return;

        _statusScratch.Clear();
        _statusScratch.AddRange(_statuses.Keys); // 순회 중 제거하므로 키를 복사해서 돈다

        foreach (var type in _statusScratch)
        {
            if (!_statuses.TryGetValue(type, out var inst)) continue;

            // 중독 틱. '절대 격자'라 재적중해도 다음 틱이 밀리지 않는다.
            // 예약 시각을 '지금 + 주기'로 다시 잡으면, 다단히트가 1초에 5번 갱신할 때
            // 틱이 계속 뒤로 밀려서 중독 피해가 하나도 안 들어간다. (BaseHitBox 에서 똑같은
            // 드리프트 버그를 이미 한 번 잡았다 — 같은 함정이다.)
            if (type == StatusType.Poison && Time.time >= inst.NextTickTime)
            {
                inst.NextTickTime += StatusRules.POISON_TICK_INTERVAL;
                DealSelfDamage(StatusRules.POISON_TICK_DAMAGE, DamageType.Poison, "중독");
            }

            if (Time.time >= inst.EndTime)
            {
                // 기절이 자연 종료되면 3초 내성이 붙는다. 빙결은 자연 만료엔 안 붙는다
                // (피해로 깨졌을 때만 — OnDirectDamageTaken 참조).
                if (type == StatusType.Stun)
                    _immuneUntil[StatusType.Stun] = Time.time + StatusRules.STUN_IMMUNITY;
                RemoveStatus(type);
            }
        }
    }

    /// <summary>자기 자신에게 상태이상 피해를 넣는다. 공격자는 없다(도트라 귀속이 애매함).</summary>
    private void DealSelfDamage(float amount, DamageType type, string popup)
    {
        var health = GetComponent<CharacterHealth>() ?? GetComponentInChildren<CharacterHealth>();
        if (health != null && !health.IsDead)
            health.GetDamage(new DamageInfo(amount, type, null, 1f, popup, category: DamageCategory.Debuff));
    }

    public void ApplySlow(string id, float reduction, float duration)
    {
        var existing = _activeSlows.Find(s => s.EffectId == id);
        if (existing != null) { existing.Reduction = Mathf.Max(existing.Reduction, reduction); existing.EndTime = Time.time + duration; }
        else { _activeSlows.Add(new SlowInstance { EffectId = id, Reduction = reduction, EndTime = Time.time + duration }); }
    }

    public void ApplySpeedBuff(string id, float increase, float duration)
    {
        var existing = _activeSpeedBuffs.Find(s => s.EffectId == id);
        if (existing != null) { existing.Increase = Mathf.Max(existing.Increase, increase); existing.EndTime = Time.time + duration; }
        else { _activeSpeedBuffs.Add(new SpeedBuffInstance { EffectId = id, Increase = increase, EndTime = Time.time + duration }); }
    }

    public void AddShield(float amount, float duration) { _shieldInstances.Add(new ShieldInstance(amount, duration)); UpdateInstances(); }

    private void ExplodeExpiredShield(float amount)
    {
        if (IsEnemyTarget || InventoryManager.Instance == null) return;
        
        // [이벤트 버스] 쉴드 폭발 시 추가 이펙트 처리 (추후 연동 시 여기에 이벤트 추가)
    }

    public float ConsumeShield(float amount)
    {
        float remainingToConsume = amount;
        for (int i = 0; i < _shieldInstances.Count; i++)
        {
            float canTake = Mathf.Min(remainingToConsume, _shieldInstances[i].RemainingAmount);
            _shieldInstances[i].RemainingAmount -= canTake;
            remainingToConsume -= canTake;
            if (remainingToConsume <= 0) break;
        }
        UpdateInstances(); 
        return amount - remainingToConsume;
    }

    public void ApplyKnockback(Vector2 dir, float force, float duration = 0.15f, bool isIronMountain = false)
    {
        if (HasSuperArmor) return; // [추가] 슈퍼아머 상태일 때는 넉백 불가
        Rigidbody2D rb = GetComponentInParent<Rigidbody2D>();
        if (rb != null) StartCoroutine(KnockbackRoutine(rb, dir, force, duration, isIronMountain));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Rigidbody2D rb, Vector2 dir, float force, float duration, bool isIronMountain)
    {
        bool isPlayer = gameObject.CompareTag("Player");
        bool hasVanguard = false;
        
        // [이벤트 버스] 넉백 전처리: force, duration 변조 기능 추가 가능

        float knockbackSpeed = force * 2.0f;
        float elapsed = 0f;
        int wallMask = Layers.WallMask;

        while (elapsed < duration)
        {
            if (rb == null) yield break;
            rb.linearVelocity = dir * knockbackSpeed;
            elapsed += Time.deltaTime;

            if (isIronMountain && !isPlayer)
            {
                Collider2D wallHit = Physics2D.OverlapCircle(transform.position, 0.4f, wallMask);
                if (wallHit != null)
                {
                    float damagePercent = IsElite ? 0.06f : 0.12f;
                    var health = GetComponentInChildren<CharacterHealth>();
                    if (health != null) health.GetDamage(new DamageInfo(health.CurHP * damagePercent, DamageType.Fixed, null, category: DamageCategory.Debuff));
                    break;
                }
            }

            if (hasVanguard)
            {
                Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, 1.0f);
                foreach(var hit in hits)
                {
                    int layerVal = hit.gameObject.layer;
                    bool isEnemy = layerVal == Layers.Enemy ||
                                   hit.CompareTag("Boss") ||
                                   (hit.TryGetComponent<BaseEntity>(out var entEnemy) && entEnemy.team == Team.Enemy);
                    if (isEnemy && hit.TryGetComponent<CharacterStat>(out var enemyStat))
                    {
                        var pc = GameManager.Instance.PLAYERCONTROLLER;
                        float damage = 10f;
                        if (pc != null)
                        {
                            var pcStat = pc.GetComponent<CharacterStat>();
                            if (pcStat != null) damage = pcStat.ATK * 1.5f;
                        }
                        enemyStat.Health.GetDamage(new DamageInfo(damage, DamageType.Physical, gameObject, category: DamageCategory.Debuff));
                    }
                    bool isAlly = layerVal == Layers.Player || 
                                  layerVal == Layers.Army || 
                                  layerVal == Layers.FlyingObject ||
                                  (hit.TryGetComponent<BaseEntity>(out var entAlly) && entAlly.team == Team.Ally);
                    if (isAlly && hit.TryGetComponent<CharacterStatus>(out var allyStat))
                    {
                        // Vanguard 버프 로직 (간단히 이속 버프로 대체 가능)
                        // TODO: 회피율 및 이속 버프
                    }
                }
            }

            yield return null;
        }
        if (rb != null) rb.linearVelocity = Vector2.zero;
    }

    // ── 상태이상 API ─────────────────────────────────────────────────

    /// <summary>
    /// 상태이상을 건다. 이미 걸려 있으면 지속시간이 갱신되고, 스택형(비폭)이면 스택이 쌓인다.
    /// 슈퍼아머가 있으면 이동 방해 계열(기절/빙결)은 씹힌다 — 저장되지 않으므로 나중에 다시 걸어야 한다.
    /// </summary>
    /// <param name="duration">0 이하면 StatusRules.DURATION(5초)을 쓴다. 경직처럼 짧은 건 직접 넘긴다.</param>
    /// <summary>
    /// 고정 스턴(그로기). 슈퍼아머를 무시하고 반드시 들어간다.
    ///
    /// 일반 CC 와 구분해서 쓴다: 이건 <b>남이 거는 CC 가 아니라 유닛이 스스로 자초한 경직</b>이다.
    /// 엘리트 차저가 돌진하다 벽에 처박는 게 대표적인데, 그건 플레이어가 회피에 성공해서 얻어낸
    /// 보상이라 슈퍼아머(엘리트는 게이지 999999 = 사실상 무한)로 씹히면 패턴 자체가 성립하지 않는다.
    ///
    /// StatusType 을 새로 만들지 않고 Stun 을 그대로 쓰는 이유: 아이콘/타이머/행동불가 판정/내성이
    /// 전부 이미 Stun 에 붙어 있고, 플레이어 입장에서 보이는 것도 똑같은 '기절'이다.
    /// 다른 건 "슈퍼아머가 막느냐" 하나뿐이라 타입이 아니라 경로를 나누는 게 맞다.
    /// </summary>
    public void ApplyFixedStun(float duration)
        => ApplyStatus(StatusType.Stun, duration, 1, bypassSuperArmor: true);

    /// <param name="bypassSuperArmor">
    /// 슈퍼아머를 무시하고 건다. '남이 거는 CC'가 아니라 <b>자기가 자초한 그로기</b>에만 쓴다 —
    /// 엘리트 차저가 돌진하다 벽에 처박았을 때가 그렇다. 그건 플레이어가 회피에 성공해서 얻어낸
    /// 보상이지 CC 가 아닌데, 슈퍼아머(엘리트는 게이지 999999)가 통째로 씹어버려서 회피 보상이
    /// 아예 발생하지 않았다.
    /// </param>
    public void ApplyStatus(StatusType type, float duration = 0f, int stacks = 1, bool bypassSuperArmor = false)
    {
        if (_stat != null && _stat.IsDead) return;
        if (!bypassSuperArmor && _hasSuperArmor && StatusRules.BlockedBySuperArmor(type)) return;
        // 내성: 기절 종료 후 3초 / 빙결이 피해로 깨진 후 1초 동안은 다시 안 걸린다.
        if (_immuneUntil.TryGetValue(type, out var immuneEnd) && Time.time < immuneEnd) return;

        // [재빙결] 이미 빙결된 적에게 빙결이 '또 부여'되면 아래에서 지속시간만 갱신된다. 부가 효과는 없다.
        //   (혼동 주의: 얼린 적을 '때려서' 깨는 건 별개로 살아 있다 — OnDirectDamageTaken 의
        //    FREEZE_BREAK_DAMAGE 경로. 여기는 피해가 아니라 '부여 중복' 경로다.)
        // ⚠ 여기에 재빙결 시 추가 피해를 넣을 거면 주의: 자해 피해 → OnDirectDamageTaken 이 빙결을
        //   해제/내성부여하고, 이어서 이 ApplyStatus 가 다시 빙결을 재적용하는 재진입이 생긴다.

        // 타입별 기본 지속시간. 기절만, 소스가 명시한 값을 0.5~2초로 clamp 한다.
        if (duration <= 0f) duration = StatusRules.DefaultDuration(type);
        else if (type == StatusType.Stun) duration = Mathf.Clamp(duration, StatusRules.STUN_MIN, StatusRules.STUN_MAX);

        // 이번 호출로 '새로' 걸린 것인지. 재적중(지속시간 갱신)에는 텍스트를 안 띄운다 —
        // 다단히트 스킬이 1초에 다섯 번 갱신하면 "빙결!"이 다섯 번 겹쳐 뜬다.
        bool isNewlyApplied = !_statuses.TryGetValue(type, out var inst); // 조회 한 번으로 겸함

        
if (isNewlyApplied)
        {
            inst = new StatusInstance();
            _statuses[type] = inst;
            // 중독의 첫 틱은 '건 시점 + 주기'다. 여기서만 잡고, 갱신 때는 절대 안 건드린다.
            inst.NextTickTime = Time.time + StatusRules.POISON_TICK_INTERVAL;
        }

        inst.EndTime = Time.time + duration; // 재적중 = 지속시간만 갱신

        if (StatusRules.IsStacking(type))
        {
            inst.Stacks += stacks;
            if (inst.Stacks >= StatusRules.BLOODPOP_THRESHOLD)
            {
                DetonateBloodPop();
                return;
            }
        }

        debuffTerminal?.UpdateUI(type, inst.Stacks);

        // 걸린 순간의 알림 텍스트. 비폭은 여기 못 온다 — 위 임계치 분기에서 return 되고,
        // 터질 때 DetonateBloodPop 이 따로 띄운다.
        if (isNewlyApplied && StatusRules.AnnouncesOnApply(type))
            OnDebuffPopped?.Invoke(StatusEffectPalette.FromStatus(type));
    }

    public bool HasStatus(StatusType type) => _statuses.ContainsKey(type);

    /// <summary>
    /// 상태이상이 하나라도 걸려 있는가. 매 프레임 도는 시각 효과(CharacterVisualFeedback)가
    /// 아무것도 안 걸린 흔한 경우를 int 비교 한 번으로 빠져나가기 위한 통로다.
    /// </summary>
    public bool HasAnyStatus => _statuses.Count > 0;

    public int GetStacks(StatusType type)
        => _statuses.TryGetValue(type, out var inst) ? inst.Stacks : 0;

    public void RemoveStatus(StatusType type)
    {
        if (!_statuses.Remove(type)) return;
        debuffTerminal?.RemoveIcon(type);
    }

    /// <summary>행동이 막혀 있는가. 기절/빙결/경직 중 하나라도 걸려 있으면 true.</summary>
    public bool IsActionBlocked
    {
        get
        {
            foreach (var kv in _statuses)
                if (StatusRules.PreventsAction(kv.Key)) return true;
            return false;
        }
    }

    // ── 피격 훅 (CharacterHealth 가 부른다) ───────────────────────────

    /// <summary>
    /// 직접 피해를 받았을 때 상태이상이 반응하는 지점. 빙결 해제와 출혈 추가 피해가 여기서 나간다.
    ///
    /// [철칙] 상태이상 피해(Fixed 계열)는 여기 못 들어온다 — DamageRules.TriggersBleed 가 막는다.
    /// 안 그러면 출혈의 +2 가 스스로를 트리거해서 무한 재귀가 난다.
    /// 같은 이유로 중독 틱이 빙결을 깨지도 않는다(얼려놓자마자 도트가 깨버리면 이상하다).
    /// </summary>
    public void OnDirectDamageTaken()
    {
        // 빙결: 맞으면 고정 피해를 터뜨리고 즉시 풀린다.
        if (HasStatus(StatusType.Freeze))
        {
            RemoveStatus(StatusType.Freeze);
            // 피해로 깨진 경우에만 1초 내성. 자연 만료(리힛 없이 2.5초)엔 안 붙는다.
            _immuneUntil[StatusType.Freeze] = Time.time + StatusRules.FREEZE_IMMUNITY;
            OnDebuffPopped?.Invoke(StatusVisual.FreezeBreak);
            DealSelfDamage(StatusRules.FREEZE_BREAK_DAMAGE, DamageType.Freeze, "빙결");
        }

        // 출혈: 맞을 때마다 추가 고정 피해. 한 방에 여러 피해가 겹쳐도 1회다.
        if (HasStatus(StatusType.Bleed))
        {
            DealSelfDamage(StatusRules.BLEED_HIT_DAMAGE, DamageType.Bleed, "출혈");
        }
    }

    /// <summary>
    /// 비폭 폭발. 자신과 주변에 고정 피해를 주고 스택을 0 으로 되돌린다.
    /// [철칙] 폭발 피해는 DamageType.BloodPop 이라 다른 적의 비폭 스택을 쌓지 않는다 —
    /// 안 그러면 연쇄 폭발이 무한히 번진다.
    /// </summary>
    private void DetonateBloodPop()
    {
        RemoveStatus(StatusType.BloodPop);
        OnDebuffPopped?.Invoke(StatusVisual.BloodPop);

        if (bloodPopExplosionPrefab == null)
        {
            Debug.LogWarning($"[비폭] {gameObject.name}: bloodPopExplosionPrefab 이 비어 있어 폭발이 피해를 못 줍니다.");
            return;
        }

        // 함정(TrapBombBarrel)과 같은 방식: 히트박스를 미리 띄우고 startDelay 동안 빨간 원이
        // 차오르게 해서 예고한다. 프리팹 콜라이더 반지름이 0.5 인 걸 전제로 스케일을 잡는다.
        var box = Instantiate(bloodPopExplosionPrefab, transform.position, Quaternion.identity);
        box.transform.localScale = new Vector3(StatusRules.BLOODPOP_RADIUS * 2f, StatusRules.BLOODPOP_RADIUS * 2f, 1f);

        // 폭발은 '터진 유닛이 속한 진영의 반대'가 아니라 그 유닛과 같은 진영을 친다 —
        // 적에게 걸린 비폭이니 적을 때린다. 자신도 범위에 들어가므로 같이 맞는다.
        LayerMask mask = IsEnemyTarget ? Layers.EnemyMask : Layers.PlayerArmy;
        var info = new DamageInfo(StatusRules.BLOODPOP_DAMAGE, DamageType.BloodPop, null, 1f, "비폭", category: DamageCategory.Debuff);
        box.Init(info, mask, 0.2f, StatusRules.BLOODPOP_FUSE, isAlly: !IsEnemyTarget);
    }



    public void ClearStatus()
    {
        _activeSlows.Clear(); _activeSpeedBuffs.Clear(); _shieldInstances.Clear();
        _statuses.Clear();
        _immuneUntil.Clear(); // 오브젝트 재사용 시 이전 유닛의 내성을 물려받지 않도록
        _cachedMoveSpeedMultiplier = 1f; _cachedTotalShield = 0f;

        // 슈퍼아머도 여기서 지운다. 예전엔 안 지워서, 오브젝트가 재사용되면 이전 유닛의
        // 슈퍼아머를 그대로 물려받았다.
        _hasSuperArmor = false; _superArmorGauge = 0f;

        // UI 갱신
        debuffTerminal?.RemoveAll();
    }
}
